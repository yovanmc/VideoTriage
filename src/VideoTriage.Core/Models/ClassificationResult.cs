namespace VideoTriage.Core.Models;

public enum ClassificationOutcome
{
    Candidate,
    SkipAlreadyAv1,
    SkipLowBpp,
    InvalidMetadata
}

public sealed record ClassificationResult
{
    public required ClassificationOutcome Outcome { get; init; }
    public required string Reason { get; init; }
    public required VideoStats Stats { get; init; }

    public bool IsCandidate => Outcome == ClassificationOutcome.Candidate;
}
