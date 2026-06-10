using VideoTriage.Core.Encoding;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Poster;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Replace;
using VideoTriage.Core.State;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Pipeline;

/// <summary>
/// Orchestrates discovery → probe → classify → space → encode → verify → size-check → replace,
/// emitting immutable <see cref="FileProgress"/> events. Every destructive step is gated by
/// verification and a strict size comparison, and every discovered file receives a terminal event.
/// The original is never touched on any failure path.
/// </summary>
public sealed class TriagePipeline(
    IRunLeaseFactory runLeaseFactory,
    IVideoFileDiscovery discovery,
    IFfprobeService ffprobe,
    IVideoClassifier classifier,
    IVideoEncoder encoder,
    IOutputVerifier verifier,
    ISafeReplacer replacer,
    IFileSystem fileSystem,
    Func<string, ICompletedFileStore> completedStoreFactory,
    Func<string, IDeleteManifest> deleteManifestFactory,
    Func<string, IResultLog> resultLogFactory,
    IPosterEmbedder? posterEmbedder = null,
    Func<string, IReplacementTransactionCoordinator>? coordinatorFactory = null,
    Func<string, IReplacementRecovery>? recoveryFactory = null,
    Func<string, IActiveRunJournal>? activeRunJournalFactory = null) : ITriagePipeline
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
        using var runLease = options.DryRun ? null : runLeaseFactory.Acquire(dataDirectory);
        ICompletedFileStore? completedStore = null;
        IDeleteManifest? deleteManifest = null;
        IResultLog? resultLog = null;
        IReplacementTransactionCoordinator? coordinator = null;
        IActiveRunJournal? activeJournal = null;
        Guid runId = options.DryRun ? Guid.Empty : Guid.NewGuid();
        var completedByPath = new Dictionary<string, CompletedFileEntry>(StringComparer.OrdinalIgnoreCase);

        if (!options.DryRun)
        {
            fileSystem.CreateDirectory(dataDirectory);
            completedStore = completedStoreFactory(dataDirectory);
            deleteManifest = deleteManifestFactory(dataDirectory);
            resultLog = resultLogFactory(dataDirectory);

            if (recoveryFactory is not null)
            {
                var recovery = recoveryFactory(dataDirectory).Recover();
                if (recovery.Unrecoverable.Count > 0)
                    throw new ReplacementRecoveryRequiredException(recovery.Unrecoverable);
            }

            if (coordinatorFactory is not null)
                coordinator = coordinatorFactory(dataDirectory);

            activeJournal = activeRunJournalFactory?.Invoke(dataDirectory);

            foreach (var entry in completedStore.Load())
            {
                if (TryNormalizePath(entry.SourcePath, out var normalizedPath))
                    completedByPath[normalizedPath] = entry;
            }
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

            // Only append to pipeline-level manifest when NOT using the transaction coordinator
            // (which appends to its own manifest internally).
            if (coordinator is null &&
                replace is { OriginalRemoved: true } &&
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

        var allFiles = discovery.EnumerateVideos(folder, options, recursive).ToList();
        var startedAtUtc = DateTimeOffset.UtcNow;

        activeJournal?.Save(new ActiveRunState
        {
            RunId = runId,
            Folder = folder,
            StartedAtUtc = startedAtUtc,
            CompletedFiles = 0,
            TotalFiles = allFiles.Count
        });

        foreach (var path in allFiles)
        {
            activeJournal?.Save(new ActiveRunState
            {
                RunId = runId,
                Folder = folder,
                StartedAtUtc = startedAtUtc,
                CurrentFile = path,
                CurrentPhase = TriagePhase.Probing,
                CompletedFiles = results.Count,
                TotalFiles = allFiles.Count
            });

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

            var transactionId = Guid.NewGuid();
            var encodePath = TempFileNaming.EncodePath(path, transactionId);
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

                var replacementPath = encodePath;
                if (options.EmbedPoster && posterEmbedder is not null)
                {
                    Report(path, TriagePhase.EmbeddingPoster);
                    var poster = await posterEmbedder.EmbedAsync(
                        encodePath,
                        probe.Stats,
                        options,
                        cancellationToken);
                    replacementPath = poster.OutputPath;
                }

                var outputBytes = fileSystem.GetFileLength(replacementPath);
                if (outputBytes >= probe.Stats.FileSizeBytes)
                {
                    if (fileSystem.FileExists(replacementPath))
                        fileSystem.DeleteFile(replacementPath);
                    if (!string.Equals(replacementPath, encodePath, StringComparison.OrdinalIgnoreCase) &&
                        fileSystem.FileExists(encodePath))
                    {
                        fileSystem.DeleteFile(encodePath);
                    }
                    Complete(
                        path,
                        TriageOutcome.GrewKeptOriginal,
                        "Output was not smaller.",
                        source: probe.Stats,
                        sourceLastWrite: sourceLastWrite);
                    continue;
                }

                if (!string.Equals(replacementPath, encodePath, StringComparison.OrdinalIgnoreCase) &&
                    fileSystem.FileExists(encodePath))
                {
                    fileSystem.DeleteFile(encodePath);
                }

                Report(path, TriagePhase.Replacing);
                ReplaceResult replace;
                if (coordinator is not null)
                {
                    replace = coordinator.Replace(new ReplacementTransactionRequest
                    {
                        RunId = runId,
                        OriginalPath = path,
                        VerifiedReplacementPath = replacementPath,
                        OriginalBytes = probe.Stats.FileSizeBytes,
                        ReplacementBytes = outputBytes,
                        DeleteMode = options.DeleteMode
                    });
                }
                else
                {
                    replace = replacer.Replace(path, replacementPath, options.DeleteMode);
                }

                if (!replace.Succeeded)
                {
                    if (coordinator is null && fileSystem.FileExists(replacementPath))
                        fileSystem.DeleteFile(replacementPath);
                    if (coordinator is null &&
                        !string.Equals(replacementPath, encodePath, StringComparison.OrdinalIgnoreCase) &&
                        fileSystem.FileExists(encodePath))
                    {
                        fileSystem.DeleteFile(encodePath);
                    }
                    Complete(
                        path,
                        TriageOutcome.ReplaceFailed,
                        replace.Reason,
                        source: probe.Stats,
                        sourceLastWrite: sourceLastWrite);
                    continue;
                }

                // Coordinator consumed replacementPath into staging/final.
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unexpected I/O error (e.g. disk full mid-encode, file locked). Clean up the temp
                // file best-effort and record a per-file failure so the run continues.
                try { if (fileSystem.FileExists(encodePath)) fileSystem.DeleteFile(encodePath); } catch { }
                Complete(
                    path,
                    TriageOutcome.EncodeFailed,
                    $"Unexpected I/O failure: {ex.Message}",
                    source: probe.Stats,
                    sourceLastWrite: sourceLastWrite);
            }
            finally
            {
                // Defensive: clean up encodePath on any unexpected exception. Normal paths (success,
                // cancellation, handled failures) have already consumed or deleted the file before
                // reaching here, so FileExists returns false and this is a no-op for them.
                if (fileSystem.FileExists(encodePath))
                    try { fileSystem.DeleteFile(encodePath); } catch { }
            }
        }

        activeJournal?.Clear();
        return Summarize(results, options);
    }

    private static Task WaitWhilePausedAsync(PauseToken? pauseToken, CancellationToken cancellationToken) =>
        pauseToken?.WaitWhilePausedAsync(cancellationToken) ?? Task.CompletedTask;

    private static bool TryNormalizePath(string path, out string normalizedPath)
    {
        try
        {
            normalizedPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

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
            Failed = Count(TriageOutcome.EncodeFailed, TriageOutcome.ReplaceFailed, TriageOutcome.InsufficientSpace, TriageOutcome.Cancelled),
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
