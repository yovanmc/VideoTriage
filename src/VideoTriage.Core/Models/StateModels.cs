namespace VideoTriage.Core.Models;

/// <summary>One source we have already triaged; lets a later run skip unchanged files.</summary>
public sealed record CompletedFileEntry
{
    public required string SourcePath { get; init; }
    public required long SourceLength { get; init; }
    public required DateTimeOffset SourceLastWriteUtc { get; init; }
    public required TriageOutcome Outcome { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

/// <summary>Audit record written only after an original is actually removed.</summary>
public sealed record DeleteManifestEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required DeleteMode DeleteMode { get; init; }
    public required string OriginalPath { get; init; }
    public required long OriginalBytes { get; init; }
    public required string ReplacementPath { get; init; }
    public required long ReplacementBytes { get; init; }
    public required double SavedPercent { get; init; }
}

/// <summary>Per-file terminal result for every non-dry-run outcome.</summary>
public sealed record ResultLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string SourcePath { get; init; }
    public required TriageOutcome Outcome { get; init; }
    public required string Message { get; init; }
    public long? SourceBytes { get; init; }
    public long? OutputBytes { get; init; }
    public double? SavedPercent { get; init; }
    public string? FinalPath { get; init; }
}
