namespace VideoTriage.Core.Pipeline;

/// <summary>
/// Cooperative pause gate observed between pipeline phases. Pausing makes
/// <see cref="WaitWhilePausedAsync"/> block until <see cref="Resume"/> (or cancellation).
/// </summary>
public sealed class PauseToken
{
    private TaskCompletionSource _resume =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;
        _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        IsPaused = false;
        _resume.TrySetResult();
    }

    public Task WaitWhilePausedAsync(CancellationToken cancellationToken) =>
        IsPaused ? _resume.Task.WaitAsync(cancellationToken) : Task.CompletedTask;
}
