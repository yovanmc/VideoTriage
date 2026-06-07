namespace VideoTriage.Core.Models;

public sealed record ProbeFailure
{
    public required string FilePath { get; init; }
    public required string Message { get; init; }
    public int? ExitCode { get; init; }
    public string? StderrPath { get; init; }
}
