using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VideoTriage.App.Services;

public sealed class RollingFileLoggerProvider(
    RollingFileLogPath paths,
    Func<DateTimeOffset>? utcNow = null) : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public ILogger CreateLogger(string categoryName) =>
        new RollingFileLogger(categoryName, paths, _gate, _utcNow);

    public void Dispose()
    {
    }

    private sealed class RollingFileLogger(
        string category,
        RollingFileLogPath paths,
        object gate,
        Func<DateTimeOffset> utcNow) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var line = $"{utcNow():O} [{logLevel}] {category}: {message}";
            if (exception is not null)
                line += Environment.NewLine + exception;

            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(paths.LogDirectory);
                    File.AppendAllText(
                        paths.CurrentLogPath,
                        line + Environment.NewLine,
                        Encoding.UTF8);
                }
                catch (Exception writeFailure) when (
                    writeFailure is IOException or UnauthorizedAccessException)
                {
                    // Diagnostics must never replace the application failure being reported.
                }
            }
        }
    }
}
