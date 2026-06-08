using Shouldly;
using VideoTriage.Core.Poster;

namespace VideoTriage.Core.Tests.Poster;

public sealed class PosterArgumentsTests
{
    [Fact]
    public void BuildFrameGrab_UsesThumbnailFilterAndTimestamp()
    {
        PosterArguments.BuildFrameGrab(
                "encode.mp4",
                "poster.jpg",
                TimeSpan.FromSeconds(12.5))
            .ShouldBe([
                "-nostdin", "-ss", "12.5", "-i", "encode.mp4",
                "-frames:v", "1", "-vf", "thumbnail", "-y", "poster.jpg"
            ]);
    }

    [Fact]
    public void BuildCoverMux_AttachesJpegAsCoverArt()
    {
        PosterArguments.BuildCoverMux("encode.mp4", "poster.jpg", "muxed.mp4")
            .ShouldBe([
                "-nostdin", "-i", "encode.mp4", "-i", "poster.jpg",
                "-map", "0", "-map", "1", "-c", "copy", "-c:v:1", "mjpeg",
                "-disposition:v:1", "attached_pic", "-y", "muxed.mp4"
            ]);
    }
}
