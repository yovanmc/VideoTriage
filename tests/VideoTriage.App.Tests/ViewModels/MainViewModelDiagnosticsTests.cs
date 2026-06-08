using Shouldly;
using VideoTriage.App.Models;
using VideoTriage.App.Services;
using VideoTriage.App.Tests.Fakes;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;
using VideoTriage.Core.Pipeline;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class MainViewModelDiagnosticsTests
{
    [Fact]
    public async Task StartAsync_PipelineThrows_LogsAddsFriendlyErrorAndResetsRunState()
    {
        var appLog = new RecordingAppLog();
        var errors = new UserErrorSink(() => DateTimeOffset.UnixEpoch);
        var diagnostics = new DiagnosticsViewModel(errors, appLog);
        var viewModel = CreateViewModel(
            new ThrowingPipeline(new IOException("disk failed")),
            appLog,
            errors,
            diagnostics);
        viewModel.SelectedFolder = @"C:\Videos";

        await viewModel.StartCommand.ExecuteAsync(null);

        viewModel.RunState.ShouldBe(RunState.Idle);
        appLog.Exceptions.Single().Message.ShouldBe("disk failed");
        errors.Errors.Single().Title.ShouldBe("Run failed");
        errors.Errors.Single().Message.ShouldContain("completed replacements may already be present");
        errors.Errors.Single().Message.ShouldContain(appLog.CurrentLogPath);
        diagnostics.ErrorCount.ShouldBe(1);
        viewModel.StatusMessage.ShouldBe("Run failed. See Diagnostics for details.");
    }

    [Fact]
    public async Task StartAsync_Cancelled_DoesNotLogError()
    {
        var appLog = new RecordingAppLog();
        var errors = new UserErrorSink(() => DateTimeOffset.UnixEpoch);
        var diagnostics = new DiagnosticsViewModel(errors, appLog);
        var viewModel = CreateViewModel(
            new CancellingPipeline(),
            appLog,
            errors,
            diagnostics);
        viewModel.SelectedFolder = @"C:\Videos";

        await viewModel.StartCommand.ExecuteAsync(null);

        appLog.Exceptions.ShouldBeEmpty();
        errors.Errors.ShouldBeEmpty();
        viewModel.StatusMessage.ShouldNotBeNull().ShouldContain("Completed replacements may remain");
    }

    private static MainViewModel CreateViewModel(
        ITriagePipeline pipeline,
        IAppLog appLog,
        IUserErrorSink errors,
        DiagnosticsViewModel diagnostics) =>
        new(
            scanner: null,
            new FakeDialogService(),
            new RecordingUiDispatcher(),
            new AvailablePrerequisiteService(),
            new PipelineProvider(pipeline),
            () => new TriageOptions(),
            settings: null,
            appLog,
            errors,
            diagnostics);

    private sealed class ThrowingPipeline(Exception exception) : ITriagePipeline
    {
        public Task<TriageSummary> RunAsync(
            string folder,
            TriageOptions options,
            bool recursive = false,
            IProgress<FileProgress>? progress = null,
            PauseToken? pauseToken = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<TriageSummary>(exception);
    }

    private sealed class CancellingPipeline : ITriagePipeline
    {
        public Task<TriageSummary> RunAsync(
            string folder,
            TriageOptions options,
            bool recursive = false,
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

    private sealed class RecordingAppLog : IAppLog
    {
        public string LogDirectory => @"C:\logs";
        public string CurrentLogPath => @"C:\logs\videotriage.log";
        public List<Exception> Exceptions { get; } = [];
        public void Information(string message) { }
        public void Error(Exception exception, string message) => Exceptions.Add(exception);
    }
}
