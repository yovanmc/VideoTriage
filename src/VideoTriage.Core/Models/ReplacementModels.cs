namespace VideoTriage.Core.Models;

/// <summary>
/// How an original file is removed once a smaller, verified replacement is safely in place.
/// RecycleBin is the safe default (recoverable). Permanent is an explicit opt-in.
/// </summary>
public enum DeleteMode
{
    RecycleBin,
    Permanent
}

public enum ReplaceOutcome
{
    Replaced,
    ReplacePartial,
    Failed
}

public sealed record ReplaceResult
{
    public required ReplaceOutcome Outcome { get; init; }
    public required string FinalPath { get; init; }
    public required string Reason { get; init; }
    public bool OriginalRemoved { get; init; }

    public bool Succeeded => Outcome is ReplaceOutcome.Replaced or ReplaceOutcome.ReplacePartial;
}
