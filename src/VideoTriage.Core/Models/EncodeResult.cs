namespace VideoTriage.Core.Models;

public enum EncodeOutcome
{
    Succeeded,
    Failed,
    Cancelled
}

public sealed record EncodeResult
{
    public required EncodeOutcome Outcome { get; init; }
    public required string OutputPath { get; init; }
    public required string Reason { get; init; }
    public int? ExitCode { get; init; }
    public string? StderrPath { get; init; }
    public TimeSpan Elapsed { get; init; }

    public bool Succeeded => Outcome == EncodeOutcome.Succeeded;
}
