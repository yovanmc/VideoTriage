using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

/// <summary>
/// Durably records replacement transaction phases for crash recovery.
/// </summary>
public interface IReplacementJournal
{
    /// <summary>Appends a transaction entry durably (WriteThrough + flush to disk).</summary>
    void Append(ReplacementTransactionEntry entry);

    /// <summary>Loads all valid entries; skips truncated or malformed lines.</summary>
    IReadOnlyList<ReplacementTransactionEntry> Load();
}
