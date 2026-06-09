using System.IO;
using Shouldly;
using VideoTriage.Core.Probing;
using Xunit;

namespace VideoTriage.Core.Tests.Probing;

public sealed class FfprobeJsonParserTests
{
    [Fact]
    public void Parse_ReadsVideoCodecDimensionsFpsDurationBitrateAndAudio()
    {
        var stats = new FfprobeJsonParser().Parse(@"C:\videos\a.mp4", 1_000_000, Fixture("h264-with-audio.json"));

        stats.CodecName.ShouldBe("h264");
        stats.Width.ShouldBe(1920);
        stats.Height.ShouldBe(1080);
        stats.FramesPerSecond.ShouldBe(30000d / 1001d, tolerance: 0.000001);
        stats.Duration.ShouldBe(TimeSpan.FromSeconds(120.5));
        stats.VideoBitrateBitsPerSecond.ShouldBe(9_000_000);
        stats.ContainerBitrateBitsPerSecond.ShouldBe(9_500_000);
        stats.HasAudio.ShouldBeTrue();
        stats.FileSizeBytes.ShouldBe(1_000_000);
    }

    [Fact]
    public void Parse_PreservesAv1CodecName()
    {
        var stats = new FfprobeJsonParser().Parse(@"C:\videos\av1.mp4", 1_000_000, Fixture("av1-video.json"));

        stats.CodecName.ShouldBe("av1");
        stats.HasAudio.ShouldBeFalse();
    }

    [Fact]
    public void Parse_UsesContainerBitrateFallback()
    {
        var stats = new FfprobeJsonParser().Parse(@"C:\videos\b.mov", 1_000_000, Fixture("missing-video-bitrate.json"));

        stats.VideoBitrateBitsPerSecond.ShouldBeNull();
        stats.ContainerBitrateBitsPerSecond.ShouldBe(45_000_000);
        stats.EffectiveBitrateBitsPerSecond.ShouldBe(45_000_000);
    }

    [Fact]
    public void Parse_UsesFormatDurationFallback()
    {
        var stats = new FfprobeJsonParser().Parse(@"C:\videos\c.mkv", 1_000_000, Fixture("stream-duration-missing-format-duration.json"));

        stats.Duration.ShouldBe(TimeSpan.FromSeconds(42.25));
    }

    [Fact]
    public void Parse_ThrowsWhenNoVideoStream()
    {
        var exception = Should.Throw<InvalidDataException>(() =>
            new FfprobeJsonParser().Parse(@"C:\videos\audio.m4a", 1_000_000, Fixture("no-video-stream.json")));

        exception.Message.ShouldContain("video stream");
    }

    [Fact]
    public void Parse_ThrowsWhenJsonInvalid()
    {
        Should.Throw<InvalidDataException>(() =>
            new FfprobeJsonParser().Parse(@"C:\videos\bad.mp4", 1_000_000, "{"));
    }

    [Fact]
    public void Parse_ParsesZeroSlashZeroFrameRateAsInvalid()
    {
        const string json = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "h264",
              "width": 1920,
              "height": 1080,
              "avg_frame_rate": "0/0",
              "duration": "5.0",
              "bit_rate": "1000000"
            }
          ],
          "format": { "duration": "5.0" }
        }
        """;

        var exception = Should.Throw<InvalidDataException>(() =>
            new FfprobeJsonParser().Parse(@"C:\videos\badfps.mp4", 1_000_000, json));

        exception.Message.ShouldContain("frame rate");
    }

    [Fact]
    public void Parse_DetectsAttachedPicStreamIndex()
    {
        var stats = new FfprobeJsonParser().Parse(
            @"C:\videos\poster.mp4", 1_000_000, Fixture("h264-with-attached-pic.json"));

        stats.AttachedPicStreamIndex.ShouldBe(2);
    }

    [Fact]
    public void Parse_AttachedPicStreamIndexIsNullWhenNoPoster()
    {
        var stats = new FfprobeJsonParser().Parse(
            @"C:\videos\a.mp4", 1_000_000, Fixture("h264-with-audio.json"));

        stats.AttachedPicStreamIndex.ShouldBeNull();
    }

    private static string Fixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffprobe", fileName);
        return File.ReadAllText(path);
    }
}
