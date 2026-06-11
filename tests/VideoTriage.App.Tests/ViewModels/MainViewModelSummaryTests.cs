using Shouldly;
using VideoTriage.App.Services;
using VideoTriage.App.Tests.Fakes;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;
using VideoTriage.Core.Pipeline;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class MainViewModelSummaryTests
{
    [Fact]
    public async Task StartAsync_RunReturns_AssignsSummary()
    {
        var viewModel = CreateViewModel(new CompletingPipeline(Summary()));
        viewModel.SelectedFolder = @"C:\Videos";
        viewModel.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));

        await viewModel.StartCommand.ExecuteAsync(null);

        viewModel.LastSummary.ShouldNotBeNull();
        viewModel.LastSummary.ReplacedCount.ShouldBe(1);
        viewModel.BackToQueueCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_Cancelled_DoesNotAssignSummary()
    {
        var viewModel = CreateViewModel(new CancellingPipeline());
        viewModel.SelectedFolder = @"C:\Videos";
        viewModel.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));

        await viewModel.StartCommand.ExecuteAsync(null);

        viewModel.LastSummary.ShouldBeNull();
        viewModel.OpenDataDirectoryCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task OpenDataDirectory_UsesFolderAndRunOptionDirectoryName()
    {
        var dialog = new FakeDialogService();
        var viewModel = CreateViewModel(
            new CompletingPipeline(Summary()),
            dialog,
            () => new TriageOptions { DataDirectoryName = ".triage-state" });
        viewModel.SelectedFolder = @"C:\Videos";
        viewModel.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));

        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.OpenDataDirectoryCommand.Execute(null);

        dialog.OpenedDirectory.ShouldBe(Path.Combine(@"C:\Videos", ".triage-state"));
    }

    [Fact]
    public async Task BackToQueue_ClearsVisibleSummary()
    {
        var viewModel = CreateViewModel(new CompletingPipeline(Summary()));
        viewModel.SelectedFolder = @"C:\Videos";
        viewModel.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));
        await viewModel.StartCommand.ExecuteAsync(null);

        viewModel.BackToQueueCommand.Execute(null);

        viewModel.LastSummary.ShouldBeNull();
    }

    [Fact]
    public async Task DryRun_DoesNotOfferMissingDataDirectory()
    {
        var viewModel = CreateViewModel(
            new CompletingPipeline(Summary()),
            optionsFactory: () => new TriageOptions { DryRun = true });
        viewModel.SelectedFolder = @"C:\Videos";
        viewModel.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));

        await viewModel.StartCommand.ExecuteAsync(null);

        viewModel.LastSummary.ShouldNotBeNull();
        viewModel.OpenDataDirectoryCommand.CanExecute(null).ShouldBeFalse();
    }

    private static MainViewModel CreateViewModel(
        ITriagePipeline pipeline,
        FakeDialogService? dialog = null,
        Func<TriageOptions>? optionsFactory = null) =>
        new(
            scanner: null,
            dialog ?? new FakeDialogService(),
            new RecordingUiDispatcher(),
            new AvailablePrerequisiteService(),
            new PipelineProvider(pipeline),
            optionsFactory ?? (() => new TriageOptions()));

    private static TriageSummary Summary() => new()
    {
        Scanned = 1,
        Candidates = 1,
        Replaced = 1,
        Marginal = 0,
        Grew = 0,
        Invalid = 0,
        Failed = 0,
        Skipped = 0,
        BytesSaved = 500,
        StartedAtUtc = DateTimeOffset.UtcNow,
        CompletedAtUtc = DateTimeOffset.UtcNow,
        Files = []
    };

    private sealed class CompletingPipeline(TriageSummary summary) : ITriagePipeline
    {
        public Task<TriageSummary> RunAsync(
            string folder,
            IReadOnlyList<string> filePaths,
            TriageOptions options,
            IProgress<FileProgress>? progress = null,
            PauseToken? pauseToken = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(summary);
    }

    private sealed class CancellingPipeline : ITriagePipeline
    {
        public Task<TriageSummary> RunAsync(
            string folder,
            IReadOnlyList<string> filePaths,
            TriageOptions options,
            IProgress<FileProgress>? progress = null,
            PauseToken? pauseToken = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<TriageSummary>(new OperationCanceledException(cancellationToken));
    }

    private sealed class PipelineProvider(ITriagePipeline pipeline) : ITriagePipelineProvider
    {
        public ITriagePipeline Pipeline { get; } = pipeline;
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
