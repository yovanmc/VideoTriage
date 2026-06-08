using System.IO;

namespace VideoTriage.App.Services;

public sealed class RollingFileLogPath(
    string logDirectory,
    Func<DateTimeOffset>? utcNow = null)
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public string LogDirectory { get; } = logDirectory;

    public string CurrentLogPath =>
        Path.Combine(LogDirectory, $"videotriage-{_utcNow():yyyyMMdd}.log");
}
