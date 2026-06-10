namespace VideoTriage.App.Services;

public sealed class ApplicationWorkLifetime : IApplicationWorkLifetime
{
    private readonly object _lock = new();
    private Task? _task;
    private CancellationTokenSource? _cts;
    private bool _stopped;

    public void Track(Task task, CancellationTokenSource cancellation)
    {
        lock (_lock)
        {
            if (_task is { IsCompleted: false })
                throw new InvalidOperationException("Cannot track overlapping active work.");
            _task = task;
            _cts = cancellation;
        }
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        Task? taskToWait;
        CancellationTokenSource? ctsToCancel;
        lock (_lock)
        {
            if (_stopped) return;
            _stopped = true;
            taskToWait = _task;
            ctsToCancel = _cts;
        }

        ctsToCancel?.Cancel();
        if (taskToWait is { IsCompleted: false })
        {
            try { await taskToWait.WaitAsync(timeout); }
            catch (TimeoutException) { /* log if needed; process is exiting */ }
            catch (OperationCanceledException) { }
        }
    }
}
