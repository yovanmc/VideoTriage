namespace VideoTriage.Core.Models;

public sealed record VerificationResult
{
    public required VerificationOutcome Outcome { get; init; }
    public required string Reason { get; init; }
    public VideoStats? OutputStats { get; init; }

    public bool IsValid => Outcome == VerificationOutcome.Valid;
}
