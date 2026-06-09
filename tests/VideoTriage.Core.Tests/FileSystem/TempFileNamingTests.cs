using Shouldly;
using VideoTriage.Core.FileSystem;

namespace VideoTriage.Core.Tests.FileSystem;

public sealed class TempFileNamingTests
{
    // Fixed GUIDs used in deterministic assertion tests.
    private static readonly Guid TxA = Guid.Parse("00000000-0000-0000-0000-000000000042");
    private static readonly Guid TxB = Guid.Parse("00000000-0000-0000-0000-000000000007");

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
        TempFileNaming.EncodePath(@"C:\Videos\clip.mov", TxA)
            .ShouldBe(@"C:\Videos\clip.videotriage.tmp.00000000000000000000000000000042.mp4");

    [Fact]
    public void StagingPath_IsDistinctFromEncodePath_ForSameSourceAndTxId()
    {
        TempFileNaming.StagingPath(@"C:\Videos\clip.mov", TxA)
            .ShouldBe(@"C:\Videos\clip.videotriage.staging.00000000000000000000000000000042.mp4");
        TempFileNaming.StagingPath(@"C:\Videos\clip.mov", TxA)
            .ShouldNotBe(TempFileNaming.EncodePath(@"C:\Videos\clip.mov", TxA));
    }

    [Fact]
    public void PartialPath_UsesPartialInfix() =>
        TempFileNaming.PartialPath(@"C:\Videos\clip.mov", TxB)
            .ShouldBe(@"C:\Videos\clip.videotriage.partial.00000000000000000000000000000007.mp4");

    [Fact]
    public void PosterImagePath_UsesJpgExtension() =>
        TempFileNaming.PosterImagePath(
            @"C:\Videos\clip.videotriage.tmp.00000000000000000000000000000007.mp4", TxB)
            .ShouldBe(@"C:\Videos\clip.videotriage.tmp.00000000000000000000000000000007.videotriage.poster.00000000000000000000000000000007.jpg");

    [Fact]
    public void PosterMuxPath_UsesMp4Extension() =>
        TempFileNaming.PosterMuxPath(
            @"C:\Videos\clip.videotriage.tmp.00000000000000000000000000000007.mp4", TxB)
            .ShouldBe(@"C:\Videos\clip.videotriage.tmp.00000000000000000000000000000007.videotriage.poster.00000000000000000000000000000007.mp4");

    [Fact]
    public void EncodePath_TwoTransactionIds_DoNotCollide()
    {
        var first = TempFileNaming.EncodePath(@"C:\videos\clip.mov",
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var second = TempFileNaming.EncodePath(@"C:\videos\clip.mov",
            Guid.Parse("22222222-2222-2222-2222-222222222222"));

        first.ShouldNotBe(second);
    }

    [Fact]
    public void EncodePath_WithTransactionId_ContainsGuidInFilename()
    {
        var txId = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");
        var path = TempFileNaming.EncodePath(@"C:\videos\clip.mov", txId);

        Path.GetFileName(path).ShouldContain(txId.ToString("N"),
            Case.Insensitive);
    }
}
