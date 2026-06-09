using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Replace;

/// <summary>
/// Replaces an original with a smaller, already-verified candidate using a crash-safe ordering:
/// the verified bytes are placed and size-checked at a staging path BEFORE the original is removed,
/// so an interruption can never destroy the only copy. See architecture contract §7.
/// </summary>
public sealed class SafeReplacer(
    IFileSystem fileSystem,
    IFileRemover fileRemover,
    Func<Guid>? transactionIdFactory = null) : ISafeReplacer
{
    private readonly Func<Guid> _transactionIdFactory = transactionIdFactory ?? Guid.NewGuid;

    public ReplaceResult Replace(string originalPath, string verifiedReplacementPath, DeleteMode deleteMode)
    {
        // 1. Both files must exist.
        if (!fileSystem.FileExists(originalPath) || !fileSystem.FileExists(verifiedReplacementPath))
            return Failed(originalPath, "Original or verified replacement is missing.");

        // 2. Candidate must be non-empty and strictly smaller than the original.
        var originalLength = fileSystem.GetFileLength(originalPath);
        var replacementLength = fileSystem.GetFileLength(verifiedReplacementPath);
        if (replacementLength <= 0 || replacementLength >= originalLength)
            return Failed(originalPath, "Replacement is empty or not smaller.");

        // 3. Canonical .mp4 target must not clobber a different existing file.
        var finalPath = Path.ChangeExtension(originalPath, ".mp4");
        if (!string.Equals(finalPath, originalPath, StringComparison.OrdinalIgnoreCase) &&
            fileSystem.FileExists(finalPath))
            return Failed(originalPath, $"Final path already exists: {finalPath}");

        var txId = _transactionIdFactory();

        // 4. Move the candidate to a DISTINCT staging path. StagingPath uses a different infix than
        //    EncodePath, so even when the candidate is the encoder output for the same source/txId the
        //    move is never a same-path move. Moving (not copying) consumes the encode temp so it
        //    cannot leak after a successful replace, while still guaranteeing the verified bytes are
        //    on disk before the original is removed.
        var stagingPath = TempFileNaming.StagingPath(originalPath, txId);
        fileSystem.MoveFile(verifiedReplacementPath, stagingPath);

        // 5. Confirm staging landed intact before we touch the original.
        if (!fileSystem.FileExists(stagingPath) || fileSystem.GetFileLength(stagingPath) != replacementLength)
            return Failed(originalPath, "Staging verification failed.");

        // 6. Remove the original (RecycleBin by default; Permanent only on explicit opt-in).
        fileRemover.Remove(originalPath, deleteMode);

        try
        {
            // 7. Commit by renaming staging to the canonical final path.
            fileSystem.MoveFile(stagingPath, finalPath);
            return new ReplaceResult
            {
                Outcome = ReplaceOutcome.Replaced,
                FinalPath = finalPath,
                Reason = "Replacement committed.",
                OriginalRemoved = true
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 8. The original is already gone but the verified bytes are safe at staging. Preserve
            //    them under a partial name and report ReplacePartial — never lose data.
            var partialPath = TempFileNaming.PartialPath(originalPath, txId);
            fileSystem.MoveFile(stagingPath, partialPath);
            return new ReplaceResult
            {
                Outcome = ReplaceOutcome.ReplacePartial,
                FinalPath = partialPath,
                Reason = $"Original removed; verified replacement preserved as partial: {ex.Message}",
                OriginalRemoved = true
            };
        }
    }

    private static ReplaceResult Failed(string path, string reason) => new()
    {
        Outcome = ReplaceOutcome.Failed,
        FinalPath = path,
        Reason = reason,
        OriginalRemoved = false
    };
}
