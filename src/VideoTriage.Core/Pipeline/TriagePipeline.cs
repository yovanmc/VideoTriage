using VideoTriage.Core.Encoding;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Replace;
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
    IFileSystem fileSystem) : ITriagePipeline
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

        void Report(string path, TriagePhase phase, double? encodeProgress = null) =>
            progress?.Report(new FileProgress { FilePath = path, Phase = phase, EncodeProgress = encodeProgress });

        void Complete(
            string path,
            TriageOutcome outcome,
            string reason,
            string? finalPath = null,
            VideoStats? source = null,
            long? outputBytes = null,
            double? savedPercent = null)
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
        }

        foreach (var path in discovery.FindVideos(folder, options, recursive))
        {
            Report(path, TriagePhase.Discovered);
            await WaitWhilePausedAsync(pauseToken, cancellationToken);

            Report(path, TriagePhase.Probing);
            var probe = await ffprobe.ProbeAsync(path, cancellationToken);
            if (!probe.Succeeded || probe.Stats is null)
            {
                Complete(path, TriageOutcome.InvalidMetadata, probe.Failure?.Message ?? "Probe failed.");
                continue;
            }

            Report(path, TriagePhase.Classified);
            var classification = classifier.Classify(probe.Stats, options);
            if (!classification.IsCandidate)
            {
                Complete(path, MapSkip(classification.Outcome), classification.Reason, source: probe.Stats);
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
                Complete(path, TriageOutcome.InsufficientSpace, "Insufficient free space.", source: probe.Stats);
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
                    Complete(path, TriageOutcome.EncodeFailed, encode.Reason, source: probe.Stats);
                    continue;
                }

                Report(path, TriagePhase.Verifying);
                var verification = await verifier.VerifyAsync(probe.Stats, encodePath, options, cancellationToken);
                if (!verification.IsValid)
                {
                    fileSystem.DeleteFile(encodePath);
                    Complete(path, TriageOutcome.OutputInvalid, verification.Reason, source: probe.Stats);
                    continue;
                }

                var outputBytes = fileSystem.GetFileLength(encodePath);
                if (outputBytes >= probe.Stats.FileSizeBytes)
                {
                    fileSystem.DeleteFile(encodePath);
                    Complete(path, TriageOutcome.GrewKeptOriginal, "Output was not smaller.", source: probe.Stats);
                    continue;
                }

                Report(path, TriagePhase.Replacing);
                var replace = replacer.Replace(path, encodePath, options.DeleteMode);
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
                    outputBytes: outputBytes,
                    savedPercent: savedPercent);
            }
            catch (OperationCanceledException)
            {
                // Only the encode temp is ever deleted here, and only if it still exists (a successful
                // replace has already consumed it). The original is never touched on cancellation.
                if (fileSystem.FileExists(encodePath)) fileSystem.DeleteFile(encodePath);
                Complete(path, TriageOutcome.Cancelled, "Cancelled.", source: probe.Stats);
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
