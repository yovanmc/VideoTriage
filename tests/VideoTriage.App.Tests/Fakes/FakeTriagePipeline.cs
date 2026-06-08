using VideoTriage.Core.Models;
using VideoTriage.Core.Pipeline;

namespace VideoTriage.App.Tests.Fakes;

public sealed class FakeTriagePipeline(IReadOnlyList<FileProgress> events) : ITriagePipeline
{
    public Task<TriageSummary> RunAsync(
        string folder,
        TriageOptions options,
        bool recursive = false,
        IProgress<FileProgress>? progress = null,
        PauseToken? pauseToken = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var e in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(e);
        }

        return Task.FromResult(EmptySummary());
    }

    public static TriageSummary EmptySummary() => new()
    {
        Scanned = 0,
        Candidates = 0,
        Replaced = 0,
        Marginal = 0,
        Grew = 0,
        Invalid = 0,
        Failed = 0,
        Skipped = 0,
        BytesSaved = 0,
        Files = []
    };
}

public sealed class BlockingTriagePipeline : ITriagePipeline
{
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public PauseToken? PauseToken { get; private set; }

    public async Task<TriageSummary> RunAsync(
        string folder,
        TriageOptions options,
        bool recursive = false,
        IProgress<FileProgress>? progress = null,
        PauseToken? pauseToken = null,
        CancellationToken cancellationToken = default)
    {
        PauseToken = pauseToken;
        Started.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }

        return FakeTriagePipeline.EmptySummary();
    }
}
