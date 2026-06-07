using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using Xunit;

namespace VideoTriage.Core.Tests.Probing;

public sealed class BppClassifierTests
{
    [Fact]
    public void Classify_SkipsAv1_WhenSkipAv1IsTrue()
    {
        var result = new BppClassifier().Classify(CreateStats(codecName: "av1", bpp: 0.25));

        result.Outcome.ShouldBe(ClassificationOutcome.SkipAlreadyAv1);
        result.IsCandidate.ShouldBeFalse();
        result.Reason.ShouldContain("already AV1");
    }

    [Fact]
    public void Classify_AllowsAv1_WhenSkipAv1IsFalse()
    {
        var result = new BppClassifier().Classify(
            CreateStats(codecName: "AV1", bpp: 0.25),
            new TriageOptions { SkipAv1 = false });

        result.Outcome.ShouldBe(ClassificationOutcome.Candidate);
    }

    [Theory]
    [InlineData(0.13)]
    [InlineData(0.20)]
    public void Classify_ReturnsCandidate_AtOrAboveThreshold(double bpp)
    {
        var result = new BppClassifier().Classify(CreateStats(codecName: "h264", bpp: bpp));

        result.Outcome.ShouldBe(ClassificationOutcome.Candidate);
        result.IsCandidate.ShouldBeTrue();
    }

    [Fact]
    public void Classify_SkipsLowBpp_BelowThreshold()
    {
        var result = new BppClassifier().Classify(CreateStats(codecName: "hevc", bpp: 0.129));

        result.Outcome.ShouldBe(ClassificationOutcome.SkipLowBpp);
        result.Reason.ShouldContain("below");
    }

    [Fact]
    public void Classify_CodecComparisonIsCaseInsensitive()
    {
        var result = new BppClassifier().Classify(CreateStats(codecName: "Av1", bpp: 0.40));

        result.Outcome.ShouldBe(ClassificationOutcome.SkipAlreadyAv1);
    }

    [Theory]
    [InlineData(0, 1080, 30, 5_000_000)]
    [InlineData(1920, 0, 30, 5_000_000)]
    [InlineData(1920, 1080, 0, 5_000_000)]
    [InlineData(1920, 1080, 30, 0)]
    public void Classify_InvalidMetadata_WhenGeometryFrameRateOrBitrateIsZero(
        int width,
        int height,
        double fps,
        long bitrate)
    {
        var stats = CreateStats(codecName: "h264", bpp: 0.20) with
        {
            Width = width,
            Height = height,
            FramesPerSecond = fps,
            VideoBitrateBitsPerSecond = bitrate
        };

        var result = new BppClassifier().Classify(stats);

        result.Outcome.ShouldBe(ClassificationOutcome.InvalidMetadata);
    }

    [Fact]
    public void Classify_InvalidMetadata_WhenDurationIsZero()
    {
        var stats = CreateStats(codecName: "h264", bpp: 0.20) with
        {
            Duration = TimeSpan.Zero
        };

        var result = new BppClassifier().Classify(stats);

        result.Outcome.ShouldBe(ClassificationOutcome.InvalidMetadata);
    }

    [Fact]
    public void Classify_UsesCustomThreshold()
    {
        var result = new BppClassifier().Classify(
            CreateStats(codecName: "h264", bpp: 0.15),
            new TriageOptions { CandidateBppThreshold = 0.16 });

        result.Outcome.ShouldBe(ClassificationOutcome.SkipLowBpp);
    }

    private static VideoStats CreateStats(string codecName, double bpp)
    {
        const int width = 1920;
        const int height = 1080;
        const double fps = 30;
        var bitrate = (long)Math.Round(bpp * width * height * fps);

        return new VideoStats
        {
            FilePath = @"C:\videos\sample.mp4",
            CodecName = codecName,
            Width = width,
            Height = height,
            FramesPerSecond = fps,
            Duration = TimeSpan.FromSeconds(60),
            FileSizeBytes = 30_000_000,
            VideoBitrateBitsPerSecond = bitrate,
            HasAudio = true
        };
    }
}
