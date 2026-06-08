using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Tools;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Tests.Verify;

public sealed class OutputVerifierIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public OutputVerifierIntegrationTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "VideoTriage.IntegrationTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task VerifyAsync_RealFfmpegOutput_ReturnsValid()
    {
        var locator = new ToolLocator();
        var ffmpegPath = locator.FindOnPath("ffmpeg");
        var ffprobePath = locator.FindOnPath("ffprobe");
        if (ffmpegPath is null || ffprobePath is null)
            return;

        var runner = new ProcessRunner();
        var syntheticPath = Path.Combine(_tempDir, "synth.mp4");
        var generateResult = await runner.RunAsync(new ProcessRequest
        {
            FileName = ffmpegPath,
            Arguments =
            [
                "-nostdin",
                "-y",
                "-f",
                "lavfi",
                "-i",
                "testsrc=duration=1:size=320x240:rate=30",
                "-f",
                "lavfi",
                "-i",
                "sine=frequency=440:duration=1",
                "-c:v",
                "libx264",
                "-c:a",
                "aac",
                syntheticPath
            ],
            StderrDirectory = _tempDir,
            Timeout = TimeSpan.FromSeconds(30)
        });

        generateResult.Succeeded.ShouldBeTrue(
            $"ffmpeg failed to generate synthetic video: exit {generateResult.ExitCode}");

        var probeService = new FfprobeService(
            ffprobePath,
            runner,
            new FfprobeJsonParser());
        var probeResult = await probeService.ProbeAsync(syntheticPath);
        probeResult.Succeeded.ShouldBeTrue("ffprobe failed to probe synthetic video");

        var verifier = new OutputVerifier(ffmpegPath, runner, probeService);
        var result = await verifier.VerifyAsync(
            probeResult.Stats!,
            syntheticPath,
            new TriageOptions());

        result.Outcome.ShouldBe(
            VerificationOutcome.Valid,
            $"Expected Valid but got {result.Outcome}: {result.Reason}");
        result.OutputStats.ShouldNotBeNull();
    }
}
