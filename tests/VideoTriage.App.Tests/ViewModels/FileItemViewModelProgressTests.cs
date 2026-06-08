using Shouldly;
using VideoTriage.App.ViewModels;
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
    [InlineData(TriageOutcome.OutputInvalid, "Verification failed; original kept")]
    [InlineData(TriageOutcome.GrewKeptOriginal, "Encode grew; original kept")]
    [InlineData(TriageOutcome.Cancelled, "Cancelled; original kept")]
    [InlineData(TriageOutcome.ReplacePartial, "Saved as recoverable partial")]
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
    public void Apply_Replaced_ShowsSavedPercentAndFinalPath()
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mov");

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mov",
            Phase = TriagePhase.Done,
            Outcome = TriageOutcome.Replaced,
            SavedPercent = 68.7,
            FinalPath = @"C:\Videos\clip.mp4"
        });

        vm.StatusText.ShouldBe("Saved 68.7%");
        vm.SavedText.ShouldContain(@"C:\Videos\clip.mp4");
    }
}
