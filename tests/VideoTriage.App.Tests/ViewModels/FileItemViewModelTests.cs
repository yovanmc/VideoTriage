using Shouldly;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Formatting;
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

    [Fact]
    public void Apply_Replaced_ShowsSizeTransitionText()
    {
        var row = new FileItemViewModel(@"C:\videos\movie.mp4");
        var fp = new FileProgress
        {
            FilePath = row.FilePath,
            Phase = TriagePhase.Done,
            Outcome = TriageOutcome.Replaced,
            Source = new VideoStats
            {
                FilePath = row.FilePath,
                CodecName = "h264",
                Width = 1920, Height = 1080,
                FramesPerSecond = 30,
                Duration = TimeSpan.FromMinutes(1),
                FileSizeBytes = 30_000_000,
                VideoBitrateBitsPerSecond = 12_441_600,
                HasAudio = true
            },
            OutputBytes = 19_500_000,
            SavedPercent = 35.0,
            FinalPath = @"C:\videos\movie.mp4"
        };

        row.Apply(fp);

        row.OldSizeText.ShouldBe(HumanSize.Format(30_000_000));
        row.SavedText.ShouldContain(HumanSize.Format(19_500_000));
        row.SavedText.ShouldContain("-35%");
    }

    [Fact]
    public void Apply_GrewKeptOriginal_ClearsSizeText()
    {
        var row = new FileItemViewModel(@"C:\videos\movie.mp4");
        var fp = new FileProgress
        {
            FilePath = row.FilePath,
            Phase = TriagePhase.Done,
            Outcome = TriageOutcome.GrewKeptOriginal
        };

        row.Apply(fp);

        row.OldSizeText.ShouldBe("");
        row.SavedText.ShouldBe("");
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
