using Microsoft.Extensions.Logging;

namespace VideoTriage.App.Services;

public sealed class AppLog(
    ILogger<AppLog> logger,
    RollingFileLogPath paths) : IAppLog
{
    public string LogDirectory => paths.LogDirectory;
    public string CurrentLogPath => paths.CurrentLogPath;

    public void Information(string message) => logger.LogInformation("{Message}", message);

    public void Error(Exception exception, string message) =>
        logger.LogError(exception, "{Message}", message);
}
