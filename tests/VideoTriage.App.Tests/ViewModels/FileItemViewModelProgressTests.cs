using Shouldly;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Formatting;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class FileItemViewModelProgressTests
{
    [Fact]
    public void Apply_EncodingProgress_ShowsPercent()
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mp4",
            Phase = TriagePhase.Encoding,
            EncodeProgress = 0.43
        });

        vm.StatusText.ShouldBe("Encoding 43%");
        vm.Progress.ShouldBe(43);
    }

    [Theory]
    [InlineData(TriagePhase.Verifying, "Verifying output")]
    [InlineData(TriagePhase.EmbeddingPoster, "Embedding poster")]
    [InlineData(TriagePhase.Replacing, "Replacing original")]
    public void Apply_ActivePhase_ShowsPhaseText(TriagePhase phase, string expected)
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");

        vm.Apply(new FileProgress { FilePath = @"C:\Videos\clip.mp4", Phase = phase });

        vm.StatusText.ShouldBe(expected);
    }

    [Theory]
    [InlineData(TriageOutcome.OutputInvalid, "Verification failed — kept original")]
    [InlineData(TriageOutcome.GrewKeptOriginal, "Kept — encode was larger")]
    [InlineData(TriageOutcome.Cancelled, "Stopped")]
    [InlineData(TriageOutcome.ReplacePartial, "Replaced (recoverable partial) · saved 0%")]
    public void Apply_TerminalOutcome_ShowsSafetyText(TriageOutcome outcome, string expected)
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mp4",
            Phase = TriagePhase.Done,
            Outcome = outcome,
            FinalPath = @"C:\Videos\clip.mp4"
        });

        vm.StatusText.ShouldBe(expected);
    }

    [Fact]
    public void Apply_Replaced_ShowsSizeTransitionAndSavedPercentInStatus()
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mov");

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mov",
            Phase = TriagePhase.Done,
            Outcome = TriageOutcome.Replaced,
            Source = new VideoStats
            {
                FilePath = @"C:\Videos\clip.mov",
                CodecName = "h264",
                Width = 1920, Height = 1080,
                FramesPerSecond = 30,
                Duration = TimeSpan.FromMinutes(1),
                FileSizeBytes = 30_000_000,
                VideoBitrateBitsPerSecond = 12_000_000,
                HasAudio = true
            },
            OutputBytes = 9_750_000,
            SavedPercent = 67.5,
            FinalPath = @"C:\Videos\clip.mp4"
        });

        vm.StatusText.ShouldContain("saved 67.5%");
        vm.OldSizeText.ShouldNotBeNullOrEmpty();
        // Saved % shows only on the status line, not duplicated in the green new-size line.
        vm.SavedText.ShouldNotContain("%");
        vm.IsComplete.ShouldBeTrue();
        vm.FinalPath.ShouldBe(@"C:\Videos\clip.mp4");
    }

    [Fact]
    public void Apply_Done_SetsProgressTo100()
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mp4",
            Phase = TriagePhase.Done,
            Outcome = TriageOutcome.Replaced,
            FinalPath = @"C:\Videos\clip.mp4"
        });

        vm.Progress.ShouldBe(100);
    }

    [Fact]
    public void Apply_EncodingWithoutProgress_IsProgressIndeterminate()
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mp4",
            Phase = TriagePhase.Encoding,
            EncodeProgress = null
        });

        vm.IsProgressIndeterminate.ShouldBeTrue();
    }

    [Fact]
    public void Apply_EncodingWithProgress_IsNotProgressIndeterminate()
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mp4",
            Phase = TriagePhase.Encoding,
            EncodeProgress = 0.5
        });

        vm.IsProgressIndeterminate.ShouldBeFalse();
        vm.Progress.ShouldBe(50);
    }

    [Theory]
    [InlineData(TriageOutcome.InsufficientSpace)]
    [InlineData(TriageOutcome.EncodeFailed)]
    [InlineData(TriageOutcome.OutputInvalid)]
    public void Apply_Done_UsesOutcomeDisplayLabel(TriageOutcome outcome)
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");
        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mp4",
            Phase = TriagePhase.Done,
            Outcome = outcome,
        });
        vm.StatusText.ShouldBe(TriageOutcomeDisplay.Label(outcome));
    }

    [Fact]
    public void Apply_Done_ClearsIsProgressIndeterminate()
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mp4",
            Phase = TriagePhase.Encoding,
            EncodeProgress = null
        });
        vm.IsProgressIndeterminate.ShouldBeTrue();

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mp4",
            Phase = TriagePhase.Done,
            Outcome = TriageOutcome.Replaced,
            FinalPath = @"C:\Videos\clip.mp4"
        });

        vm.IsProgressIndeterminate.ShouldBeFalse();
        vm.Progress.ShouldBe(100);
    }
}
