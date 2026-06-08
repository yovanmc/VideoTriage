using Shouldly;
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

    private static MainViewModel MakeViewModel(
        ITriagePipeline? pipeline,
        IUiDispatcher? dispatcher = null) =>
        new(
            scanner: new NoopFolderProbeScanner(),
            new FakeDialogService(),
            dispatcher ?? new RecordingUiDispatcher(),
            new AvailablePrerequisiteService(),
            new StubPipelineProvider(pipeline),
            () => new TriageOptions());

    private sealed class StubPipelineProvider(ITriagePipeline? pipeline) : ITriagePipelineProvider
    {
        public ITriagePipeline? Pipeline { get; } = pipeline;
    }

    private sealed class NoopFolderProbeScanner : IFolderProbeScanner
    {
        public Task<IReadOnlyList<ProbeResult>> ScanAsync(
            string folderPath,
            TriageOptions? options = null,
            bool recursive = false,
            IProgress<ProbeResult>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProbeResult>>([]);
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
