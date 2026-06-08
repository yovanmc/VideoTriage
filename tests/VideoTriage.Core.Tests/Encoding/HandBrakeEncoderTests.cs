using Shouldly;
using VideoTriage.Core.Encoding;
using VideoTriage.Core.Models;
using VideoTriage.Core.Tools;

namespace VideoTriage.Core.Tests.Encoding;

public sealed class HandBrakeEncoderTests
{
    [Fact]
    public async Task EncodeAsync_BuildsPresetCommandAndReportsProgress()
    {
        var runner = new FakeRunner();
        var values = new List<double>();
        var encoder = new HandBrakeEncoder(
            "HandBrakeCLI.exe",
            runner,
            "preset.json",
            "VideoTriage AV1");

        var result = await encoder.EncodeAsync(
            "input.mov",
            "output.mp4",
            new InlineProgress<double>(values.Add));

        result.Succeeded.ShouldBeTrue();
        runner.Request!.Arguments.ShouldBe(
        [
            "--preset-import-file",
            "preset.json",
            "-Z",
            "VideoTriage AV1",
            "-i",
            "input.mov",
            "-o",
            "output.mp4",
            "--json"
        ]);
        values.ShouldBe([0.5]);
    }

    [Fact]
    public async Task EncodeAsync_NonzeroExit_ReturnsFailed()
    {
        var runner = new FakeRunner
        {
            Result = new ProcessResult
            {
                ExitCode = 7,
                StandardOutput = string.Empty,
                StandardErrorPath = "stderr.log",
                Elapsed = TimeSpan.FromSeconds(1)
            }
        };
        var encoder = new HandBrakeEncoder(
            "HandBrakeCLI.exe",
            runner,
            "preset.json",
            "VideoTriage AV1");

        var result = await encoder.EncodeAsync("input.mov", "output.mp4");

        result.Outcome.ShouldBe(EncodeOutcome.Failed);
        result.Succeeded.ShouldBeFalse();
        result.ExitCode.ShouldBe(7);
    }

    [Fact]
    public async Task EncodeAsync_CancelledRunner_ReturnsCancelled()
    {
        var encoder = new HandBrakeEncoder(
            "HandBrakeCLI.exe",
            new CancellingRunner(),
            "preset.json",
            "VideoTriage AV1");

        var result = await encoder.EncodeAsync("input.mov", "output.mp4");

        result.Outcome.ShouldBe(EncodeOutcome.Cancelled);
        result.Succeeded.ShouldBeFalse();
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public ProcessRequest? Request { get; private set; }
        public ProcessResult Result { get; init; } = new()
        {
            ExitCode = 0,
            StandardOutput = string.Empty,
            StandardErrorPath = "stderr.log",
            Elapsed = TimeSpan.FromSeconds(1)
        };

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            request.StandardOutputLines!.Report("""{"Working":{"Progress":0.5}}""");
            return Task.FromResult(Result);
        }
    }

    private sealed class CancellingRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromCanceled<ProcessResult>(new CancellationToken(canceled: true));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
