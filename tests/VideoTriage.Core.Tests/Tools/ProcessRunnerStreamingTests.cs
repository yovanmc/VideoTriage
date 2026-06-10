using System.Diagnostics;
using Shouldly;
using VideoTriage.Core.Tools;

namespace VideoTriage.Core.Tests.Tools;

public sealed class ProcessRunnerStreamingTests
{
    [Fact]
    public async Task RunAsync_ReportsEveryStdoutLineAndReturnsFullText()
    {
        var lines = new List<string>();

        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "echo first&echo second"],
            StandardOutputLines = new InlineProgress<string>(lines.Add)
        });

        lines.ShouldBe(["first", "second"]);
        result.StandardOutput.ShouldContain("first");
        result.StandardOutput.ShouldContain("second");
    }

    [Fact]
    public async Task RunAsync_ReportsEveryStderrLine()
    {
        var lines = new List<string>();

        await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "echo stderr-line 1>&2"],
            StandardErrorLines = new InlineProgress<string>(lines.Add)
        });

        lines.ShouldContain(l => l.Contains("stderr-line"), "expected a line containing 'stderr-line'");
    }

    [Fact(Timeout = 15_000)]
    public async Task RunAsync_StdoutExceedsLimit_ReturnsTruncatedTailWithoutGrowingUnbounded()
    {
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "for /L %i in (1,1,40000) do @echo 01234567890123456789"],
            StandardOutputLimitCharacters = 4096
        });

        result.StandardOutput.Length.ShouldBeLessThanOrEqualTo(4096);
        result.StandardOutputTruncated.ShouldBeTrue();
    }

    [Fact(Timeout = 15_000)]
    public async Task RunAsync_ProgressCallbackThrows_ProcessStillDrainsAndReturns()
    {
        var callbackErrors = new List<Exception>();
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "echo first&echo second"],
            StandardOutputLines = new InlineProgress<string>(_ => throw new InvalidOperationException("callback")),
            ProgressCallbackError = callbackErrors.Add
        });

        result.ExitCode.ShouldBe(0);
        callbackErrors.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact(Timeout = 15_000)]
    public async Task RunAsync_Cancelled_KillsChildTreeAndReturnsWithinFiveSeconds()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var elapsed = Stopwatch.StartNew();
        await Should.ThrowAsync<OperationCanceledException>(() => new ProcessRunner().RunAsync(
            new ProcessRequest
            {
                FileName = "cmd.exe",
                Arguments = ["/c", "ping -t 127.0.0.1"],
                Timeout = Timeout.InfiniteTimeSpan
            },
            cts.Token));

        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
