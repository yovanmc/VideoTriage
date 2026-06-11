using Shouldly;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class SummaryViewModelTests
{
    [Fact]
    public void ZeroFileRun_UsesZeroSafeValues()
    {
        var viewModel = new SummaryViewModel(Summary());

        viewModel.ProcessedCount.ShouldBe(0);
        viewModel.KeptCount.ShouldBe(0);
        viewModel.BytesSavedText.ShouldBe("0 B");
        viewModel.AverageReductionPercent.ShouldBe(0);
        viewModel.AverageReductionText.ShouldBe("0.0%");
        viewModel.Segments.Sum(x => x.Count).ShouldBe(0);
        viewModel.Files.ShouldBeEmpty();
    }

    [Fact]
    public void Projection_FormatsBytesAndComputesWeightedReduction()
    {
        var summary = Summary(
            scanned: 2,
            replaced: 2,
            bytesSaved: 750,
            files:
            [
                File("a.mp4", TriageOutcome.Replaced, sourceBytes: 1000, outputBytes: 500),
                File("b.mp4", TriageOutcome.ReplacePartial, sourceBytes: 500, outputBytes: 250)
            ]);

        var viewModel = new SummaryViewModel(summary);

        viewModel.BytesSavedText.ShouldBe("750 B");
        viewModel.AverageReductionPercent.ShouldBe(50);
        viewModel.AverageReductionText.ShouldBe("50.0%");
    }

    [Fact]
    public void Projection_DoesNotDoubleCountMarginalReplacements()
    {
        var viewModel = new SummaryViewModel(Summary(
            scanned: 15,
            replaced: 3,
            marginal: 2,
            grew: 2,
            invalid: 1,
            failed: 4,
            skipped: 5));

        viewModel.KeptCount.ShouldBe(12);
        viewModel.Segments.ShouldBe([
            new SummarySegment("Replaced", 3, "#36C98F"),
            new SummarySegment("Kept / grew", 2, "#F5A524"),
            new SummarySegment("Invalid", 1, "#8B93A7"),
            new SummarySegment("Failed", 4, "#F05252"),
            new SummarySegment("Skipped", 5, "#5B8DEF")
        ]);
        viewModel.Segments.Sum(x => x.Count).ShouldBe(15);
    }

    [Fact]
    public void Projection_UsesTerminalFileMessages()
    {
        var viewModel = new SummaryViewModel(Summary(
            scanned: 1,
            files: [File("clip.mp4", TriageOutcome.OutputInvalid, 1000, null, "Decode failed")]));

        viewModel.Files.Single().ShouldBe(new SummaryFileResult(
            "clip.mp4", "OutputInvalid", "Decode failed", null, null));
    }

    private static TriageSummary Summary(
        int scanned = 0,
        int replaced = 0,
        int marginal = 0,
        int grew = 0,
        int invalid = 0,
        int failed = 0,
        int skipped = 0,
        long bytesSaved = 0,
        IReadOnlyList<FileProgress>? files = null) => new()
        {
            Scanned = scanned,
            Candidates = replaced + grew + invalid + failed,
            Replaced = replaced,
            Marginal = marginal,
            Grew = grew,
            Invalid = invalid,
            Failed = failed,
            Skipped = skipped,
            BytesSaved = bytesSaved,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Files = files ?? []
        };

    private static FileProgress File(
        string path,
        TriageOutcome outcome,
        long sourceBytes,
        long? outputBytes,
        string message = "done") => new()
        {
            FilePath = path,
            Phase = TriagePhase.Done,
            Outcome = outcome,
            Source = new VideoStats
            {
                FilePath = path,
                FileSizeBytes = sourceBytes,
                Duration = TimeSpan.FromMinutes(1),
                Width = 1920,
                Height = 1080,
                FramesPerSecond = 30,
                VideoBitrateBitsPerSecond = 10_000_000,
                CodecName = "h264",
                HasAudio = true
            },
            OutputBytes = outputBytes,
            Message = message
        };
}
