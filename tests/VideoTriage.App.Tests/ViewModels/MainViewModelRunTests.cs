using Shouldly;
using VideoTriage.App.Models;
using VideoTriage.App.Services;
using VideoTriage.App.Tests.Fakes;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;
using VideoTriage.Core.Pipeline;
using VideoTriage.Core.Probing;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class MainViewModelRunTests
{
    [Fact]
    public void StartCommand_NoFolder_CannotExecute()
    {
        var vm = MakeViewModel(new FakeTriagePipeline([]));

        vm.StartCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void StartCommand_MissingPrerequisites_CannotExecute()
    {
        var vm = MakeViewModel(pipeline: null);
        vm.SelectedFolder = @"C:\Videos";

        vm.StartCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task StartCommand_Progress_UpdatesExistingRowsOnDispatcher()
    {
        var pipeline = new FakeTriagePipeline([
            new FileProgress
            {
                FilePath = @"C:\Videos\clip.mp4",
                Phase = TriagePhase.Encoding,
                EncodeProgress = 0.25
            }
        ]);
        var dispatcher = new RecordingUiDispatcher();
        var vm = MakeViewModel(pipeline, dispatcher);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));

        await vm.StartCommand.ExecuteAsync(null);

        vm.Items[0].StatusText.ShouldBe("Encoding 25%");
        dispatcher.PostCount.ShouldBe(1);
        vm.RunState.ShouldBe(RunState.Idle);
    }

    [Fact]
    public async Task PauseAndResume_UpdatePauseTokenAndState()
    {
        var pipeline = new BlockingTriagePipeline();
        var vm = MakeViewModel(pipeline);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));

        var run = vm.StartCommand.ExecuteAsync(null);
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        vm.ChooseFolderCommand.CanExecute(null).ShouldBeFalse();
        vm.PauseCommand.Execute(null);
        vm.RunState.ShouldBe(RunState.Paused);
        pipeline.PauseToken!.IsPaused.ShouldBeTrue();

        vm.ResumeCommand.Execute(null);
        vm.RunState.ShouldBe(RunState.Running);
        pipeline.PauseToken!.IsPaused.ShouldBeFalse();

        vm.StopCommand.Execute(null);
        await run;
        vm.RunState.ShouldBe(RunState.Idle);
        vm.ChooseFolderCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task StartCommand_UsesCurrentValidatedSettings()
    {
        var pipeline = new CapturingTriagePipeline();
        var settings = new SettingsViewModel(new StubSettingsStore())
        {
            CandidateBppThreshold = 0.24,
            MinimumFreeGigabytes = 8,
            DryRun = true
        };
        var vm = MakeViewModel(pipeline, settings: settings);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));

        await vm.StartCommand.ExecuteAsync(null);

        pipeline.Options.ShouldNotBeNull();
        pipeline.Options.CandidateBppThreshold.ShouldBe(0.24);
        pipeline.Options.MinimumFreeGigabytes.ShouldBe(8);
        pipeline.Options.DryRun.ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_PassesRecursiveFalse_ToPipeline()
    {
        var pipeline = new CapturingTriagePipeline();
        var stubStore = new StubSettingsStore(new AppSettings { Recursive = false });
        var settings = new SettingsViewModel(stubStore);
        var vm = MakeViewModel(pipeline, settings: settings);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));

        await vm.StartCommand.ExecuteAsync(null);

        pipeline.CapturedRecursive.ShouldBe(false);
    }

    [Fact]
    public void StartCommand_EmptyItems_CannotExecute()
    {
        var vm = MakeViewModel(new FakeTriagePipeline([]));
        vm.SelectedFolder = @"C:\Videos";
        // Items is empty — no files were found by the scan

        vm.StartCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void StartCommand_UnconfirmedPermanentDelete_CannotExecute()
    {
        var settings = new SettingsViewModel(new StubSettingsStore())
        {
            DeleteMode = DeleteMode.Permanent
        };
        var vm = MakeViewModel(new FakeTriagePipeline([]), settings: settings);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));

        vm.StartCommand.CanExecute(null).ShouldBeFalse();

        settings.ConfirmPermanentDelete = true;
        vm.StartCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyProgress_FirstEncodingEvent_MovesItemToTop()
    {
        var pipeline = new FakeTriagePipeline([
            new FileProgress
            {
                FilePath = @"C:\Videos\b.mp4",
                Phase = TriagePhase.Encoding,
                EncodeProgress = null
            }
        ]);
        var vm = MakeViewModel(pipeline);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\a.mp4"));
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\b.mp4"));
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\c.mp4"));

        await vm.StartCommand.ExecuteAsync(null);

        vm.Items[0].FileName.ShouldBe("b.mp4");
    }

    [Fact]
    public async Task ApplyProgress_DoneEvent_MovesItemToBottom()
    {
        var pipeline = new FakeTriagePipeline([
            new FileProgress
            {
                FilePath = @"C:\Videos\a.mp4",
                Phase = TriagePhase.Done,
                Outcome = TriageOutcome.Replaced,
                FinalPath = @"C:\Videos\a.mp4"
            }
        ]);
        var vm = MakeViewModel(pipeline);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\a.mp4"));
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\b.mp4"));
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\c.mp4"));

        await vm.StartCommand.ExecuteAsync(null);

        vm.Items[2].FileName.ShouldBe("a.mp4");
    }

    [Fact]
    public async Task ApplyProgress_DoneEvent_DecrementsQueueRemainingCount()
    {
        var pipeline = new FakeTriagePipeline([
            new FileProgress
            {
                FilePath = @"C:\Videos\a.mp4",
                Phase = TriagePhase.Done,
                Outcome = TriageOutcome.Replaced,
                FinalPath = @"C:\Videos\a.mp4"
            }
        ]);
        var vm = MakeViewModel(pipeline);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\a.mp4"));
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\b.mp4"));

        await vm.StartCommand.ExecuteAsync(null);

        vm.QueueRemainingCount.ShouldBe(1);
    }

    [Fact]
    public async Task StartCommand_SetsQueueRemainingCountToItemsCount()
    {
        var pipeline = new FakeTriagePipeline([]);
        var vm = MakeViewModel(pipeline);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\a.mp4"));
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\b.mp4"));
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\c.mp4"));

        await vm.StartCommand.ExecuteAsync(null);

        // No Done events fired, so count stays at Items.Count (set at start of run)
        vm.QueueRemainingCount.ShouldBe(3);
    }

    private static MainViewModel MakeViewModel(
        ITriagePipeline? pipeline,
        IUiDispatcher? dispatcher = null,
        SettingsViewModel? settings = null) =>
        new(
            scanner: new NoopFolderProbeScanner(),
            new FakeDialogService(),
            dispatcher ?? new RecordingUiDispatcher(),
            new AvailablePrerequisiteService(),
            new StubPipelineProvider(pipeline),
            settings is null ? () => new TriageOptions() : null,
            settings);

    private sealed class StubPipelineProvider(ITriagePipeline? pipeline) : ITriagePipelineProvider
    {
        public ITriagePipeline? Pipeline { get; } = pipeline;
    }

    private sealed class NoopFolderProbeScanner : IFolderProbeScanner
    {
        public Task<FolderScanSummary> ScanAsync(
            string folderPath,
            TriageOptions? options = null,
            bool recursive = false,
            IProgress<ProbeResult>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FolderScanSummary
            {
                FilesDiscovered = 0,
                CandidateCount = 0,
                ProbeFailureCount = 0,
            });
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

    private sealed class StubSettingsStore(AppSettings? seed = null) : ISettingsStore
    {
        public AppSettings Load() => seed ?? new AppSettings();
        public void Save(AppSettings settings) { }
    }

    private sealed class CapturingTriagePipeline : ITriagePipeline
    {
        public TriageOptions? Options { get; private set; }
        public bool? CapturedRecursive { get; private set; }

        public Task<TriageSummary> RunAsync(
            string folder,
            TriageOptions options,
            bool recursive = false,
            IProgress<FileProgress>? progress = null,
            PauseToken? pauseToken = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            CapturedRecursive = recursive;
            return Task.FromResult(FakeTriagePipeline.EmptySummary());
        }
    }
}
