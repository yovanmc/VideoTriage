using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.Poster;
using VideoTriage.Core.Tools;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Tests.Poster;

public sealed class PosterEmbedderTests
{
    [Fact]
    public async Task EmbedAsync_Success_ReturnsMuxedPath()
    {
        var runner = new FakeRunner([0, 0]);
        var verifier = new FakeVerifier(valid: true);
        var embedder = new PosterEmbedder("ffmpeg.exe", runner, verifier);

        var result = await embedder.EmbedAsync("encode.mp4", Source(), new TriageOptions());

        result.Embedded.ShouldBeTrue();
        result.OutputPath.ShouldEndWith(".mp4");
        result.OutputPath.ShouldContain(".videotriage.poster.");
        verifier.Paths.Single().ShouldBe(result.OutputPath);
        runner.Requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public async Task EmbedAsync_FfmpegFailure_ReturnsOriginalPath(int grabExit, int muxExit)
    {
        var runner = new FakeRunner([grabExit, muxExit]);
        var embedder = new PosterEmbedder("ffmpeg.exe", runner, new FakeVerifier(valid: true));

        var result = await embedder.EmbedAsync("encode.mp4", Source(), new TriageOptions());

        result.Embedded.ShouldBeFalse();
        result.OutputPath.ShouldBe("encode.mp4");
    }

    [Fact]
    public async Task EmbedAsync_ReverifyFailure_ReturnsOriginalPath()
    {
        var embedder = new PosterEmbedder(
            "ffmpeg.exe",
            new FakeRunner([0, 0]),
            new FakeVerifier(valid: false));

        var result = await embedder.EmbedAsync("encode.mp4", Source(), new TriageOptions());

        result.Embedded.ShouldBeFalse();
        result.OutputPath.ShouldBe("encode.mp4");
    }

    [Fact]
    public async Task EmbedAsync_Disabled_DoesNotRunFfmpeg()
    {
        var runner = new FakeRunner([0, 0]);
        var embedder = new PosterEmbedder("ffmpeg.exe", runner, new FakeVerifier(valid: true));

        var result = await embedder.EmbedAsync(
            "encode.mp4",
            Source(),
            new TriageOptions { EmbedPoster = false });

        result.Embedded.ShouldBeFalse();
        result.OutputPath.ShouldBe("encode.mp4");
        runner.Requests.ShouldBeEmpty();
    }

    private static VideoStats Source() => new()
    {
        FilePath = "source.mov",
        CodecName = "h264",
        Width = 1920,
        Height = 1080,
        FramesPerSecond = 30,
        Duration = TimeSpan.FromSeconds(100),
        FileSizeBytes = 100_000_000,
        VideoBitrateBitsPerSecond = 20_000_000,
        HasAudio = true
    };

    private sealed class FakeRunner(IReadOnlyList<int> exitCodes) : IProcessRunner
    {
        private int _index;
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var exitCode = exitCodes[Math.Min(_index, exitCodes.Count - 1)];
            _index++;

            return Task.FromResult(new ProcessResult
            {
                ExitCode = exitCode,
                StandardOutput = "",
                StandardErrorPath = "",
                Elapsed = TimeSpan.FromMilliseconds(1)
            });
        }
    }

    private sealed class FakeVerifier(bool valid) : IOutputVerifier
    {
        public List<string> Paths { get; } = [];

        public Task<VerificationResult> VerifyAsync(
            VideoStats source,
            string outputPath,
            TriageOptions options,
            CancellationToken cancellationToken = default)
        {
            Paths.Add(outputPath);
            return Task.FromResult(new VerificationResult
            {
                Outcome = valid ? VerificationOutcome.Valid : VerificationOutcome.DecodeError,
                Reason = valid ? "ok" : "decode error",
                OutputStats = valid ? source : null
            });
        }
    }
}
