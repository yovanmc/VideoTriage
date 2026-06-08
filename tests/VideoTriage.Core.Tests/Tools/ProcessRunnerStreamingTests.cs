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

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
