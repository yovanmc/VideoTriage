using Shouldly;
using VideoTriage.App.Controls;
using VideoTriage.App.ViewModels;

namespace VideoTriage.App.Tests.Controls;

public sealed class DonutChartTests
{
    [Fact]
    public void BuildSlices_ZeroTotal_ReturnsOneNeutralFullRing()
    {
        var slices = DonutChart.BuildSlices([]);

        slices.ShouldBe([
            new DonutSlice(0, 360, "#3A3F4B")
        ]);
    }

    [Fact]
    public void BuildSlices_PositiveCounts_ReturnsProportionalAngles()
    {
        var slices = DonutChart.BuildSlices([
            new SummarySegment("A", 1, "#111111"),
            new SummarySegment("B", 3, "#222222")
        ]);

        slices[0].StartAngle.ShouldBe(0);
        slices[0].SweepAngle.ShouldBe(90);
        slices[1].StartAngle.ShouldBe(90);
        slices[1].SweepAngle.ShouldBe(270);
    }

    [Fact]
    public void BuildSlices_IgnoresNonPositiveSegments()
    {
        var slices = DonutChart.BuildSlices([
            new SummarySegment("Zero", 0, "#000000"),
            new SummarySegment("Negative", -1, "#111111"),
            new SummarySegment("Positive", 2, "#222222")
        ]);

        slices.ShouldBe([new DonutSlice(0, 360, "#222222")]);
    }
}
