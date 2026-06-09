namespace VideoTriage.Core.Models;

public enum TriagePhase
{
    Discovered,
    Probing,
    Classified,
    WaitingForSpace,
    Encoding,
    Verifying,
    EmbeddingPoster,
    Replacing,
    Done
}

public enum TriageOutcome
{
    DryRunCandidate,
    SkippedAlreadyAv1,
    SkippedLowBpp,
    InvalidMetadata,
    AlreadyCompleted,
    InsufficientSpace,
    EncodeFailed,
    ReplaceFailed,
    OutputInvalid,
    GrewKeptOriginal,
    Replaced,
    ReplacePartial,
    Cancelled
}

public sealed record FileProgress
{
    public required string FilePath { get; init; }
    public required TriagePhase Phase { get; init; }
    public double? EncodeProgress { get; init; }
    public TriageOutcome? Outcome { get; init; }
    public VideoStats? Source { get; init; }
    public ClassificationResult? Classification { get; init; }
    public long? OutputBytes { get; init; }
    public double? SavedPercent { get; init; }
    public string? Message { get; init; }
    public string? FinalPath { get; init; }
}

public sealed record TriageSummary
{
    public required int Scanned { get; init; }
    public required int Candidates { get; init; }
    public required int Replaced { get; init; }
    public required int Marginal { get; init; }
    public required int Grew { get; init; }
    public required int Invalid { get; init; }
    public required int Failed { get; init; }
    public required int Skipped { get; init; }
    public required long BytesSaved { get; init; }
    public required IReadOnlyList<FileProgress> Files { get; init; }
}
