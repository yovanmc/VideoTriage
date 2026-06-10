namespace VideoTriage.App.Services;

public interface IApplicationWorkLifetime
{
    /// <summary>
    /// Register the currently-active work task and its CTS.
    /// If a previous task is still running, throws <see cref="InvalidOperationException"/>.
    /// Silently replaces completed/null previous task.
    /// </summary>
    void Track(Task task, CancellationTokenSource cancellation);

    /// <summary>
    /// Cancel tracked work and await its completion within <paramref name="timeout"/>.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    Task StopAsync(TimeSpan timeout);
}
