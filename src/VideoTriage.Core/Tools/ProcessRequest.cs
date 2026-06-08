namespace VideoTriage.Core.Tools;

public sealed record ProcessRequest
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    public string? StderrDirectory { get; init; }
    public IProgress<string>? StandardOutputLines { get; init; }
}
