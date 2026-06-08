namespace VideoTriage.App.Services;

public interface IAppLog
{
    string LogDirectory { get; }
    string CurrentLogPath { get; }
    void Information(string message);
    void Error(Exception exception, string message);
}
