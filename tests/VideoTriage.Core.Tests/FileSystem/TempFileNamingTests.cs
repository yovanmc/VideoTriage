using Shouldly;
using VideoTriage.Core.FileSystem;

namespace VideoTriage.Core.Tests.FileSystem;

public sealed class TempFileNamingTests
{
    [Theory]
    [InlineData("clip.videotriage.tmp.42.mp4")]
    [InlineData("clip.videotriage.staging.42.mp4")]
    [InlineData("clip.videotriage.partial.42.mp4")]
    [InlineData("clip.videotriage.poster.42.jpg")]
    public void IsTempArtifact_KnownMarker_ReturnsTrue(string path) =>
        TempFileNaming.IsTempArtifact(path).ShouldBeTrue();

    [Fact]
    public void IsTempArtifact_RegularVideo_ReturnsFalse() =>
        TempFileNaming.IsTempArtifact(@"C:\Videos\clip.mp4").ShouldBeFalse();

    [Fact]
    public void EncodePath_UsesSourceDirectoryAndMp4Extension() =>
        TempFileNaming.EncodePath(@"C:\Videos\clip.mov", 42)
            .ShouldBe(@"C:\Videos\clip.videotriage.tmp.42.mp4");

    [Fact]
    public void StagingPath_IsDistinctFromEncodePath_ForSameSourceAndPid()
    {
        TempFileNaming.StagingPath(@"C:\Videos\clip.mov", 42)
            .ShouldBe(@"C:\Videos\clip.videotriage.staging.42.mp4");
        TempFileNaming.StagingPath(@"C:\Videos\clip.mov", 42)
            .ShouldNotBe(TempFileNaming.EncodePath(@"C:\Videos\clip.mov", 42));
    }

    [Fact]
    public void PartialPath_UsesPartialInfix() =>
        TempFileNaming.PartialPath(@"C:\Videos\clip.mov", 7)
            .ShouldBe(@"C:\Videos\clip.videotriage.partial.7.mp4");

    [Fact]
    public void PosterImagePath_UsesJpgExtension() =>
        TempFileNaming.PosterImagePath(@"C:\Videos\clip.videotriage.tmp.7.mp4", 7)
            .ShouldBe(@"C:\Videos\clip.videotriage.tmp.7.videotriage.poster.7.jpg");

    [Fact]
    public void PosterMuxPath_UsesMp4Extension() =>
        TempFileNaming.PosterMuxPath(@"C:\Videos\clip.videotriage.tmp.7.mp4", 7)
            .ShouldBe(@"C:\Videos\clip.videotriage.tmp.7.videotriage.poster.7.mp4");
}
