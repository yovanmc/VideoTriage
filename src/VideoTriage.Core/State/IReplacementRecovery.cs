using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

/// <summary>Reconciles unresolved journal entries before a new run begins.</summary>
public interface IReplacementRecovery
{
    ReplacementRecoveryReport Recover();
}

public sealed record ReplacementRecoveryReport
{
    public required IReadOnlyList<string> Recovered { get; init; }
    public required IReadOnlyList<string> Cleaned { get; init; }
    public required IReadOnlyList<UnrecoverableEntry> Unrecoverable { get; init; }
}

public sealed record UnrecoverableEntry
{
    public required string OriginalPath { get; init; }
    public required string Detail { get; init; }
}
