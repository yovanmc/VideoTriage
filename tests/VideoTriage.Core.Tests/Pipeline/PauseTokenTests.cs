using Shouldly;
using VideoTriage.Core.Pipeline;

namespace VideoTriage.Core.Tests.Pipeline;

public sealed class PauseTokenTests
{
    [Fact]
    public async Task WaitWhilePausedAsync_CompletesOnlyAfterResume()
    {
        var token = new PauseToken();
        token.Pause();
        var wait = token.WaitWhilePausedAsync(CancellationToken.None);
        wait.IsCompleted.ShouldBeFalse();
        token.Resume();
        await wait.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitWhilePausedAsync_CancellationThrows()
    {
        var token = new PauseToken();
        token.Pause();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(
            () => token.WaitWhilePausedAsync(cts.Token));
    }

    [Fact]
    public void WaitWhilePausedAsync_NotPaused_CompletesImmediately() =>
        new PauseToken().WaitWhilePausedAsync(CancellationToken.None).IsCompleted.ShouldBeTrue();
}
