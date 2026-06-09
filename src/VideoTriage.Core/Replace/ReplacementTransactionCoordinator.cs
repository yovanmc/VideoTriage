using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.State;

namespace VideoTriage.Core.Replace;

/// <summary>
/// Executes a crash-safe, durably journaled replacement transaction. Journals each phase
/// (Prepared → OriginalRemoved → Committed / Partial) so a recovery tool can reconstruct the
/// exact state after a crash at any point in the sequence.
/// </summary>
public sealed class ReplacementTransactionCoordinator(
    IReplacementJournal journal,
    IFileSystem fileSystem,
    IFileRemover fileRemover,
    IDeleteManifest deleteManifest,
    Func<Guid>? transactionIdFactory = null) : IReplacementTransactionCoordinator
{
    private readonly Func<Guid> _transactionIdFactory = transactionIdFactory ?? Guid.NewGuid;

    public ReplaceResult Replace(ReplacementTransactionRequest request)
    {
        // Pre-flight: both files must exist before we journal anything.
        if (!fileSystem.FileExists(request.OriginalPath) ||
            !fileSystem.FileExists(request.VerifiedReplacementPath))
        {
            return Failed(request.OriginalPath, "Original or verified replacement is missing.");
        }

        var txId = _transactionIdFactory();
        var stagingPath = BuildStagingPath(request.OriginalPath);
        var finalPath = Path.ChangeExtension(request.OriginalPath, ".mp4");

        var baseEntry = new ReplacementTransactionEntry
        {
            RunId = request.RunId,
            TransactionId = txId,
            Timestamp = DateTimeOffset.UtcNow,
            Phase = ReplacementTransactionPhase.Prepared, // overridden per append
            DeleteMode = request.DeleteMode,
            OriginalPath = request.OriginalPath,
            OriginalBytes = request.OriginalBytes,
            StagingPath = stagingPath,
            IntendedFinalPath = finalPath,
            ReplacementBytes = request.ReplacementBytes
        };

        // Phase 1: Journal Prepared, then move replacement to staging.
        journal.Append(baseEntry with { Phase = ReplacementTransactionPhase.Prepared });
        fileSystem.MoveFile(request.VerifiedReplacementPath, stagingPath);

        // Phase 2: Remove original. If this fails, staging is still safe to clean up.
        try
        {
            fileRemover.Remove(request.OriginalPath, request.DeleteMode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Original is still alive. Clean up staging so no orphan remains.
            try { fileSystem.DeleteFile(stagingPath); } catch { }
            return new ReplaceResult
            {
                Outcome = ReplaceOutcome.Failed,
                FinalPath = request.OriginalPath,
                Reason = $"Remove failed; staging cleaned up: {ex.Message}",
                OriginalRemoved = false
            };
        }

        // Phase 3: Journal OriginalRemoved — the original is gone, bytes are in staging.
        journal.Append(baseEntry with
        {
            Phase = ReplacementTransactionPhase.OriginalRemoved,
            Timestamp = DateTimeOffset.UtcNow
        });

        // Phase 4: Final rename: staging → intended final path.
        try
        {
            fileSystem.MoveFile(stagingPath, finalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Original is gone but bytes are safe at staging. Journal Partial and preserve staging.
            journal.Append(baseEntry with
            {
                Phase = ReplacementTransactionPhase.Partial,
                Timestamp = DateTimeOffset.UtcNow,
                ActualFinalPath = stagingPath,
                Detail = ex.Message
            });

            // Append manifest with staging as the actual resting place.
            try
            {
                deleteManifest.Append(BuildManifestEntry(request, stagingPath));
            }
            catch { /* manifest failure is non-fatal when staging is preserved */ }

            return new ReplaceResult
            {
                Outcome = ReplaceOutcome.ReplacePartial,
                FinalPath = stagingPath,
                Reason = $"Original removed; verified replacement preserved as staging: {ex.Message}",
                OriginalRemoved = true
            };
        }

        // Phase 5: Journal Committed — the final rename succeeded.
        journal.Append(baseEntry with
        {
            Phase = ReplacementTransactionPhase.Committed,
            Timestamp = DateTimeOffset.UtcNow,
            ActualFinalPath = finalPath
        });

        // Phase 6: Append manifest. Failure here means ReplacePartial but journal proves bytes safe.
        try
        {
            deleteManifest.Append(BuildManifestEntry(request, finalPath));
        }
        catch
        {
            return new ReplaceResult
            {
                Outcome = ReplaceOutcome.ReplacePartial,
                FinalPath = finalPath,
                Reason = "Replacement committed; manifest append failed.",
                OriginalRemoved = true
            };
        }

        return new ReplaceResult
        {
            Outcome = ReplaceOutcome.Replaced,
            FinalPath = finalPath,
            Reason = "Replacement committed.",
            OriginalRemoved = true
        };
    }

    /// <summary>
    /// Builds the staging path for a given original. Uses the StagingInfix without a process-ID
    /// suffix so the path is deterministic per original file within this transaction.
    /// </summary>
    private static string BuildStagingPath(string originalPath)
    {
        var dir = Path.GetDirectoryName(originalPath)!;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
        return Path.Combine(dir, $"{nameWithoutExt}{TempFileNaming.StagingInfix}mp4");
    }

    private static DeleteManifestEntry BuildManifestEntry(
        ReplacementTransactionRequest request,
        string actualFinalPath)
    {
        var savedPercent = request.OriginalBytes > 0
            ? (request.OriginalBytes - request.ReplacementBytes) / (double)request.OriginalBytes * 100
            : 0;

        return new DeleteManifestEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            DeleteMode = request.DeleteMode,
            OriginalPath = request.OriginalPath,
            OriginalBytes = request.OriginalBytes,
            ReplacementPath = actualFinalPath,
            ReplacementBytes = request.ReplacementBytes,
            SavedPercent = savedPercent
        };
    }

    private static ReplaceResult Failed(string path, string reason) => new()
    {
        Outcome = ReplaceOutcome.Failed,
        FinalPath = path,
        Reason = reason,
        OriginalRemoved = false
    };
}
