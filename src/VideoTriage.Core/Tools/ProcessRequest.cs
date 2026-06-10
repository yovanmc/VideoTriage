namespace VideoTriage.Core.Tools;

public sealed record ProcessRequest
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    public string? StderrDirectory { get; init; }

    /// <summary>
    /// Exact path for stderr capture. When set, takes precedence over <see cref="StderrDirectory"/>.
    /// The caller owns the file's lifecycle; it is NOT deleted by <see cref="ProcessRunner"/>.
    /// </summary>
    public string? StandardErrorPath { get; init; }

    public IProgress<string>? StandardOutputLines { get; init; }
    public IProgress<string>? StandardErrorLines { get; init; }
    public int StandardOutputLimitCharacters { get; init; } = 256 * 1024;
    public Action<Exception>? ProgressCallbackError { get; init; }
}
