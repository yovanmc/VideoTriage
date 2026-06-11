using Shouldly;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class TriageOutcomeDisplayTests
{
    [Fact]
    public void Label_CoversEveryOutcome_NoBlanksNoRawEnumNames()
    {
        foreach (TriageOutcome o in Enum.GetValues<TriageOutcome>())
        {
            var label = TriageOutcomeDisplay.Label(o);
            label.ShouldNotBeNullOrWhiteSpace();
            // Raw multi-word PascalCase enum names must be humanized. "Replaced" is already a
            // clean English word, so it is allowed to coincide with the enum name.
            if (o is not TriageOutcome.Replaced)
                label.ShouldNotBe(o.ToString());
        }
    }

    [Theory]
    [InlineData(TriageOutcome.Replaced, true)]
    [InlineData(TriageOutcome.ReplacePartial, true)]
    [InlineData(TriageOutcome.GrewKeptOriginal, true)]
    [InlineData(TriageOutcome.EncodeFailed, true)]
    [InlineData(TriageOutcome.ReplaceFailed, true)]
    [InlineData(TriageOutcome.OutputInvalid, true)]
    [InlineData(TriageOutcome.InsufficientSpace, true)]
    [InlineData(TriageOutcome.Cancelled, true)]
    [InlineData(TriageOutcome.SkippedAlreadyAv1, false)]
    [InlineData(TriageOutcome.SkippedLowBpp, false)]
    [InlineData(TriageOutcome.InvalidMetadata, false)]
    [InlineData(TriageOutcome.DryRunCandidate, false)]
    [InlineData(TriageOutcome.AlreadyCompleted, false)]
    public void IsProcessed_PartitionsOutcomes(TriageOutcome o, bool processed) =>
        TriageOutcomeDisplay.IsProcessed(o).ShouldBe(processed);

    [Fact]
    public void GroupColor_IsHexForProcessedOutcomes()
    {
        foreach (TriageOutcome o in Enum.GetValues<TriageOutcome>())
            if (TriageOutcomeDisplay.IsProcessed(o))
                TriageOutcomeDisplay.GroupColor(o).ShouldStartWith("#");
    }

    [Theory]
    [InlineData(TriageOutcome.Replaced, false)]
    [InlineData(TriageOutcome.ReplacePartial, false)]
    [InlineData(TriageOutcome.GrewKeptOriginal, true)]
    [InlineData(TriageOutcome.InsufficientSpace, true)]
    [InlineData(TriageOutcome.EncodeFailed, true)]
    public void IsWarning_TrueForNonReplacedProcessed(TriageOutcome o, bool warn) =>
        TriageOutcomeDisplay.IsWarning(o).ShouldBe(warn);
}
