namespace VideoTriage.Core.Models;

public sealed record ProbeResult
{
    public required string FilePath { get; init; }
    public VideoStats? Stats { get; init; }
    public ProbeFailure? Failure { get; init; }
    public ClassificationResult? Classification { get; init; }

    public bool Succeeded => Stats is not null && Failure is null;
}
