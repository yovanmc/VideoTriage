using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Tools;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Tests.Verify;

public sealed class OutputVerifierTests : IDisposable
{
    private readonly string _tempDir;

    public OutputVerifierTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "VideoTriage.VerifierTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task VerifyAsync_FileDoesNotExist_ReturnsMissingOrEmpty()
    {
        var verifier = new OutputVerifier(
            "ffmpeg.exe",
            new FakeProcessRunner(),
            new FakeProbeService(ProbeResults.Fail(@"C:\missing.mp4", "not found")));

        var result = await verifier.VerifyAsync(
            MakeSource(),
            Path.Combine(_tempDir, "does-not-exist.mp4"),
            DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.MissingOrEmpty);
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task VerifyAsync_ZeroByteFile_ReturnsMissingOrEmpty()
    {
        var outputPath = TempFile("empty.mp4", []);
        var verifier = new OutputVerifier(
            "ffmpeg.exe",
            new FakeProcessRunner(),
            new FakeProbeService(ProbeResults.Fail(outputPath, "empty")));

        var result = await verifier.VerifyAsync(MakeSource(), outputPath, DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.MissingOrEmpty);
    }

    [Fact]
    public async Task VerifyAsync_ProbeFails_ReturnsProbeFailed()
    {
        var outputPath = TempFile("output.mp4");
        var failure = new ProbeFailure
        {
            FilePath = outputPath,
            Message = "ffprobe exited 1",
            ExitCode = 1
        };
        var probeResult = new ProbeResult { FilePath = outputPath, Failure = failure };
        var verifier = new OutputVerifier(
            "ffmpeg.exe",
            new FakeProcessRunner(),
            new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(MakeSource(), outputPath, DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.ProbeFailed);
        result.Reason.ShouldContain("ffprobe exited 1");
    }

    [Fact]
    public async Task VerifyAsync_NoVideoStream_ReturnsProbeFailed()
    {
        var outputPath = TempFile("output.mp4");
        var probeResult = new ProbeResult
        {
            FilePath = outputPath,
            Failure = new ProbeFailure
            {
                FilePath = outputPath,
                Message = "no video stream"
            }
        };
        var verifier = new OutputVerifier(
            "ffmpeg.exe",
            new FakeProcessRunner(),
            new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(
            MakeSource(),
            outputPath,
            DefaultOptions with { DeepVerify = false });

        result.Outcome.ShouldBe(VerificationOutcome.ProbeFailed);
    }

    [Fact]
    public async Task VerifyAsync_DurationTooFarOff_ReturnsDurationMismatch()
    {
        var outputPath = TempFile("output.mp4");
        var outputStats = MakeOutput(durationSec: 50);
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var verifier = new OutputVerifier(
            "ffmpeg.exe",
            new FakeProcessRunner(),
            new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(
            MakeSource(durationSec: 120),
            outputPath,
            DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.DurationMismatch);
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task VerifyAsync_DownscaledOutput_ReturnsResolutionMismatch()
    {
        var outputPath = TempFile("output.mp4");
        var outputStats = MakeOutput(width: 506, height: 676);
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var verifier = new OutputVerifier(
            "ffmpeg.exe",
            new FakeProcessRunner(),
            new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(
            MakeSource(width: 1010, height: 1354),
            outputPath,
            DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.ResolutionMismatch);
    }

    [Fact]
    public async Task VerifyAsync_RequireResolutionMatchFalse_SkipsResolutionCheck()
    {
        var outputPath = TempFile("output.mp4");
        var outputStats = MakeOutput(width: 640, height: 360);
        var stderrPath = StderrFile(string.Empty);
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var runner = new FakeProcessRunner(stderrPath);
        var verifier = new OutputVerifier("ffmpeg.exe", runner, new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(
            MakeSource(),
            outputPath,
            DefaultOptions with { RequireResolutionMatch = false });

        result.Outcome.ShouldBe(VerificationOutcome.Valid);
    }

    [Fact]
    public async Task VerifyAsync_SourceHasAudioButOutputDoesNot_ReturnsAudioMissing()
    {
        var outputPath = TempFile("output.mp4");
        var outputStats = MakeOutput(hasAudio: false);
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var verifier = new OutputVerifier(
            "ffmpeg.exe",
            new FakeProcessRunner(),
            new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(
            MakeSource(hasAudio: true),
            outputPath,
            DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.AudioMissing);
    }

    [Fact]
    public async Task VerifyAsync_RequireAudioParityFalse_SkipsAudioCheck()
    {
        var outputPath = TempFile("output.mp4");
        var outputStats = MakeOutput(hasAudio: false);
        var stderrPath = StderrFile(string.Empty);
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var runner = new FakeProcessRunner(stderrPath);
        var verifier = new OutputVerifier("ffmpeg.exe", runner, new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(
            MakeSource(hasAudio: true),
            outputPath,
            DefaultOptions with { RequireAudioParity = false });

        result.Outcome.ShouldBe(VerificationOutcome.Valid);
    }

    [Fact]
    public async Task VerifyAsync_SourceHasNoAudio_AudioCheckIsSkipped()
    {
        var outputPath = TempFile("output.mp4");
        var outputStats = MakeOutput(hasAudio: false);
        var stderrPath = StderrFile(string.Empty);
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var runner = new FakeProcessRunner(stderrPath);
        var verifier = new OutputVerifier("ffmpeg.exe", runner, new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(
            MakeSource(hasAudio: false),
            outputPath,
            DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.Valid);
    }

    [Fact]
    public async Task VerifyAsync_DeepDecodeFindsRealError_ReturnsDecodeError()
    {
        var outputPath = TempFile("output.mp4");
        var stderrPath = StderrFile("error while decoding MB 42 50, bytestream -7");
        var outputStats = MakeOutput();
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var runner = new FakeProcessRunner(stderrPath);
        var verifier = new OutputVerifier("ffmpeg.exe", runner, new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(MakeSource(), outputPath, DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.DecodeError);
        result.Reason.ShouldContain("error while decoding");
    }

    [Fact]
    public async Task VerifyAsync_DeepDecodeNonzeroExit_ReturnsDecodeError()
    {
        var outputPath = TempFile("output.mp4");
        var stderrPath = StderrFile(string.Empty);
        var outputStats = MakeOutput();
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var runner = new FakeProcessRunner(stderrPath) { ExitCode = 9 };
        var verifier = new OutputVerifier("ffmpeg.exe", runner, new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(MakeSource(), outputPath, DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.DecodeError);
        result.Reason.ShouldContain("ffmpeg exited 9");
    }

    [Fact]
    public async Task VerifyAsync_DeepDecodeHasOnlyBenignDtsNoise_ReturnsValid()
    {
        var outputPath = TempFile("output.mp4");
        var stderrPath = StderrFile(
            "non monotonically increasing dts to muxer in stream 0\r\n" +
            "Last message repeated 3 times");
        var outputStats = MakeOutput();
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var runner = new FakeProcessRunner(stderrPath);
        var verifier = new OutputVerifier("ffmpeg.exe", runner, new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(MakeSource(), outputPath, DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.Valid);
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task VerifyAsync_DeepVerifyFalse_DoesNotRunFfmpeg()
    {
        var outputPath = TempFile("output.mp4");
        var outputStats = MakeOutput();
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var runner = new FakeProcessRunner();
        var verifier = new OutputVerifier("ffmpeg.exe", runner, new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(
            MakeSource(),
            outputPath,
            DefaultOptions with { DeepVerify = false });

        result.Outcome.ShouldBe(VerificationOutcome.Valid);
        runner.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task VerifyAsync_AllChecksPass_ReturnsValidWithOutputStats()
    {
        var outputPath = TempFile("output.mp4");
        var stderrPath = StderrFile(string.Empty);
        var outputStats = MakeOutput();
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var runner = new FakeProcessRunner(stderrPath);
        var verifier = new OutputVerifier("ffmpeg.exe", runner, new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(MakeSource(), outputPath, DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.Valid);
        result.IsValid.ShouldBeTrue();
        result.OutputStats.ShouldBe(outputStats);
    }

    [Fact]
    public async Task VerifyAsync_DeepDecodeCommandUsesCorrectFfmpegArgs()
    {
        var outputPath = TempFile("output.mp4");
        var stderrPath = StderrFile(string.Empty);
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = MakeOutput() };
        var runner = new FakeProcessRunner(stderrPath);
        var verifier = new OutputVerifier(
            @"C:\tools\ffmpeg.exe",
            runner,
            new FakeProbeService(probeResult));

        await verifier.VerifyAsync(MakeSource(), outputPath, DefaultOptions);

        runner.LastRequest.ShouldNotBeNull();
        runner.LastRequest!.FileName.ShouldBe(@"C:\tools\ffmpeg.exe");
        runner.LastRequest.Arguments.ShouldBe(
            ["-nostdin", "-v", "error", "-i", outputPath, "-f", "null", "-"]);
        runner.LastRequest.StderrDirectory.ShouldNotBeNull();
    }

    [Fact]
    public async Task VerifyAsync_RotatedPhoneVideo_ResolutionSwapPasses()
    {
        var outputPath = TempFile("output.mp4");
        var stderrPath = StderrFile(string.Empty);
        var outputStats = MakeOutput(width: 1920, height: 1080);
        var probeResult = new ProbeResult { FilePath = outputPath, Stats = outputStats };
        var runner = new FakeProcessRunner(stderrPath);
        var verifier = new OutputVerifier("ffmpeg.exe", runner, new FakeProbeService(probeResult));

        var result = await verifier.VerifyAsync(
            MakeSource(width: 1080, height: 1920),
            outputPath,
            DefaultOptions);

        result.Outcome.ShouldBe(VerificationOutcome.Valid);
    }

    private string TempFile(string name, byte[]? content = null)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, content ?? [1, 2, 3, 4]);
        return path;
    }

    private string StderrFile(string stderrContent)
    {
        var path = Path.Combine(_tempDir, $"stderr-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, stderrContent);
        return path;
    }

    private static VideoStats MakeSource(
        int width = 1920,
        int height = 1080,
        double fps = 30,
        double durationSec = 120,
        bool hasAudio = true,
        long videoBitrate = 8_000_000) =>
        new()
        {
            FilePath = @"C:\videos\original.mp4",
            CodecName = "h264",
            Width = width,
            Height = height,
            FramesPerSecond = fps,
            Duration = TimeSpan.FromSeconds(durationSec),
            FileSizeBytes = 500_000_000,
            VideoBitrateBitsPerSecond = videoBitrate,
            HasAudio = hasAudio
        };

    private static VideoStats MakeOutput(
        int width = 1920,
        int height = 1080,
        double fps = 30,
        double durationSec = 120,
        bool hasAudio = true,
        long videoBitrate = 2_000_000) =>
        new()
        {
            FilePath = @"C:\videos\output.mp4",
            CodecName = "av1",
            Width = width,
            Height = height,
            FramesPerSecond = fps,
            Duration = TimeSpan.FromSeconds(durationSec),
            FileSizeBytes = 150_000_000,
            VideoBitrateBitsPerSecond = videoBitrate,
            HasAudio = hasAudio
        };

    private static TriageOptions DefaultOptions => new();

    private sealed class FakeProcessRunner(string? stderrPath = null) : IProcessRunner
    {
        public int CallCount { get; private set; }
        public int ExitCode { get; init; }
        public ProcessRequest? LastRequest { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new ProcessResult
            {
                ExitCode = ExitCode,
                StandardOutput = string.Empty,
                StandardErrorPath = stderrPath ?? string.Empty,
                Elapsed = TimeSpan.FromMilliseconds(50)
            });
        }
    }

    private sealed class FakeProbeService(ProbeResult result) : IFfprobeService
    {
        public Task<ProbeResult> ProbeAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result with { FilePath = filePath });
        }
    }

    private static class ProbeResults
    {
        public static ProbeResult Fail(string filePath, string message) =>
            new()
            {
                FilePath = filePath,
                Failure = new ProbeFailure { FilePath = filePath, Message = message }
            };
    }
}
