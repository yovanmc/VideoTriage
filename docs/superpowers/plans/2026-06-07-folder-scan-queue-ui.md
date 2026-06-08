# Folder Scan And Queue UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user choose a folder, observe read-only probe/classification progress, and see one queue row per discovered video before any encoding begins.

**Architecture:** The existing `FolderProbeScanner` implements a narrow `IFolderProbeScanner` interface so `MainViewModel` can be tested without files or tools. `MainViewModel` owns the queue and sends every collection mutation through `IUiDispatcher`; `FileItemViewModel` projects immutable Core results into display text. The toolbar reserves space for run controls, but this plan performs no pipeline execution or filesystem mutation.

**Tech Stack:** .NET 10, WPF, WPF-UI, CommunityToolkit.Mvvm, `Microsoft.Win32.OpenFolderDialog`, xUnit, Shouldly.

---

## Scope Check

This plan is independently testable and non-destructive. It depends on the prerequisite/composition
plan for `IPrerequisiteService` and `ToolPrerequisiteStatus`. Start, pause, resume, stop, and
`ITriagePipeline` are added only by `2026-06-07-run-controls.md`.

## File Structure

```text
src/VideoTriage.Core/Probing/
  IFolderProbeScanner.cs              # Testable contract for the existing read-only scanner.
  FolderProbeScanner.cs               # Implements the contract without behavior changes.
src/VideoTriage.App/Services/
  IDialogService.cs                   # Folder-picker boundary.
  DialogService.cs                    # OpenFolderDialog adapter.
  IUiDispatcher.cs                    # UI-thread boundary.
  UiDispatcher.cs                     # WPF Dispatcher adapter.
src/VideoTriage.App/ViewModels/
  FileItemViewModel.cs                # One queue row.
  MainViewModel.cs                    # Folder selection, prerequisites, scan lifetime, queue.
src/VideoTriage.App/Views/
  MainWindow.xaml                     # Mockup-derived shell and queue.
  MainWindow.xaml.cs                  # Constructor injection only.
tests/VideoTriage.App.Tests/Fakes/
  FakeDialogService.cs
  FakeFolderProbeScanner.cs
  RecordingUiDispatcher.cs
tests/VideoTriage.App.Tests/Services/
  UiServiceTests.cs
tests/VideoTriage.App.Tests/ViewModels/
  FileItemViewModelTests.cs
  MainViewModelScanTests.cs
tests/VideoTriage.App.Tests/Views/
  MainWindowMarkupTests.cs
```

### Task 1: Add Folder, Scanner, And Dispatcher Seams

**Files:**
- Create: `src/VideoTriage.Core/Probing/IFolderProbeScanner.cs`
- Modify: `src/VideoTriage.Core/Probing/FolderProbeScanner.cs`
- Create: `src/VideoTriage.App/Services/IDialogService.cs`
- Create: `src/VideoTriage.App/Services/DialogService.cs`
- Create: `src/VideoTriage.App/Services/IUiDispatcher.cs`
- Create: `src/VideoTriage.App/Services/UiDispatcher.cs`
- Create: `tests/VideoTriage.App.Tests/Fakes/FakeDialogService.cs`
- Create: `tests/VideoTriage.App.Tests/Fakes/RecordingUiDispatcher.cs`
- Test: `tests/VideoTriage.App.Tests/Services/UiServiceTests.cs`

- [ ] **Step 1: Write the failing service tests**

```csharp
using Shouldly;
using VideoTriage.App.Tests.Fakes;

namespace VideoTriage.App.Tests.Services;

public sealed class UiServiceTests
{
    [Fact]
    public void FakeDialogService_ReturnsConfiguredFolderAndRecordsInitialFolder()
    {
        var dialog = new FakeDialogService { Result = @"C:\videos" };

        dialog.ChooseFolder(@"C:\start").ShouldBe(@"C:\videos");

        dialog.LastInitialFolder.ShouldBe(@"C:\start");
    }

    [Fact]
    public void RecordingUiDispatcher_Post_ExecutesActionAndCountsCall()
    {
        var dispatcher = new RecordingUiDispatcher();
        var executed = false;

        dispatcher.Post(() => executed = true);

        executed.ShouldBeTrue();
        dispatcher.PostCount.ShouldBe(1);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/VideoTriage.App.Tests -c Debug --filter UiServiceTests`

Expected: build fails with `CS0234` because `VideoTriage.App.Tests.Fakes` and the service
interfaces do not exist.

- [ ] **Step 3: Add the scanner interface and implement it on the existing scanner**

```csharp
// src/VideoTriage.Core/Probing/IFolderProbeScanner.cs
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public interface IFolderProbeScanner
{
    Task<IReadOnlyList<ProbeResult>> ScanAsync(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false,
        IProgress<ProbeResult>? progress = null,
        CancellationToken cancellationToken = default);
}
```

Change only the declaration in `FolderProbeScanner.cs`; retain its constructor and method body:

```csharp
public sealed class FolderProbeScanner : IFolderProbeScanner
```

- [ ] **Step 4: Add the application interfaces and physical adapters**

```csharp
// src/VideoTriage.App/Services/IDialogService.cs
namespace VideoTriage.App.Services;

public interface IDialogService
{
    string? ChooseFolder(string? initialFolder);
}
```

```csharp
// src/VideoTriage.App/Services/DialogService.cs
using Microsoft.Win32;

namespace VideoTriage.App.Services;

public sealed class DialogService : IDialogService
{
    public string? ChooseFolder(string? initialFolder)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder containing videos",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
```

```csharp
// src/VideoTriage.App/Services/IUiDispatcher.cs
namespace VideoTriage.App.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}
```

```csharp
// src/VideoTriage.App/Services/UiDispatcher.cs
using System.Windows.Threading;

namespace VideoTriage.App.Services;

public sealed class UiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        dispatcher.BeginInvoke(action);
    }
}
```

- [ ] **Step 5: Add the reusable test fakes**

```csharp
// tests/VideoTriage.App.Tests/Fakes/FakeDialogService.cs
using VideoTriage.App.Services;

namespace VideoTriage.App.Tests.Fakes;

public sealed class FakeDialogService : IDialogService
{
    public string? Result { get; set; }
    public string? LastInitialFolder { get; private set; }

    public string? ChooseFolder(string? initialFolder)
    {
        LastInitialFolder = initialFolder;
        return Result;
    }
}
```

```csharp
// tests/VideoTriage.App.Tests/Fakes/RecordingUiDispatcher.cs
using VideoTriage.App.Services;

namespace VideoTriage.App.Tests.Fakes;

public sealed class RecordingUiDispatcher : IUiDispatcher
{
    public int PostCount { get; private set; }

    public void Post(Action action)
    {
        PostCount++;
        action();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/VideoTriage.App.Tests -c Debug --filter UiServiceTests`

Expected:

```text
Passed!  - Failed: 0, Passed: 2, Skipped: 0
```

- [ ] **Step 7: Commit**

```powershell
git add src/VideoTriage.Core/Probing/IFolderProbeScanner.cs src/VideoTriage.Core/Probing/FolderProbeScanner.cs src/VideoTriage.App/Services tests/VideoTriage.App.Tests/Fakes tests/VideoTriage.App.Tests/Services/UiServiceTests.cs
git commit -m "feat(app): add folder scan UI seams"
```

### Task 2: Project Probe Results Into Queue Rows

**Files:**
- Create: `src/VideoTriage.App/ViewModels/FileItemViewModel.cs`
- Test: `tests/VideoTriage.App.Tests/ViewModels/FileItemViewModelTests.cs`

- [ ] **Step 1: Write the failing queue-row tests**

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/VideoTriage.App.Tests -c Debug --filter FileItemViewModelTests`

Expected: build fails with `CS0246: The type or namespace name 'FileItemViewModel' could not be found`.

- [ ] **Step 3: Add the minimal queue-row implementation**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using VideoTriage.Core.Formatting;
using VideoTriage.Core.Models;

namespace VideoTriage.App.ViewModels;

public sealed class FileItemViewModel : ObservableObject
{
    private string _metaLine = "";
    private string _statusText = "Queued";
    private double _progress;
    private string _savedText = "";
    private string? _finalPath;

    public FileItemViewModel(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        FileName = Path.GetFileName(filePath);
    }

    public string FilePath { get; }
    public string FileName { get; }

    public string MetaLine
    {
        get => _metaLine;
        private set => SetProperty(ref _metaLine, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string SavedText
    {
        get => _savedText;
        private set => SetProperty(ref _savedText, value);
    }

    public string? FinalPath
    {
        get => _finalPath;
        private set => SetProperty(ref _finalPath, value);
    }

    public void ApplyProbe(ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFullPath(result.FilePath),
                FilePath))
        {
            throw new ArgumentException("Probe result belongs to another file.", nameof(result));
        }

        if (!result.Succeeded || result.Stats is null)
        {
            MetaLine = "";
            StatusText = $"Probe failed: {result.Failure?.Message ?? "unknown error"}";
            return;
        }

        var stats = result.Stats;
        MetaLine = $"{stats.Width}x{stats.Height} | {stats.FramesPerSecond:0.##} fps | " +
                   $"{HumanSize.Format(stats.FileSizeBytes)} | bpp {stats.BitsPerPixel:0.###}";
        StatusText = result.Classification?.Outcome switch
        {
            ClassificationOutcome.Candidate => "Candidate",
            ClassificationOutcome.SkipAlreadyAv1 => "Already AV1",
            ClassificationOutcome.SkipLowBpp => "Below threshold",
            _ => "Invalid metadata"
        };
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/VideoTriage.App.Tests -c Debug --filter FileItemViewModelTests`

Expected:

```text
Passed!  - Failed: 0, Passed: 5, Skipped: 0
```

- [ ] **Step 5: Commit**

```powershell
git add src/VideoTriage.App/ViewModels/FileItemViewModel.cs tests/VideoTriage.App.Tests/ViewModels/FileItemViewModelTests.cs
git commit -m "feat(app): project probe results into queue rows"
```

### Task 3: Scan The Selected Folder Into A Stable Queue

**Files:**
- Create: `tests/VideoTriage.App.Tests/Fakes/FakeFolderProbeScanner.cs`
- Create: `src/VideoTriage.App/ViewModels/MainViewModel.cs`
- Test: `tests/VideoTriage.App.Tests/ViewModels/MainViewModelScanTests.cs`

- [ ] **Step 1: Add the fake scanner used by the exact tests**

```csharp
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;

namespace VideoTriage.App.Tests.Fakes;

public sealed class FakeFolderProbeScanner : IFolderProbeScanner
{
    public List<ProbeResult> Results { get; } = [];
    public string? LastFolder { get; private set; }
    public TaskCompletionSource? BlockUntil { get; set; }

    public async Task<IReadOnlyList<ProbeResult>> ScanAsync(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false,
        IProgress<ProbeResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LastFolder = folderPath;
        foreach (var result in Results)
        {
            progress?.Report(result);
        }

        if (BlockUntil is not null)
        {
            await BlockUntil.Task.WaitAsync(cancellationToken);
        }

        return Results;
    }
}
```

- [ ] **Step 2: Write the failing scan tests**

```csharp
using Shouldly;
using VideoTriage.App.Services;
using VideoTriage.App.Tests.Fakes;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class MainViewModelScanTests
{
    [Fact]
    public async Task ChooseFolderAsync_CancelledDialog_DoesNotScanOrClearQueue()
    {
        var scanner = new FakeFolderProbeScanner();
        var dialog = new FakeDialogService { Result = null };
        var vm = Create(scanner, dialog);
        vm.Items.Add(new FileItemViewModel(@"C:\old.mp4"));

        await vm.ChooseFolderAsync();

        scanner.LastFolder.ShouldBeNull();
        vm.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ChooseFolderAsync_SelectedFolder_ClearsAndAddsOneRowPerProgress()
    {
        var scanner = new FakeFolderProbeScanner();
        scanner.Results.Add(Result(@"C:\videos\a.mp4"));
        scanner.Results.Add(Result(@"C:\videos\b.mp4"));
        var dispatcher = new RecordingUiDispatcher();
        var vm = Create(
            scanner,
            new FakeDialogService { Result = @"C:\videos" },
            dispatcher);
        vm.Items.Add(new FileItemViewModel(@"C:\old.mp4"));

        await vm.ChooseFolderAsync();

        vm.SelectedFolder.ShouldBe(@"C:\videos");
        vm.Items.Select(item => item.FileName).ShouldBe(["a.mp4", "b.mp4"]);
        dispatcher.PostCount.ShouldBe(3);
        vm.IsScanning.ShouldBeFalse();
    }

    [Fact]
    public async Task ChooseFolderAsync_WhileScanning_DisablesChooseFolder()
    {
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeFolderProbeScanner { BlockUntil = release };
        var vm = Create(scanner, new FakeDialogService { Result = @"C:\videos" });

        var scan = vm.ChooseFolderAsync();

        vm.IsScanning.ShouldBeTrue();
        vm.ChooseFolderCommand.CanExecute(null).ShouldBeFalse();
        release.SetResult();
        await scan;
        vm.ChooseFolderCommand.CanExecute(null).ShouldBeTrue();
    }

    private static MainViewModel Create(
        FakeFolderProbeScanner scanner,
        FakeDialogService dialog,
        RecordingUiDispatcher? dispatcher = null) =>
        new(
            scanner,
            dialog,
            dispatcher ?? new RecordingUiDispatcher(),
            new AvailablePrerequisiteService());

    private static ProbeResult Result(string path)
    {
        var stats = new VideoStats
        {
            FilePath = path,
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
            FilePath = path,
            Stats = stats,
            Classification = new ClassificationResult
            {
                Outcome = ClassificationOutcome.Candidate,
                Reason = "candidate",
                Stats = stats
            }
        };
    }

    private sealed class AvailablePrerequisiteService : IPrerequisiteService
    {
        public IReadOnlyList<ToolPrerequisiteStatus> Check() =>
        [
            new("ffprobe", true, @"C:\tools\ffprobe.exe", ""),
            new("ffmpeg", true, @"C:\tools\ffmpeg.exe", ""),
            new("HandBrakeCLI", true, @"C:\tools\HandBrakeCLI.exe", "")
        ];
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/VideoTriage.App.Tests -c Debug --filter MainViewModelScanTests`

Expected: build fails with `CS0246: The type or namespace name 'MainViewModel' could not be found`.

- [ ] **Step 4: Add the minimal scan ViewModel**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoTriage.App.Services;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;

namespace VideoTriage.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IFolderProbeScanner _scanner;
    private readonly IDialogService _dialogService;
    private readonly IUiDispatcher _dispatcher;
    private string? _selectedFolder;
    private bool _isScanning;

    public MainViewModel(
        IFolderProbeScanner scanner,
        IDialogService dialogService,
        IUiDispatcher dispatcher,
        IPrerequisiteService prerequisiteService)
    {
        _scanner = scanner;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
        Prerequisites = prerequisiteService.Check();
        ChooseFolderCommand = new AsyncRelayCommand(
            ChooseFolderAsync,
            () => !IsScanning);
    }

    public ObservableCollection<FileItemViewModel> Items { get; } = [];
    public IReadOnlyList<ToolPrerequisiteStatus> Prerequisites { get; }
    public IAsyncRelayCommand ChooseFolderCommand { get; }

    public string? SelectedFolder
    {
        get => _selectedFolder;
        private set => SetProperty(ref _selectedFolder, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                ChooseFolderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public async Task ChooseFolderAsync()
    {
        if (IsScanning)
        {
            return;
        }

        var folder = _dialogService.ChooseFolder(SelectedFolder);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        SelectedFolder = folder;
        IsScanning = true;
        _dispatcher.Post(Items.Clear);

        try
        {
            var progress = new Progress<ProbeResult>(result =>
                _dispatcher.Post(() =>
                {
                    var row = new FileItemViewModel(result.FilePath);
                    row.ApplyProbe(result);
                    Items.Add(row);
                }));

            await _scanner.ScanAsync(
                folder,
                progress: progress,
                cancellationToken: CancellationToken.None);
        }
        finally
        {
            IsScanning = false;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/VideoTriage.App.Tests -c Debug --filter MainViewModelScanTests`

Expected:

```text
Passed!  - Failed: 0, Passed: 3, Skipped: 0
```

- [ ] **Step 6: Commit**

```powershell
git add src/VideoTriage.App/ViewModels/MainViewModel.cs tests/VideoTriage.App.Tests/Fakes/FakeFolderProbeScanner.cs tests/VideoTriage.App.Tests/ViewModels/MainViewModelScanTests.cs
git commit -m "feat(app): scan selected folders into live queue"
```

### Task 4: Build The Mockup-Derived Main Window

**Files:**
- Modify: `src/VideoTriage.App/Views/MainWindow.xaml`
- Modify: `src/VideoTriage.App/Views/MainWindow.xaml.cs`
- Test: `tests/VideoTriage.App.Tests/Views/MainWindowMarkupTests.cs`

- [ ] **Step 1: Write the failing markup contract test**

```csharp
using Shouldly;

namespace VideoTriage.App.Tests.Views;

public sealed class MainWindowMarkupTests
{
    [Fact]
    public void MainWindowMarkup_BindsFolderQueuePrerequisitesAndReservedToolbar()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VideoTriage.App", "Views", "MainWindow.xaml"));
        var xaml = File.ReadAllText(path);

        xaml.ShouldContain("Command=\"{Binding ChooseFolderCommand}\"");
        xaml.ShouldContain("ItemsSource=\"{Binding Items}\"");
        xaml.ShouldContain("ItemsSource=\"{Binding Prerequisites}\"");
        xaml.ShouldContain("x:Name=\"StartButton\"");
        xaml.ShouldContain("x:Name=\"PauseButton\"");
        xaml.ShouldContain("x:Name=\"StopButton\"");
        xaml.ShouldNotContain("sample.mp4");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/VideoTriage.App.Tests -c Debug --filter MainWindowMarkupTests`

Expected: `MainWindowMarkup_BindsFolderQueuePrerequisitesAndReservedToolbar` fails because the shell
does not contain the queue and prerequisite bindings.

- [ ] **Step 3: Replace `MainWindow.xaml` with the complete minimal layout**

Use `C:\Agent Projects\VideoTriage design\videotriage-mockup.html` only as a visual reference. Do
not copy hard-coded sample rows.

```xml
<ui:FluentWindow x:Class="VideoTriage.App.Views.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    Title="VideoTriage"
    Width="1180"
    Height="760"
    MinWidth="900"
    MinHeight="600"
    ExtendsContentIntoTitleBar="True"
    WindowBackdropType="Mica"
    WindowStartupLocation="CenterScreen">
    <ui:FluentWindow.Resources>
        <SolidColorBrush x:Key="AccentBrush" Color="#5CC8FF" />
        <SolidColorBrush x:Key="SuccessBrush" Color="#5AD17F" />
        <SolidColorBrush x:Key="WarningBrush" Color="#E8C35A" />
        <SolidColorBrush x:Key="DangerBrush" Color="#FF6B6B" />
    </ui:FluentWindow.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="44" />
            <RowDefinition Height="*" />
            <RowDefinition Height="58" />
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" Title="VideoTriage" />

        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="288" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <Border Grid.Column="0" Padding="16" BorderBrush="#22000000"
                    BorderThickness="0,0,1,0">
                <StackPanel>
                    <TextBlock Text="Source folder" FontWeight="SemiBold" />
                    <TextBlock Margin="0,6,0,10" Text="{Binding SelectedFolder}"
                               TextWrapping="Wrap" Opacity="0.7" />
                    <Button Content="Choose folder"
                            Command="{Binding ChooseFolderCommand}" />

                    <TextBlock Margin="0,24,0,8" Text="Preset"
                               FontWeight="SemiBold" />
                    <TextBlock Text="VideoTriage AV1" />
                    <TextBlock Text="Read-only scan before encoding"
                               Opacity="0.7" TextWrapping="Wrap" />

                    <TextBlock Margin="0,24,0,8" Text="Prerequisites"
                               FontWeight="SemiBold" />
                    <ItemsControl ItemsSource="{Binding Prerequisites}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,3">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="{Binding Name}" />
                                    <TextBlock Grid.Column="1"
                                               Text="{Binding IsAvailable}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>

            <Grid Grid.Column="1">
                <Grid.RowDefinitions>
                    <RowDefinition Height="64" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <Border Grid.Row="0" Padding="16,10" BorderBrush="#22000000"
                        BorderThickness="0,0,0,1">
                    <StackPanel Orientation="Horizontal">
                        <Button x:Name="StartButton" Content="Start"
                                Width="92" IsEnabled="False" />
                        <Button x:Name="PauseButton" Content="Pause"
                                Width="92" Margin="8,0,0,0" IsEnabled="False" />
                        <Button x:Name="StopButton" Content="Stop"
                                Width="92" Margin="8,0,0,0" IsEnabled="False" />
                        <TextBlock Margin="16,0,0,0" VerticalAlignment="Center"
                                   Text="{Binding IsScanning, StringFormat=Scanning: {0}}" />
                    </StackPanel>
                </Border>

                <ListBox Grid.Row="1" Margin="16" ItemsSource="{Binding Items}"
                         BorderThickness="0" Background="Transparent">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Border Margin="0,0,0,10" Padding="12"
                                    CornerRadius="8" Background="#0F7F7F7F">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="96" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="180" />
                                    </Grid.ColumnDefinitions>
                                    <Border Width="96" Height="56" CornerRadius="4"
                                            Background="#247F7F7F" />
                                    <StackPanel Grid.Column="1" Margin="14,0">
                                        <TextBlock Text="{Binding FileName}"
                                                   FontWeight="SemiBold" />
                                        <TextBlock Text="{Binding MetaLine}"
                                                   Opacity="0.7" />
                                        <ProgressBar Height="4" Margin="0,8,0,0"
                                                     Minimum="0" Maximum="100"
                                                     Value="{Binding Progress}" />
                                    </StackPanel>
                                    <StackPanel Grid.Column="2">
                                        <TextBlock Text="{Binding StatusText}"
                                                   HorizontalAlignment="Right" />
                                        <TextBlock Text="{Binding SavedText}"
                                                   HorizontalAlignment="Right"
                                                   Foreground="{StaticResource SuccessBrush}" />
                                    </StackPanel>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </Grid>
        </Grid>

        <Border Grid.Row="2" Padding="18,0" BorderBrush="#22000000"
                BorderThickness="0,1,0,0">
            <TextBlock VerticalAlignment="Center"
                       Text="{Binding Items.Count, StringFormat=Queue: {0} files}" />
        </Border>
    </Grid>
</ui:FluentWindow>
```

- [ ] **Step 4: Replace the code-behind with constructor injection only**

```csharp
using VideoTriage.App.ViewModels;
using Wpf.Ui.Controls;

namespace VideoTriage.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
```

- [ ] **Step 5: Run the markup test and Release build**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter MainWindowMarkupTests
dotnet build VideoTriage.sln -c Release
```

Expected:

```text
Passed!  - Failed: 0, Passed: 1, Skipped: 0
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

- [ ] **Step 6: Commit**

```powershell
git add src/VideoTriage.App/Views/MainWindow.xaml src/VideoTriage.App/Views/MainWindow.xaml.cs tests/VideoTriage.App.Tests/Views/MainWindowMarkupTests.cs
git commit -m "feat(app): build live folder scan queue"
```

## Self-Review

- Spec coverage: folder selection, cancelled selection, live progress, queue projection,
  prerequisites, mockup structure, and UI dispatch are each implemented and tested.
- Placeholder scan: no task says “test X,” “implement X,” “similar to,” `TODO`, or `TBD`.
- Type consistency: `IFolderProbeScanner.ScanAsync`, `FileItemViewModel.FilePath`, and
  `MainViewModel.Items` are the exact contracts consumed by the run-controls plan.
- Safety: this plan calls only discovery, ffprobe, and classification; it does not resolve or invoke
  `ITriagePipeline`.

## Execution Handoff

Plan complete and saved to
`docs/superpowers/plans/2026-06-07-folder-scan-queue-ui.md`. Execute on
`feature/folder-scan-queue-ui` after prerequisite detection and real application composition are
integrated. Use superpowers:subagent-driven-development (recommended) or
superpowers:executing-plans.
