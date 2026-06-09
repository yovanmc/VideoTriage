using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

public sealed class ReplacementRecovery(
    IReplacementJournal journal,
    IFileSystem fileSystem,
    IDeleteManifest deleteManifest) : IReplacementRecovery
{
    public ReplacementRecoveryReport Recover()
    {
        var entries = journal.Load();
        if (entries.Count == 0)
            return new ReplacementRecoveryReport
            {
                Recovered = [], Cleaned = [], Unrecoverable = []
            };

        // Group by TransactionId, take the last phase per transaction
        var byTx = entries
            .GroupBy(e => e.TransactionId)
            .Select(g => (Last: g.OrderBy(e => e.Timestamp).Last(), All: g.ToList()))
            .ToList();

        var recovered = new List<string>();
        var cleaned = new List<string>();
        var unrecoverable = new List<UnrecoverableEntry>();

        foreach (var (last, all) in byTx)
        {
            var phase = last.Phase;

            // Terminal states — nothing to do
            if (phase is ReplacementTransactionPhase.Committed
                      or ReplacementTransactionPhase.Partial
                      or ReplacementTransactionPhase.Recovered)
                continue;

            var originalExists = fileSystem.FileExists(last.OriginalPath);
            var stagingExists = fileSystem.FileExists(last.StagingPath);

            if (phase == ReplacementTransactionPhase.Prepared && originalExists && stagingExists)
            {
                // Case 1: both alive, delete staging (original is safe)
                fileSystem.DeleteFile(last.StagingPath);
                journal.Append(last with { Phase = ReplacementTransactionPhase.Recovered,
                                           Timestamp = DateTimeOffset.UtcNow });
                cleaned.Add(last.OriginalPath);
            }
            else if ((phase == ReplacementTransactionPhase.Prepared && !originalExists && stagingExists)
                  || (phase == ReplacementTransactionPhase.OriginalRemoved && stagingExists))
            {
                // Case 2 or 3: original gone, staging has the bytes — move to final
                var finalPath = last.IntendedFinalPath;
                if (fileSystem.FileExists(finalPath))
                    finalPath = last.StagingPath; // fall back to staging path as partial

                if (!string.Equals(finalPath, last.StagingPath, StringComparison.OrdinalIgnoreCase))
                    fileSystem.MoveFile(last.StagingPath, finalPath);

                // If this was Prepared (original unexpectedly gone), journal OriginalRemoved first
                if (phase == ReplacementTransactionPhase.Prepared)
                    journal.Append(last with { Phase = ReplacementTransactionPhase.OriginalRemoved,
                                               Timestamp = DateTimeOffset.UtcNow });

                journal.Append(last with { Phase = ReplacementTransactionPhase.Recovered,
                                           ActualFinalPath = finalPath,
                                           Timestamp = DateTimeOffset.UtcNow });

                // Repair manifest
                deleteManifest.Append(new DeleteManifestEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    DeleteMode = last.DeleteMode,
                    OriginalPath = last.OriginalPath,
                    OriginalBytes = last.OriginalBytes,
                    ReplacementPath = finalPath,
                    ReplacementBytes = last.ReplacementBytes,
                    SavedPercent = (last.OriginalBytes - last.ReplacementBytes) / (double)last.OriginalBytes * 100
                });

                recovered.Add(last.OriginalPath);
            }
            else
            {
                // Case 5: both missing — unrecoverable
                unrecoverable.Add(new UnrecoverableEntry
                {
                    OriginalPath = last.OriginalPath,
                    Detail = $"Transaction {last.TransactionId} is in phase {phase} but " +
                             $"neither original ({last.OriginalPath}) nor staging " +
                             $"({last.StagingPath}) exists on disk."
                });
            }
        }

        return new ReplacementRecoveryReport
        {
            Recovered = recovered,
            Cleaned = cleaned,
            Unrecoverable = unrecoverable
        };
    }
}
