using Shouldly;
using VideoTriage.Core.Tools;
using Xunit;

namespace VideoTriage.Core.Tests.Tools;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesStdout()
    {
        using var temp = new TempDirectory();
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "echo stdout-text"],
            StderrDirectory = temp.Path
        });

        result.Succeeded.ShouldBeTrue();
        result.StandardOutput.ShouldContain("stdout-text");
        File.Exists(result.StandardErrorPath).ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_WritesStderrToFile()
    {
        using var temp = new TempDirectory();
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "echo stderr-text 1>&2"],
            StderrDirectory = temp.Path
        });

        result.Succeeded.ShouldBeTrue();
        File.ReadAllText(result.StandardErrorPath!).ShouldContain("stderr-text");
    }

    [Fact]
    public async Task RunAsync_ReturnsNonZeroExitCode()
    {
        using var temp = new TempDirectory();
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "exit /b 7"],
            StderrDirectory = temp.Path
        });

        result.ExitCode.ShouldBe(7);
        result.Succeeded.ShouldBeFalse();
        result.TimedOut.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_TimesOutAndKillsProcess()
    {
        using var temp = new TempDirectory();
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "ping -n 6 127.0.0.1 > nul"],
            Timeout = TimeSpan.FromMilliseconds(200),
            StderrDirectory = temp.Path
        });

        result.TimedOut.ShouldBeTrue();
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_HonorsCancellation()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            new ProcessRunner().RunAsync(new ProcessRequest
            {
                FileName = "cmd.exe",
                Arguments = ["/c", "ping -n 6 127.0.0.1 > nul"],
                Timeout = TimeSpan.FromSeconds(10),
                StderrDirectory = temp.Path
            }, cts.Token));
    }

    [Fact]
    public async Task RunAsync_NoStderrDirectory_CreatesNoFile()
    {
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "echo stderr 1>&2"]
        });

        result.StandardErrorPath.ShouldBeNull();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VideoTriage.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
