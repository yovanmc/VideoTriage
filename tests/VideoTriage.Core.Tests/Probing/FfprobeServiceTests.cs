using Shouldly;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Tools;
using Xunit;

namespace VideoTriage.Core.Tests.Probing;

public sealed class FfprobeServiceTests
{
    [Fact]
    public async Task ProbeAsync_ReturnsFailureWhenFileDoesNotExist()
    {
        var service = new FfprobeService("ffprobe.exe", new FakeProcessRunner(), new FfprobeJsonParser());

        var result = await service.ProbeAsync(@"C:\missing\video.mp4");

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldNotBeNull();
        result.Failure.Message.ShouldContain("does not exist");
    }

    [Fact]
    public async Task ProbeAsync_BuildsFfprobeCommandAndParsesStats()
    {
        using var temp = new TempVideoFile();
        var runner = new FakeProcessRunner { Result = SuccessfulResult(Fixture("h264-with-audio.json")) };
        var service = new FfprobeService(@"C:\tools\ffprobe.exe", runner, new FfprobeJsonParser());

        var result = await service.ProbeAsync(temp.Path);

        result.Succeeded.ShouldBeTrue();
        result.Stats.ShouldNotBeNull();
        result.Stats.FilePath.ShouldBe(temp.Path);
        result.Stats.FileSizeBytes.ShouldBe(new FileInfo(temp.Path).Length);
        runner.Requests.Single().FileName.ShouldBe(@"C:\tools\ffprobe.exe");
        runner.Requests.Single().Arguments.ShouldBe(
            new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", temp.Path });
    }

    [Fact]
    public async Task ProbeAsync_ReturnsFailureWhenFfprobeExitCodeIsNonZero()
    {
        using var temp = new TempVideoFile();
        var service = new FfprobeService("ffprobe.exe", new FakeProcessRunner
        {
            Result = new ProcessResult
            {
                ExitCode = 2,
                StandardOutput = string.Empty,
                StandardErrorPath = @"C:\temp\ffprobe.err",
                Elapsed = TimeSpan.FromMilliseconds(12)
            }
        }, new FfprobeJsonParser());

        var result = await service.ProbeAsync(temp.Path);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldNotBeNull();
        result.Failure.ExitCode.ShouldBe(2);
        result.Failure.StderrPath.ShouldBe(@"C:\temp\ffprobe.err");
    }

    [Fact]
    public async Task ProbeAsync_ReturnsFailureWhenJsonCannotBeParsed()
    {
        using var temp = new TempVideoFile();
        var service = new FfprobeService("ffprobe.exe", new FakeProcessRunner
        {
            Result = SuccessfulResult("{")
        }, new FfprobeJsonParser());

        var result = await service.ProbeAsync(temp.Path);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldNotBeNull();
        result.Failure.Message.ShouldContain("ffprobe JSON");
    }

    private static ProcessResult SuccessfulResult(string stdout) =>
        new()
        {
            ExitCode = 0,
            StandardOutput = stdout,
            StandardErrorPath = @"C:\temp\empty.err",
            Elapsed = TimeSpan.FromMilliseconds(10)
        };

    private static string Fixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffprobe", fileName);
        return File.ReadAllText(path);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];
        public ProcessResult Result { get; init; } = SuccessfulResult("{}");

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class TempVideoFile : IDisposable
    {
        public TempVideoFile()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"videotriage-{Guid.NewGuid():N}.mp4");
            File.WriteAllBytes(Path, [1, 2, 3, 4]);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
