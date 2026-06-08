using VideoTriage.Core.Encoding;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Replace;
using VideoTriage.Core.State;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Pipeline;

/// <summary>
/// Orchestrates discovery → probe → classify → space → encode → verify → size-check → safe-replace,
/// emitting immutable <see cref="FileProgress"/> events. Every destructive step is gated by
/// verification and a strict size comparison, and every discovered file receives a terminal event.
/// The original is never touched on any failure path.
/// </summary>
public sealed class TriagePipeline(
    IVideoFileDiscovery discovery,
    IFfprobeService ffprobe,
    IVideoClassifier classifier,
    IVideoEncoder encoder,
    IOutputVerifier verifier,
    ISafeReplacer replacer,
    IFileSystem fileSystem,
    Func<string, ICompletedFileStore> completedStoreFactory,
    Func<string, IDeleteManifest> deleteManifestFactory,
    Func<string, IResultLog> resultLogFactory) : ITriagePipeline
{
    public async Task<TriageSummary> RunAsync(
        string folder,
        TriageOptions options,
        bool recursive = false,
        IProgress<FileProgress>? progress = null,
        PauseToken? pauseToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var results = new List<FileProgress>();
        var dataDirectory = Path.Combine(folder, options.DataDirectoryName);
        ICompletedFileStore? completedStore = null;
        IDeleteManifest? deleteManifest = null;
        IResultLog? resultLog = null;
        var completedByPath = new Dictionary<string, CompletedFileEntry>(StringComparer.OrdinalIgnoreCase);

        if (!options.DryRun)
        {
            fileSystem.CreateDirectory(dataDirectory);
            completedStore = completedStoreFactory(dataDirectory);
            deleteManifest = deleteManifestFactory(dataDirectory);
            resultLog = resultLogFactory(dataDirectory);

            foreach (var entry in completedStore.Load())
                completedByPath[Path.GetFullPath(entry.SourcePath)] = entry;
        }

        void Report(string path, TriagePhase phase, double? encodeProgress = null) =>
            progress?.Report(new FileProgress { FilePath = path, Phase = phase, EncodeProgress = encodeProgress });

        void Complete(
            string path,
            TriageOutcome outcome,
            string reason,
            string? finalPath = null,
            VideoStats? source = null,
            DateTimeOffset? sourceLastWrite = null,
            long? outputBytes = null,
            double? savedPercent = null,
            ReplaceResult? replace = null,
            bool persist = true)
        {
            var terminal = new FileProgress
            {
                FilePath = path,
                Phase = TriagePhase.Done,
                Outcome = outcome,
                Message = reason,
                FinalPath = finalPath,
                Source = source,
                OutputBytes = outputBytes,
                SavedPercent = savedPercent
            };
            results.Add(terminal);
            progress?.Report(terminal);

            if (!persist || options.DryRun)
                return;

            resultLog?.Append(new ResultLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                SourcePath = path,
                Outcome = outcome,
                Message = reason,
                SourceBytes = source?.FileSizeBytes,
                OutputBytes = outputBytes,
                SavedPercent = savedPercent,
                FinalPath = finalPath
            });

            if (source is not null &&
                sourceLastWrite.HasValue &&
                outcome is TriageOutcome.Replaced
                    or TriageOutcome.ReplacePartial
                    or TriageOutcome.GrewKeptOriginal
                    or TriageOutcome.SkippedAlreadyAv1
                    or TriageOutcome.SkippedLowBpp)
            {
                completedStore?.Append(new CompletedFileEntry
                {
                    SourcePath = path,
                    SourceLength = source.FileSizeBytes,
                    SourceLastWriteUtc = sourceLastWrite.Value,
                    Outcome = outcome,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                });
            }

            if (replace is { OriginalRemoved: true } &&
                source is not null &&
                finalPath is not null &&
                outputBytes.HasValue &&
                savedPercent.HasValue)
            {
                deleteManifest?.Append(new DeleteManifestEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    DeleteMode = options.DeleteMode,
                    OriginalPath = path,
                    OriginalBytes = source.FileSizeBytes,
                    ReplacementPath = finalPath,
                    ReplacementBytes = outputBytes.Value,
                    SavedPercent = savedPercent.Value
                });
            }
        }

        foreach (var path in discovery.FindVideos(folder, options, recursive))
        {
            Report(path, TriagePhase.Discovered);
            await WaitWhilePausedAsync(pauseToken, cancellationToken);

            if (completedByPath.TryGetValue(Path.GetFullPath(path), out var prior) &&
                fileSystem.FileExists(path) &&
                fileSystem.GetFileLength(path) == prior.SourceLength &&
                fileSystem.GetLastWriteTimeUtc(path) == prior.SourceLastWriteUtc)
            {
                Complete(
                    path,
                    TriageOutcome.AlreadyCompleted,
                    "Already completed in a prior run.",
                    persist: false);
                continue;
            }

            Report(path, TriagePhase.Probing);
            var probe = await ffprobe.ProbeAsync(path, cancellationToken);
            if (!probe.Succeeded || probe.Stats is null)
            {
                Complete(path, TriageOutcome.InvalidMetadata, probe.Failure?.Message ?? "Probe failed.");
                continue;
            }

            var sourceLastWrite = fileSystem.GetLastWriteTimeUtc(path);

            Report(path, TriagePhase.Classified);
            var classification = classifier.Classify(probe.Stats, options);
            if (!classification.IsCandidate)
            {
                Complete(
                    path,
                    MapSkip(classification.Outcome),
                    classification.Reason,
                    source: probe.Stats,
                    sourceLastWrite: sourceLastWrite);
                continue;
            }

            if (options.DryRun)
            {
                Complete(path, TriageOutcome.DryRunCandidate, "Dry-run candidate.", source: probe.Stats);
                continue;
            }

            Report(path, TriagePhase.WaitingForSpace);
            var needed = Math.Max(
                (long)(options.MinimumFreeGigabytes * 1024 * 1024 * 1024),
                probe.Stats.FileSizeBytes);
            if (fileSystem.GetAvailableFreeSpace(path) < needed)
            {
                Complete(
                    path,
                    TriageOutcome.InsufficientSpace,
                    "Insufficient free space.",
                    source: probe.Stats,
                    sourceLastWrite: sourceLastWrite);
                continue;
            }

            var encodePath = TempFileNaming.EncodePath(path, Environment.ProcessId);
            try
            {
                Report(path, TriagePhase.Encoding);
                var encode = await encoder.EncodeAsync(
                    path,
                    encodePath,
                    new Progress<double>(value => Report(path, TriagePhase.Encoding, value)),
                    cancellationToken);
                if (!encode.Succeeded)
                {
                    Complete(
                        path,
                        TriageOutcome.EncodeFailed,
                        encode.Reason,
                        source: probe.Stats,
                        sourceLastWrite: sourceLastWrite);
                    continue;
                }

                Report(path, TriagePhase.Verifying);
                var verification = await verifier.VerifyAsync(probe.Stats, encodePath, options, cancellationToken);
                if (!verification.IsValid)
                {
                    fileSystem.DeleteFile(encodePath);
                    Complete(
                        path,
                        TriageOutcome.OutputInvalid,
                        verification.Reason,
                        source: probe.Stats,
                        sourceLastWrite: sourceLastWrite);
                    continue;
                }

                var outputBytes = fileSystem.GetFileLength(encodePath);
                if (outputBytes >= probe.Stats.FileSizeBytes)
                {
                    fileSystem.DeleteFile(encodePath);
                    Complete(
                        path,
                        TriageOutcome.GrewKeptOriginal,
                        "Output was not smaller.",
                        source: probe.Stats,
                        sourceLastWrite: sourceLastWrite);
                    continue;
                }

                Report(path, TriagePhase.Replacing);
                var replace = replacer.Replace(path, encodePath, options.DeleteMode);
                if (!replace.Succeeded)
                {
                    if (fileSystem.FileExists(encodePath))
                        fileSystem.DeleteFile(encodePath);
                    Complete(
                        path,
                        TriageOutcome.EncodeFailed,
                        replace.Reason,
                        source: probe.Stats,
                        sourceLastWrite: sourceLastWrite);
                    continue;
                }

                // SafeReplacer MOVED the encode temp into staging/final, so do NOT delete encodePath here.
                var savedPercent =
                    (probe.Stats.FileSizeBytes - outputBytes) / (double)probe.Stats.FileSizeBytes * 100;
                Complete(
                    path,
                    replace.Outcome == ReplaceOutcome.ReplacePartial
                        ? TriageOutcome.ReplacePartial
                        : TriageOutcome.Replaced,
                    replace.Reason,
                    replace.FinalPath,
                    source: probe.Stats,
                    sourceLastWrite: sourceLastWrite,
                    outputBytes: outputBytes,
                    savedPercent: savedPercent,
                    replace: replace);
            }
            catch (OperationCanceledException)
            {
                // Only the encode temp is ever deleted here, and only if it still exists (a successful
                // replace has already consumed it). The original is never touched on cancellation.
                if (fileSystem.FileExists(encodePath)) fileSystem.DeleteFile(encodePath);
                Complete(
                    path,
                    TriageOutcome.Cancelled,
                    "Cancelled.",
                    source: probe.Stats,
                    sourceLastWrite: sourceLastWrite);
                throw;
            }
        }

        return Summarize(results, options);
    }

    private static Task WaitWhilePausedAsync(PauseToken? pauseToken, CancellationToken cancellationToken) =>
        pauseToken?.WaitWhilePausedAsync(cancellationToken) ?? Task.CompletedTask;

    private static TriageOutcome MapSkip(ClassificationOutcome outcome) => outcome switch
    {
        ClassificationOutcome.SkipAlreadyAv1 => TriageOutcome.SkippedAlreadyAv1,
        ClassificationOutcome.SkipLowBpp => TriageOutcome.SkippedLowBpp,
        _ => TriageOutcome.InvalidMetadata
    };

    private static TriageSummary Summarize(IReadOnlyList<FileProgress> files, TriageOptions options)
    {
        var replaced = files
            .Where(f => f.Outcome is TriageOutcome.Replaced or TriageOutcome.ReplacePartial)
            .ToList();

        long bytesSaved = 0;
        foreach (var f in replaced)
            bytesSaved += Math.Max(0, (f.Source?.FileSizeBytes ?? 0) - (f.OutputBytes ?? 0));

        int Count(params TriageOutcome[] outcomes) =>
            files.Count(f => f.Outcome is { } o && outcomes.Contains(o));

        return new TriageSummary
        {
            Scanned = files.Count,
            Candidates = files.Count(f => f.Outcome is not (
                TriageOutcome.SkippedAlreadyAv1 or
                TriageOutcome.SkippedLowBpp or
                TriageOutcome.AlreadyCompleted or
                TriageOutcome.InvalidMetadata)),
            Replaced = replaced.Count,
            Marginal = replaced.Count(f => (f.SavedPercent ?? 100) < options.MarginalThresholdPercent),
            Grew = Count(TriageOutcome.GrewKeptOriginal),
            Invalid = Count(TriageOutcome.OutputInvalid, TriageOutcome.InvalidMetadata),
            Failed = Count(TriageOutcome.EncodeFailed, TriageOutcome.InsufficientSpace, TriageOutcome.Cancelled),
            Skipped = Count(
                TriageOutcome.SkippedAlreadyAv1,
                TriageOutcome.SkippedLowBpp,
                TriageOutcome.AlreadyCompleted,
                TriageOutcome.DryRunCandidate),
            BytesSaved = bytesSaved,
            Files = files
        };
    }
}
