# Post Run Summary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Present immutable post-run totals, weighted space reduction, outcome distribution, and terminal per-file results after a successful pipeline run.

**Architecture:** `SummaryViewModel` is a pure projection of the contract's `TriageSummary`; it performs no I/O and does not retain a live pipeline reference. A package-free WPF `DonutChart` renders fixed-color segments, and `MainViewModel` navigates to the summary only when `RunAsync` returns normally.

**Tech Stack:** .NET 10, WPF `DrawingContext`, CommunityToolkit.Mvvm, xUnit, Shouldly.

---

## Scope Check

This plan owns summary projection, rendering, and completed-run navigation. It does not change Core
summary semantics, persistence, cancellation, or replacement policy. It assumes settings and run
controls are integrated.

**Working directory for every command:** `C:\Agent Projects\VideoTriage`

## File Structure

```text
src/VideoTriage.App/ViewModels/SummarySegment.cs           CREATE
src/VideoTriage.App/ViewModels/SummaryFileResult.cs        CREATE
src/VideoTriage.App/ViewModels/SummaryViewModel.cs         CREATE
src/VideoTriage.App/Controls/DonutChart.cs                 CREATE
src/VideoTriage.App/Views/SummaryView.xaml                 CREATE
src/VideoTriage.App/Views/SummaryView.xaml.cs              CREATE
src/VideoTriage.App/ViewModels/MainViewModel.cs            MODIFY - successful-run navigation
src/VideoTriage.App/Services/IDialogService.cs             MODIFY - open data directory contract
tests/VideoTriage.App.Tests/ViewModels/SummaryViewModelTests.cs CREATE
tests/VideoTriage.App.Tests/Controls/DonutChartTests.cs     CREATE
tests/VideoTriage.App.Tests/ViewModels/MainViewModelSummaryTests.cs CREATE
```

### Task 1: Project A `TriageSummary` Into Immutable Display Data

**Files:**
- Create: `src/VideoTriage.App/ViewModels/SummarySegment.cs`
- Create: `src/VideoTriage.App/ViewModels/SummaryFileResult.cs`
- Create: `src/VideoTriage.App/ViewModels/SummaryViewModel.cs`
- Create: `tests/VideoTriage.App.Tests/ViewModels/SummaryViewModelTests.cs`

- [ ] **Step 1: Write the failing projection tests**

Create `tests/VideoTriage.App.Tests/ViewModels/SummaryViewModelTests.cs`:

```csharp
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
                File("b.mp4", TriageOutcome.Replaced, sourceBytes: 500, outputBytes: 250)
            ]);

        var viewModel = new SummaryViewModel(summary);

        viewModel.BytesSavedText.ShouldBe("750 B");
        viewModel.AverageReductionPercent.ShouldBe(50);
        viewModel.AverageReductionText.ShouldBe("50.0%");
    }

    [Fact]
    public void Projection_BuildsStableOutcomeSegments()
    {
        var viewModel = new SummaryViewModel(Summary(
            replaced: 3,
            grew: 2,
            invalid: 1,
            failed: 4,
            skipped: 5));

        viewModel.Segments.ShouldBe([
            new SummarySegment("Replaced", 3, "#36C98F"),
            new SummarySegment("Kept / grew", 2, "#F5A524"),
            new SummarySegment("Invalid", 1, "#8B93A7"),
            new SummarySegment("Failed", 4, "#F05252"),
            new SummarySegment("Skipped", 5, "#5B8DEF")
        ]);
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
            Marginal = 0,
            Grew = grew,
            Invalid = invalid,
            Failed = failed,
            Skipped = skipped,
            BytesSaved = bytesSaved,
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
                VideoBitrateBitsPerSecond = 10_000_000,
                CodecName = "h264",
                HasAudio = true
            },
            OutputBytes = outputBytes,
            Message = message
        };
}
```

- [ ] **Step 2: Run red**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter SummaryViewModelTests
```

Expected: build fails with `CS0234` or `CS0246` because the summary projection types do not exist.

- [ ] **Step 3: Add the immutable records**

Create `src/VideoTriage.App/ViewModels/SummarySegment.cs`:

```csharp
namespace VideoTriage.App.ViewModels;

public sealed record SummarySegment(string Label, int Count, string Color);
```

Create `src/VideoTriage.App/ViewModels/SummaryFileResult.cs`:

```csharp
namespace VideoTriage.App.ViewModels;

public sealed record SummaryFileResult(
    string FilePath,
    string Outcome,
    string Message,
    string? SavedPercent,
    string? FinalPath);
```

- [ ] **Step 4: Add the complete projection**

Create `src/VideoTriage.App/ViewModels/SummaryViewModel.cs`:

```csharp
using System.Globalization;
using VideoTriage.Core.Formatting;
using VideoTriage.Core.Models;

namespace VideoTriage.App.ViewModels;

public sealed class SummaryViewModel
{
    public SummaryViewModel(TriageSummary summary)
    {
        ScannedCount = summary.Scanned;
        CandidateCount = summary.Candidates;
        ReplacedCount = summary.Replaced;
        ProcessedCount = summary.Files.Count(x => x.Phase == TriagePhase.Done);
        KeptCount = summary.Grew + summary.Marginal + summary.Invalid +
                    summary.Failed + summary.Skipped;
        BytesSaved = summary.BytesSaved;
        BytesSavedText = HumanSize.Format(summary.BytesSaved);

        var totalSourceBytes = summary.Files
            .Where(x => x.Outcome == TriageOutcome.Replaced)
            .Sum(x => x.Source?.FileSizeBytes ?? 0);
        AverageReductionPercent = totalSourceBytes == 0
            ? 0
            : 100d * summary.BytesSaved / totalSourceBytes;
        AverageReductionText = AverageReductionPercent.ToString("0.0", CultureInfo.CurrentCulture) + "%";

        Segments =
        [
            new SummarySegment("Replaced", summary.Replaced, "#36C98F"),
            new SummarySegment("Kept / grew", summary.Grew + summary.Marginal, "#F5A524"),
            new SummarySegment("Invalid", summary.Invalid, "#8B93A7"),
            new SummarySegment("Failed", summary.Failed, "#F05252"),
            new SummarySegment("Skipped", summary.Skipped, "#5B8DEF")
        ];

        Files = summary.Files
            .Where(x => x.Phase == TriagePhase.Done)
            .Select(x => new SummaryFileResult(
                x.FilePath,
                x.Outcome?.ToString() ?? "Unknown",
                x.Message ?? string.Empty,
                x.SavedPercent is null
                    ? null
                    : x.SavedPercent.Value.ToString("0.0", CultureInfo.CurrentCulture) + "%",
                x.FinalPath))
            .ToArray();
    }

    public int ScannedCount { get; }
    public int CandidateCount { get; }
    public int ReplacedCount { get; }
    public int ProcessedCount { get; }
    public int KeptCount { get; }
    public long BytesSaved { get; }
    public string BytesSavedText { get; }
    public double AverageReductionPercent { get; }
    public string AverageReductionText { get; }
    public IReadOnlyList<SummarySegment> Segments { get; }
    public IReadOnlyList<SummaryFileResult> Files { get; }
}
```

If the integrated `HumanSize` method is named differently, add `Format(long)` to `HumanSize` as a
thin alias to the existing formatter and test that alias in Core. Do not duplicate byte formatting
inside App.

- [ ] **Step 5: Run green**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter SummaryViewModelTests
```

Expected: `Passed!` and `Failed: 0`.

- [ ] **Step 6: Commit**

```powershell
git add src/VideoTriage.App/ViewModels/SummarySegment.cs src/VideoTriage.App/ViewModels/SummaryFileResult.cs src/VideoTriage.App/ViewModels/SummaryViewModel.cs tests/VideoTriage.App.Tests/ViewModels/SummaryViewModelTests.cs
git commit -m "feat(app): project pipeline results into run summary"
```

Expected: commit succeeds.

### Task 2: Render A Package-Free Donut Chart

**Files:**
- Create: `src/VideoTriage.App/Controls/DonutChart.cs`
- Create: `tests/VideoTriage.App.Tests/Controls/DonutChartTests.cs`

- [ ] **Step 1: Write the failing geometry tests**

Create `tests/VideoTriage.App.Tests/Controls/DonutChartTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run red**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter DonutChartTests
```

Expected: build fails with `CS0234` because `DonutChart` and `DonutSlice` do not exist.

- [ ] **Step 3: Add the complete chart control**

Create `src/VideoTriage.App/Controls/DonutChart.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using VideoTriage.App.ViewModels;

namespace VideoTriage.App.Controls;

public sealed record DonutSlice(double StartAngle, double SweepAngle, string Color);

public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IReadOnlyList<SummarySegment>),
            typeof(DonutChart),
            new FrameworkPropertyMetadata(
                Array.Empty<SummarySegment>(),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<SummarySegment> ItemsSource
    {
        get => (IReadOnlyList<SummarySegment>)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static IReadOnlyList<DonutSlice> BuildSlices(
        IReadOnlyList<SummarySegment> segments)
    {
        var positive = segments.Where(x => x.Count > 0).ToArray();
        var total = positive.Sum(x => x.Count);
        if (total == 0)
        {
            return [new DonutSlice(0, 360, "#3A3F4B")];
        }

        var start = 0d;
        var slices = new List<DonutSlice>();
        foreach (var segment in positive)
        {
            var sweep = 360d * segment.Count / total;
            slices.Add(new DonutSlice(start, sweep, segment.Color));
            start += sweep;
        }

        return slices;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 4);
        var thickness = Math.Max(8, radius * 0.28);

        foreach (var slice in BuildSlices(ItemsSource))
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(slice.Color)!;
            drawingContext.DrawGeometry(
                null,
                new Pen(brush, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat },
                Arc(center, radius - thickness / 2, slice.StartAngle, slice.SweepAngle));
        }
    }

    private static Geometry Arc(
        Point center,
        double radius,
        double startAngle,
        double sweepAngle)
    {
        if (sweepAngle >= 359.999)
        {
            return new EllipseGeometry(center, radius, radius);
        }

        Point At(double degrees)
        {
            var radians = (degrees - 90) * Math.PI / 180;
            return new Point(
                center.X + radius * Math.Cos(radians),
                center.Y + radius * Math.Sin(radians));
        }

        var figure = new PathFigure { StartPoint = At(startAngle), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = At(startAngle + sweepAngle),
            Size = new Size(radius, radius),
            IsLargeArc = sweepAngle > 180,
            SweepDirection = SweepDirection.Clockwise
        });
        return new PathGeometry([figure]);
    }
}
```

- [ ] **Step 4: Run green**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter DonutChartTests
```

Expected: `Passed!` and `Failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add src/VideoTriage.App/Controls/DonutChart.cs tests/VideoTriage.App.Tests/Controls/DonutChartTests.cs
git commit -m "feat(app): render summary donut without chart package"
```

Expected: commit succeeds.

### Task 3: Build The Summary View

**Files:**
- Create: `src/VideoTriage.App/Views/SummaryView.xaml`
- Create: `src/VideoTriage.App/Views/SummaryView.xaml.cs`
- Modify: `src/VideoTriage.App/Services/IDialogService.cs`

- [ ] **Step 1: Extend the dialog service contract**

Add this method to the existing App-owned `IDialogService`:

```csharp
void OpenDirectory(string path);
```

Implement it in the physical dialog service with:

```csharp
System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
{
    FileName = path,
    UseShellExecute = true
});
```

Add or extend the dialog-service unit test to assert the ViewModel calls the abstraction; tests must
not launch Explorer.

- [ ] **Step 2: Add the complete view**

Create `src/VideoTriage.App/Views/SummaryView.xaml`:

```xml
<UserControl x:Class="VideoTriage.App.Views.SummaryView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:VideoTriage.App.Controls">
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock Text="Run complete" FontSize="28" FontWeight="SemiBold" />

        <Grid Grid.Row="1" Margin="0,20,0,20">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="220" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <controls:DonutChart Width="180"
                                 Height="180"
                                 ItemsSource="{Binding Segments}" />

            <UniformGrid Grid.Column="1" Columns="2" Margin="28,0,0,0">
                <StackPanel Margin="8">
                    <TextBlock Text="Space saved" Opacity="0.65" />
                    <TextBlock Text="{Binding BytesSavedText}" FontSize="24" />
                </StackPanel>
                <StackPanel Margin="8">
                    <TextBlock Text="Processed" Opacity="0.65" />
                    <TextBlock Text="{Binding ProcessedCount}" FontSize="24" />
                </StackPanel>
                <StackPanel Margin="8">
                    <TextBlock Text="Kept" Opacity="0.65" />
                    <TextBlock Text="{Binding KeptCount}" FontSize="24" />
                </StackPanel>
                <StackPanel Margin="8">
                    <TextBlock Text="Average reduction" Opacity="0.65" />
                    <TextBlock Text="{Binding AverageReductionText}" FontSize="24" />
                </StackPanel>
            </UniformGrid>
        </Grid>

        <DataGrid Grid.Row="2"
                  ItemsSource="{Binding Files}"
                  AutoGenerateColumns="False"
                  IsReadOnly="True"
                  CanUserAddRows="False">
            <DataGrid.Columns>
                <DataGridTextColumn Header="File" Binding="{Binding FilePath}" Width="2*" />
                <DataGridTextColumn Header="Outcome" Binding="{Binding Outcome}" Width="Auto" />
                <DataGridTextColumn Header="Saved" Binding="{Binding SavedPercent}" Width="Auto" />
                <DataGridTextColumn Header="Message" Binding="{Binding Message}" Width="3*" />
            </DataGrid.Columns>
        </DataGrid>
    </Grid>
</UserControl>
```

Create `src/VideoTriage.App/Views/SummaryView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace VideoTriage.App.Views;

public partial class SummaryView : UserControl
{
    public SummaryView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Build the App**

Run:

```powershell
dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Debug
```

Expected: build reports `0 Error(s)`.

- [ ] **Step 4: Commit**

```powershell
git add src/VideoTriage.App/Views/SummaryView.xaml src/VideoTriage.App/Views/SummaryView.xaml.cs src/VideoTriage.App/Services/IDialogService.cs
git commit -m "feat(app): add post-run summary view"
```

Expected: commit succeeds.

### Task 4: Navigate Only After A Normally Completed Run

**Files:**
- Modify: `src/VideoTriage.App/ViewModels/MainViewModel.cs`
- Create: `tests/VideoTriage.App.Tests/ViewModels/MainViewModelSummaryTests.cs`

- [ ] **Step 1: Write the failing navigation tests**

Create `tests/VideoTriage.App.Tests/ViewModels/MainViewModelSummaryTests.cs` using the run-controls
fixture:

```csharp
[Fact]
public async Task StartAsync_RunReturns_AssignsSummaryAndShowsSummary()
{
    var pipeline = new CompletingPipeline(CreateSummary());
    var navigation = new RecordingNavigation();
    var viewModel = CreateViewModel(pipeline, navigation);
    viewModel.SelectedFolder = @"C:\Videos";

    await viewModel.StartCommand.ExecuteAsync(null);

    viewModel.LastSummary.ShouldNotBeNull();
    viewModel.LastSummary!.ReplacedCount.ShouldBe(1);
    navigation.Destination.ShouldBe("Summary");
}

[Fact]
public async Task StartAsync_Cancelled_DoesNotAssignOrShowSummary()
{
    var pipeline = new CancellingPipeline();
    var navigation = new RecordingNavigation();
    var viewModel = CreateViewModel(pipeline, navigation);
    viewModel.SelectedFolder = @"C:\Videos";

    await viewModel.StartCommand.ExecuteAsync(null);

    viewModel.LastSummary.ShouldBeNull();
    navigation.Destination.ShouldNotBe("Summary");
}
```

- [ ] **Step 2: Run red**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter MainViewModelSummaryTests
```

Expected: tests fail because completed runs do not project/navigate, or cancellation still opens a
summary.

- [ ] **Step 3: Add the successful-run projection**

Add this property to `MainViewModel`:

```csharp
[ObservableProperty]
private SummaryViewModel? lastSummary;
```

Immediately after the awaited pipeline call returns normally, add:

```csharp
LastSummary = new SummaryViewModel(summary);
ShowSummary();
```

Keep that code inside the `try` block and before any catch. The cancellation catch must remain:

```csharp
catch (OperationCanceledException)
{
    StatusMessage = "Run stopped. Original files were left unchanged.";
}
```

It must not assign `LastSummary` or call `ShowSummary`.

- [ ] **Step 4: Add the data-directory command**

Inject `IDialogService` and the configured data-directory path into `MainViewModel`, then add:

```csharp
[RelayCommand]
private void OpenDataDirectory() => _dialogService.OpenDirectory(_dataDirectory);
```

Bind the shell's summary-page button to `OpenDataDirectoryCommand`. This command opens the directory
through the abstraction and does not perform filesystem work in the ViewModel.

- [ ] **Step 5: Run green and the full gate**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter "SummaryViewModelTests|DonutChartTests|MainViewModelSummaryTests"
dotnet build VideoTriage.sln -c Release
dotnet test VideoTriage.sln -c Release --no-build
```

Expected: selected and full test suites report `Failed: 0`; build reports `0 Error(s)`.

- [ ] **Step 6: Commit**

```powershell
git add src/VideoTriage.App/ViewModels/MainViewModel.cs tests/VideoTriage.App.Tests/ViewModels/MainViewModelSummaryTests.cs
git commit -m "feat(app): show summary after completed runs"
```

Expected: commit succeeds.

## Self-Review

### Spec Coverage

- Summary projection is immutable and unit-tested for zero totals, formatting, weighting, segments,
  and terminal file messages.
- Weighted reduction uses source bytes for replaced files, not an unweighted average of percentages.
- The chart has no package dependency and renders a neutral ring for a zero total.
- Cancellation never produces a success summary.
- Opening the data directory goes through `IDialogService`.

### Placeholder And Type Scan

- Every code-producing step contains literal code.
- Every TDD task includes an exact red command/failure and green command/output.
- Segment labels/colors and `TriageSummary`/`FileProgress` property names are consistent throughout.
- No Core model is duplicated in App.

## Execution Handoff

Execute on `feature/post-run-summary` from updated `main` after settings persistence and run controls
are integrated. Use `superpowers:subagent-driven-development`; specification review must confirm
weighted calculations and cancellation behavior before merge.
