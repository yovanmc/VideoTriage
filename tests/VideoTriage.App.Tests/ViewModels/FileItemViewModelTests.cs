using Shouldly;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class FileItemViewModelTests
{
    [Fact]
    public void ApplyProbe_Candidate_FormatsMetadataAndStatus()
    {
        var row = new FileItemViewModel(@"C:\videos\movie.mp4");

        row.ApplyProbe(Success(ClassificationOutcome.Candidate));

        row.FileName.ShouldBe("movie.mp4");
        row.MetaLine.ShouldBe("1920x1080 | 30 fps | 28.6 MB | bpp 0.2");
        row.StatusText.ShouldBe("Candidate");
    }

    [Theory]
    [InlineData(ClassificationOutcome.SkipAlreadyAv1, "Already AV1")]
    [InlineData(ClassificationOutcome.SkipLowBpp, "Below threshold")]
    [InlineData(ClassificationOutcome.InvalidMetadata, "Invalid metadata")]
    public void ApplyProbe_NonCandidate_MapsDistinctStatus(
        ClassificationOutcome outcome,
        string expected)
    {
        var row = new FileItemViewModel(@"C:\videos\movie.mp4");

        row.ApplyProbe(Success(outcome));

        row.StatusText.ShouldBe(expected);
    }

    [Fact]
    public void ApplyProbe_Failure_ShowsReasonWithoutThrowing()
    {
        var row = new FileItemViewModel(@"C:\videos\broken.mp4");
        var result = new ProbeResult
        {
            FilePath = row.FilePath,
            Failure = new ProbeFailure
            {
                FilePath = row.FilePath,
                Message = "no video stream"
            }
        };

        row.ApplyProbe(result);

        row.StatusText.ShouldBe("Probe failed: no video stream");
        row.MetaLine.ShouldBe("");
    }

    private static ProbeResult Success(ClassificationOutcome outcome)
    {
        var stats = new VideoStats
        {
            FilePath = @"C:\videos\movie.mp4",
            CodecName = "h264",
            Width = 1920,
            Height = 1080,
            FramesPerSecond = 30,
            Duration = TimeSpan.FromMinutes(1),
            FileSizeBytes = 30_000_000,
            VideoBitrateBitsPerSecond = 12_441_600,
            HasAudio = true
        };

        return new ProbeResult
        {
            FilePath = stats.FilePath,
            Stats = stats,
            Classification = new ClassificationResult
            {
                Outcome = outcome,
                Reason = outcome.ToString(),
                Stats = stats
            }
        };
    }
}
