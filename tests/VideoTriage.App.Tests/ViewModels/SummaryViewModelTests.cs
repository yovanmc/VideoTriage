using Shouldly;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class SummaryViewModelTests
{
    private static FileProgress Done(string path, TriageOutcome o, long src = 0, long? outBytes = null, double? saved = null) =>
        new()
        {
            FilePath = path,
            Phase = TriagePhase.Done,
            Outcome = o,
            Source = src == 0 ? null : new VideoStats
            {
                FilePath = path, CodecName = "h264", Width = 1920, Height = 1080,
                FramesPerSecond = 30, Duration = TimeSpan.FromMinutes(1), FileSizeBytes = src, HasAudio = true,
            },
            OutputBytes = outBytes,
            SavedPercent = saved,
        };

    private static TriageSummary Summary(params FileProgress[] files) => new()
    {
        Scanned = files.Length, Candidates = files.Length, Replaced = 0, Marginal = 0,
        Grew = 0, Invalid = 0, Failed = 0, Skipped = 0, BytesSaved = 0,
        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
        CompletedAtUtc = DateTimeOffset.UtcNow,
        Files = files,
    };

    [Fact]
    public void Files_ExcludeNonProcessedOutcomes()
    {
        var vm = new SummaryViewModel(Summary(
            Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50),
            Done(@"C:\b.mp4", TriageOutcome.SkippedAlreadyAv1),
            Done(@"C:\c.mp4", TriageOutcome.SkippedLowBpp),
            Done(@"C:\d.mp4", TriageOutcome.InsufficientSpace, 2000)));

        vm.Files.Select(f => f.FileName).ShouldBe(["a.mp4", "d.mp4"], ignoreOrder: true);
        vm.ProcessedCount.ShouldBe(2);
    }

    [Fact]
    public void Segments_ReconcileWithProcessedCount()
    {
        var vm = new SummaryViewModel(Summary(
            Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50),
            Done(@"C:\e.mp4", TriageOutcome.GrewKeptOriginal, 1000, 1100),
            Done(@"C:\f.mp4", TriageOutcome.EncodeFailed, 1000)));

        vm.Segments.Sum(s => s.Count).ShouldBe(vm.ProcessedCount);
        vm.Segments.Select(s => s.Label).ShouldContain("Replaced");
        vm.Segments.Select(s => s.Label).ShouldContain("Kept larger");
        vm.Segments.Select(s => s.Label).ShouldContain("Failed");
    }

    [Fact]
    public void Severity_IsWarning_WhenAnyNonReplacedProcessed()
    {
        var ok = new SummaryViewModel(Summary(Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50)));
        ok.Severity.ShouldBe(SummarySeverity.Success);

        var warn = new SummaryViewModel(Summary(
            Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50),
            Done(@"C:\g.mp4", TriageOutcome.InsufficientSpace, 2000)));
        warn.Severity.ShouldBe(SummarySeverity.Warning);
    }

    [Fact]
    public void EmptyRun_SeverityNone()
    {
        new SummaryViewModel(Summary()).Severity.ShouldBe(SummarySeverity.None);
    }

    [Fact]
    public void ReplacedRow_HasSizeTransitionAndSaved()
    {
        var vm = new SummaryViewModel(Summary(Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50)));
        var row = vm.Files.Single();
        row.OldSizeText.ShouldNotBeNullOrEmpty();
        row.NewSizeText.ShouldNotBeNullOrEmpty();
        row.SavedText!.ShouldContain("50");
        row.StatusLabel.ShouldBe("Replaced");
    }

    [Fact]
    public void NonReplacedRow_HasNoNewSizeNoSaved()
    {
        var vm = new SummaryViewModel(Summary(Done(@"C:\e.mp4", TriageOutcome.GrewKeptOriginal, 1000, 1100)));
        var row = vm.Files.Single();
        row.NewSizeText.ShouldBeNullOrEmpty();
        row.SavedText.ShouldBeNull();
        row.StatusLabel.ShouldBe(TriageOutcomeDisplay.Label(TriageOutcome.GrewKeptOriginal));
    }

    [Fact]
    public void DurationText_IsPresent()
    {
        var vm = new SummaryViewModel(Summary(Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50)));
        vm.DurationText.ShouldNotBeNullOrWhiteSpace();
        vm.CompletedAtText.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RevealCommand_NullPath_DoesNotThrow()
    {
        var vm = new SummaryViewModel(Summary(Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50)));
        vm.RevealCommand.Execute(null);
    }
}
