using Shouldly;
using VideoTriage.App.Services;

namespace VideoTriage.App.Tests.Services;

public sealed class ApplicationWorkLifetimeTests
{
    [Fact]
    public async Task StopAsync_CancelsTrackedCtsAndAwaitsTask()
    {
        var lifetime = new ApplicationWorkLifetime();
        var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lifetime.Track(tcs.Task, cts);

        // Complete the task when cancelled
        cts.Token.Register(() => tcs.SetResult());

        await lifetime.StopAsync(TimeSpan.FromSeconds(5));

        cts.IsCancellationRequested.ShouldBeTrue();
        tcs.Task.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task StopAsync_CalledTwice_IsIdempotent()
    {
        var lifetime = new ApplicationWorkLifetime();
        var cts = new CancellationTokenSource();
        lifetime.Track(Task.CompletedTask, cts);

        await lifetime.StopAsync(TimeSpan.FromSeconds(5));
        await lifetime.StopAsync(TimeSpan.FromSeconds(5)); // must not throw
    }

    [Fact]
    public void Track_OverlappingActiveTasks_ThrowsInvalidOperationException()
    {
        var lifetime = new ApplicationWorkLifetime();
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var longTask = Task.Delay(Timeout.Infinite);

        lifetime.Track(longTask, cts1);

        Should.Throw<InvalidOperationException>(() => lifetime.Track(longTask, cts2));

        cts1.Cancel();
    }

    [Fact]
    public async Task Track_AfterCompletedTask_Succeeds()
    {
        var lifetime = new ApplicationWorkLifetime();
        var cts1 = new CancellationTokenSource();
        lifetime.Track(Task.CompletedTask, cts1);

        await Task.Delay(10); // ensure task is completed

        var cts2 = new CancellationTokenSource();
        Should.NotThrow(() => lifetime.Track(Task.CompletedTask, cts2));
    }
}
