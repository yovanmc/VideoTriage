using Shouldly;
using VideoTriage.App.Models;
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

    [Fact]
    public void Constructor_WithoutScanner_DisablesChooseFolderButKeepsPrerequisites()
    {
        var vm = new MainViewModel(
            scanner: null,
            new FakeDialogService { Result = @"C:\videos" },
            new RecordingUiDispatcher(),
            new MissingFfprobePrerequisiteService());

        vm.ChooseFolderCommand.CanExecute(null).ShouldBeFalse();
        vm.Prerequisites.Any(x => !x.IsAvailable).ShouldBeTrue();
    }

    [Fact]
    public async Task ChooseFolderAsync_NonCandidateResult_ExcludedFromItems()
    {
        var scanner = new FakeFolderProbeScanner();
        scanner.Results.Add(Result(@"C:\videos\a.mp4"));                       // Candidate
        scanner.Results.Add(NonCandidateResult(@"C:\videos\b.mp4"));          // Below threshold
        var vm = Create(scanner, new FakeDialogService { Result = @"C:\videos" });

        await vm.ChooseFolderAsync();

        vm.Items.Count.ShouldBe(1);
        vm.Items[0].FileName.ShouldBe("a.mp4");
    }

    [Fact]
    public async Task ChooseFolderAsync_SetsQueueRemainingCountAfterScan()
    {
        var scanner = new FakeFolderProbeScanner();
        scanner.Results.Add(Result(@"C:\videos\a.mp4"));
        scanner.Results.Add(Result(@"C:\videos\b.mp4"));
        var vm = Create(scanner, new FakeDialogService { Result = @"C:\videos" });

        await vm.ChooseFolderAsync();

        vm.QueueRemainingCount.ShouldBe(2);
    }

    [Fact]
    public async Task ChooseFolderAsync_DefaultSettings_PassesRecursiveTrueToScanner()
    {
        var scanner = new FakeFolderProbeScanner();
        var settings = new SettingsViewModel(new StubSettingsStore());
        var vm = new MainViewModel(
            scanner,
            new FakeDialogService { Result = @"C:\videos" },
            new RecordingUiDispatcher(),
            new AvailablePrerequisiteService(),
            settings: settings);

        await vm.ChooseFolderAsync();

        scanner.LastRecursive.ShouldBe(true);
    }

    [Fact]
    public async Task ChooseFolderAsync_RecursiveFalseInSettings_PassesRecursiveFalseToScanner()
    {
        var scanner = new FakeFolderProbeScanner();
        var settings = new SettingsViewModel(new StubSettingsStore(recursive: false));
        var vm = new MainViewModel(
            scanner,
            new FakeDialogService { Result = @"C:\videos" },
            new RecordingUiDispatcher(),
            new AvailablePrerequisiteService(),
            settings: settings);

        await vm.ChooseFolderAsync();

        scanner.LastRecursive.ShouldBe(false);
    }

    [Fact]
    public async Task ChooseFolderAsync_500Candidates_AddsAllToItems()
    {
        var scanner = new FakeFolderProbeScanner();
        for (var i = 0; i < 500; i++)
            scanner.Results.Add(Result($@"C:\videos\file{i:D4}.mp4"));
        var vm = Create(
            scanner,
            new FakeDialogService { Result = @"C:\videos" });

        await vm.ChooseFolderAsync();

        vm.Items.Count.ShouldBe(500);
    }

    [Fact]
    public async Task ChooseFolderAsync_SecondScan_ClearsItemsFromFirstScan()
    {
        var scanner = new FakeFolderProbeScanner();
        for (var i = 0; i < 10; i++)
            scanner.Results.Add(Result($@"C:\videos\file{i}.mp4"));
        var vm = Create(
            scanner,
            new FakeDialogService { Result = @"C:\videos" });

        await vm.ChooseFolderAsync();
        vm.Items.Count.ShouldBe(10);

        scanner.Results.Clear();
        await vm.ChooseFolderAsync();

        vm.Items.ShouldBeEmpty();
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

    private static ProbeResult NonCandidateResult(string path)
    {
        var stats = new VideoStats
        {
            FilePath = path,
            CodecName = "av1",
            Width = 1920,
            Height = 1080,
            FramesPerSecond = 30,
            Duration = TimeSpan.FromMinutes(1),
            FileSizeBytes = 5_000_000,
            VideoBitrateBitsPerSecond = 666_666,
            HasAudio = true
        };
        return new ProbeResult
        {
            FilePath = path,
            Stats = stats,
            Classification = new ClassificationResult
            {
                Outcome = ClassificationOutcome.SkipAlreadyAv1,
                Reason = "already av1",
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

    private sealed class MissingFfprobePrerequisiteService : IPrerequisiteService
    {
        public IReadOnlyList<ToolPrerequisiteStatus> Check() =>
        [
            new("ffprobe", false, null, "winget install Gyan.FFmpeg"),
            new("ffmpeg", true, @"C:\tools\ffmpeg.exe", ""),
            new("HandBrakeCLI", true, @"C:\tools\HandBrakeCLI.exe", "")
        ];
    }

    private sealed class StubSettingsStore(bool recursive = true) : ISettingsStore
    {
        public AppSettings Load() => new() { Recursive = recursive };
        public void Save(AppSettings settings) { }
    }
}
