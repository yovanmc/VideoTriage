using Shouldly;
using VideoTriage.Core.Models;
using Xunit;

namespace VideoTriage.Core.Tests.Models;

public sealed class VideoStatsTests
{
    [Fact]
    public void BitsPerPixel_UsesVideoBitrateFirst()
    {
        var stats = CreateStats(videoBitrate: 8_294_400, containerBitrate: 99_000_000);

        stats.EffectiveBitrateBitsPerSecond.ShouldBe(8_294_400);
        stats.BitsPerPixel.ShouldBe(8_294_400d / (1920 * 1080 * 30), tolerance: 0.000001);
    }

    [Fact]
    public void BitsPerPixel_FallsBackToContainerBitrate()
    {
        var stats = CreateStats(videoBitrate: null, containerBitrate: 4_147_200);

        stats.EffectiveBitrateBitsPerSecond.ShouldBe(4_147_200);
        stats.BitsPerPixel.ShouldBe(4_147_200d / (1920 * 1080 * 30), tolerance: 0.000001);
    }

    [Fact]
    public void BitsPerPixel_FallsBackToFileSizeAndDuration()
    {
        var stats = CreateStats(videoBitrate: null, containerBitrate: null) with
        {
            FileSizeBytes = 30_000_000,
            Duration = TimeSpan.FromSeconds(60)
        };

        stats.EffectiveBitrateBitsPerSecond.ShouldBe(4_000_000);
        stats.BitsPerPixel.ShouldBe(4_000_000d / (1920 * 1080 * 30), tolerance: 0.000001);
    }

    [Theory]
    [InlineData(0, 1080, 30)]
    [InlineData(1920, 0, 30)]
    [InlineData(1920, 1080, 0)]
    public void BitsPerPixel_InvalidGeometryOrFrameRate_ReturnsZero(int width, int height, double fps)
    {
        var stats = CreateStats(videoBitrate: 5_000_000, containerBitrate: null) with
        {
            Width = width,
            Height = height,
            FramesPerSecond = fps
        };

        stats.BitsPerPixel.ShouldBe(0);
    }

    private static VideoStats CreateStats(long? videoBitrate, long? containerBitrate) =>
        new()
        {
            FilePath = @"C:\videos\sample.mp4",
            CodecName = "h264",
            Width = 1920,
            Height = 1080,
            FramesPerSecond = 30,
            Duration = TimeSpan.FromSeconds(120),
            FileSizeBytes = 120_000_000,
            VideoBitrateBitsPerSecond = videoBitrate,
            ContainerBitrateBitsPerSecond = containerBitrate,
            HasAudio = true
        };
}
