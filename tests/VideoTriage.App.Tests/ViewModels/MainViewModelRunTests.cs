using System.Windows.Media;
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
    public async Task StartAsync_PassesQueueFilePaths_ToPipeline()
    {
        var pipeline = new CapturingTriagePipeline();
        var vm = MakeViewModel(pipeline);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));

        await vm.StartCommand.ExecuteAsync(null);

        pipeline.CapturedFilePaths.ShouldBe([@"C:\Videos\clip.mp4"]);
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

    [Fact]
    public void StartBlockedReason_NoFolder_ExplainsWhy()
    {
        var vm = MakeViewModel(new FakeTriagePipeline([]));
        vm.StartBlockedReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void StartBlockedReason_Ready_IsNull()
    {
        var vm = MakeViewModel(new FakeTriagePipeline([]));
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));
        vm.StartBlockedReason.ShouldBeNull();
    }

    [Fact]
    public void QueueSummaryText_ShowsCount()
    {
        var vm = MakeViewModel(new FakeTriagePipeline([]));
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));
        vm.QueueSummaryText.ShouldContain("1 candidate");
    }

    [Fact]
    public void DismissInterruptedNotice_WhenNull_DoesNotThrow()
    {
        var vm = MakeViewModel(new FakeTriagePipeline([]));
        vm.InterruptedRunNotice.ShouldBeNull();
        vm.DismissInterruptedNoticeCommand.Execute(null);
        vm.InterruptedRunNotice.ShouldBeNull();
    }

    [Fact]
    public async Task BackToQueue_RescansFolder()
    {
        var scanner = new RecordingScanner();
        var vm = MakeViewModel(new FakeTriagePipeline([]), scanner: scanner);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));
        await vm.StartCommand.ExecuteAsync(null); // sets LastSummary
        var before = scanner.ScanCount;
        vm.BackToQueueCommand.Execute(null);
        await Task.Delay(100);
        scanner.ScanCount.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task RunProgressText_ShowsCompletedOfTotal()
    {
        var vm = MakeViewModel(new FakeTriagePipeline(
        [
            new FileProgress { FilePath = @"C:\Videos\clip.mp4", Phase = TriagePhase.Done, Outcome = TriageOutcome.Replaced },
        ]));
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip2.mp4"));
        await vm.StartCommand.ExecuteAsync(null);
        vm.RunProgressText.ShouldContain("of 2");
    }

    [Fact]
    public async Task Summary_KeepsThumbnailForEveryProcessedFile_AfterReorderMoves()
    {
        // Regression: the queue reorders rows during a run (float-to-top / sink-to-bottom via
        // Items.Move). A Move raises CollectionChanged with the row in BOTH OldItems and NewItems;
        // if the index handler adds-then-removes, moved rows fall out of the queue index and the
        // summary loses their thumbnails — only the never-moved row keeps one.
        var paths = new[] { @"C:\v\a.mp4", @"C:\v\b.mp4", @"C:\v\c.mp4" };
        var vm = MakeViewModel(new ReplacingPipeline(paths));
        vm.SelectedFolder = @"C:\v";
        foreach (var p in paths)
            vm.Items.Add(new FileItemViewModel(p) { Thumbnail = FrozenThumb() });

        await vm.StartCommand.ExecuteAsync(null);

        vm.LastSummary.ShouldNotBeNull();
        vm.LastSummary!.Files.Count.ShouldBe(3);
        vm.LastSummary.Files.ShouldAllBe(f => f.Thumbnail != null);
    }

    private static ImageSource FrozenThumb()
    {
        var img = new DrawingImage();
        img.Freeze();
        return img;
    }

    private sealed class ReplacingPipeline(IReadOnlyList<string> paths) : ITriagePipeline
    {
        public Task<TriageSummary> RunAsync(
            string folder,
            IReadOnlyList<string> filePaths,
            TriageOptions options,
            IProgress<FileProgress>? progress = null,
            PauseToken? pauseToken = null,
            CancellationToken cancellationToken = default)
        {
            var files = new List<FileProgress>();
            foreach (var p in paths)
            {
                var stats = new VideoStats
                {
                    FilePath = p, CodecName = "h264", Width = 1920, Height = 1080,
                    FramesPerSecond = 30, Duration = TimeSpan.FromSeconds(5),
                    FileSizeBytes = 1000, HasAudio = true
                };
                // Encoding (null progress) floats the row; Done sinks it — both raise Move.
                progress?.Report(new FileProgress { FilePath = p, Phase = TriagePhase.Encoding });
                var done = new FileProgress
                {
                    FilePath = p, Phase = TriagePhase.Done, Outcome = TriageOutcome.Replaced,
                    Source = stats, OutputBytes = 500, SavedPercent = 50, FinalPath = p
                };
                progress?.Report(done);
                files.Add(done);
            }

            return Task.FromResult(new TriageSummary
            {
                Scanned = paths.Count, Candidates = paths.Count, Replaced = paths.Count,
                Marginal = 0, Grew = 0, Invalid = 0, Failed = 0, Skipped = 0,
                BytesSaved = 500 * paths.Count,
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-5),
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Files = files
            });
        }
    }

    private static MainViewModel MakeViewModel(
        ITriagePipeline? pipeline,
        IUiDispatcher? dispatcher = null,
        SettingsViewModel? settings = null,
        IFolderProbeScanner? scanner = null) =>
        new(
            scanner: scanner ?? new NoopFolderProbeScanner(),
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

    private sealed class RecordingScanner : IFolderProbeScanner
    {
        public int ScanCount { get; private set; }

        public Task<FolderScanSummary> ScanAsync(
            string folderPath,
            TriageOptions? options = null,
            bool recursive = false,
            IProgress<ProbeResult>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ScanCount++;
            return Task.FromResult(new FolderScanSummary
            {
                FilesDiscovered = 0,
                CandidateCount = 0,
                ProbeFailureCount = 0,
            });
        }
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
        public IReadOnlyList<string>? CapturedFilePaths { get; private set; }

        public Task<TriageSummary> RunAsync(
            string folder,
            IReadOnlyList<string> filePaths,
            TriageOptions options,
            IProgress<FileProgress>? progress = null,
            PauseToken? pauseToken = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            CapturedFilePaths = filePaths;
            return Task.FromResult(FakeTriagePipeline.EmptySummary());
        }
    }
}
