namespace VideoTriage.Core.Tools;

public sealed record ProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardErrorPath { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public bool TimedOut { get; init; }

    public bool Succeeded => ExitCode == 0 && !TimedOut;
}
