using Shouldly;

namespace VideoTriage.App.Tests.Views;

public sealed class SummaryViewMarkupTests
{
    private static string ReadSummaryViewXaml()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VideoTriage.App", "Views", "SummaryView.xaml"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void SummaryViewMarkup_BindsTilesLegendAndReveal()
    {
        var xaml = ReadSummaryViewXaml();

        xaml.ShouldContain("{Binding ReplacedCount}");
        xaml.ShouldContain("{Binding KeptOriginalCount}");
        xaml.ShouldContain("{Binding OverallReductionText}");
        xaml.ShouldContain("RevealCommand");
        xaml.ShouldContain("ItemsSource=\"{Binding Segments}\"");
    }

    [Fact]
    public void SummaryViewMarkup_DropsRemovedMembers()
    {
        var xaml = ReadSummaryViewXaml();

        xaml.ShouldNotContain("AverageReductionText");
        xaml.ShouldNotContain("KeptCount}");
        xaml.ShouldNotContain("<DataGrid");
    }
}
