namespace VideoTriage.App.Services;

public sealed class UserErrorSink(Func<DateTimeOffset>? utcNow = null) : IUserErrorSink
{
    private const int Capacity = 200;
    private readonly object _gate = new();
    private readonly List<UserError> _errors = [];
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public IReadOnlyList<UserError> Errors
    {
        get
        {
            lock (_gate)
                return _errors.ToArray();
        }
    }

    public void Add(
        UserErrorSeverity severity,
        string title,
        string message,
        string? detail = null)
    {
        lock (_gate)
        {
            _errors.Add(new UserError(_utcNow(), severity, title, message, detail));
            if (_errors.Count > Capacity)
                _errors.RemoveRange(0, _errors.Count - Capacity);
        }
    }

    public void Clear()
    {
        lock (_gate)
            _errors.Clear();
    }
}
