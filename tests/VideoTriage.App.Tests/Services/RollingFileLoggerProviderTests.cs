using Microsoft.Extensions.Logging;
using Shouldly;
using VideoTriage.App.Services;

namespace VideoTriage.App.Tests.Services;

public sealed class RollingFileLoggerProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "VideoTriage.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Log_WritesTimestampLevelCategoryAndMessageToDailyFile()
    {
        var now = new DateTimeOffset(2026, 6, 7, 12, 30, 0, TimeSpan.Zero);
        var paths = new RollingFileLogPath(_directory, () => now);
        using var provider = new RollingFileLoggerProvider(paths, () => now);
        var logger = provider.CreateLogger("VideoTriage.Tests");

        logger.LogError(new InvalidOperationException("boom"), "Run {RunId} failed", 42);

        var text = File.ReadAllText(Path.Combine(_directory, "videotriage-20260607.log"));
        text.ShouldContain("2026-06-07T12:30:00.0000000+00:00");
        text.ShouldContain("Error");
        text.ShouldContain("VideoTriage.Tests");
        text.ShouldContain("Run 42 failed");
        text.ShouldContain("InvalidOperationException: boom");
    }

    [Fact]
    public void Log_DateChanges_WritesToNewDailyFile()
    {
        var now = new DateTimeOffset(2026, 6, 7, 23, 59, 0, TimeSpan.Zero);
        var paths = new RollingFileLogPath(_directory, () => now);
        using var provider = new RollingFileLoggerProvider(paths, () => now);
        var logger = provider.CreateLogger("VideoTriage.Tests");

        logger.LogInformation("before");
        now = now.AddMinutes(2);
        logger.LogInformation("after");

        File.Exists(Path.Combine(_directory, "videotriage-20260607.log")).ShouldBeTrue();
        File.Exists(Path.Combine(_directory, "videotriage-20260608.log")).ShouldBeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
