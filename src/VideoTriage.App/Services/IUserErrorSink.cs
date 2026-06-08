namespace VideoTriage.App.Services;

public interface IUserErrorSink
{
    IReadOnlyList<UserError> Errors { get; }
    void Add(UserErrorSeverity severity, string title, string message, string? detail = null);
    void Clear();
}
