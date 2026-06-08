using Shouldly;
using VideoTriage.App.Services;
using VideoTriage.App.ViewModels;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class DiagnosticsViewModelTests
{
    [Fact]
    public void Constructor_ProjectsLogPathCountAndLatestError()
    {
        var sink = new UserErrorSink(() => DateTimeOffset.UnixEpoch);
        sink.Add(UserErrorSeverity.Warning, "First", "one");
        sink.Add(UserErrorSeverity.Error, "Second", "two", "detail");
        var log = new FakeAppLog(@"C:\logs", @"C:\logs\videotriage-20260607.log");

        var viewModel = new DiagnosticsViewModel(sink, log);

        viewModel.LogPath.ShouldBe(@"C:\logs\videotriage-20260607.log");
        viewModel.ErrorCount.ShouldBe(2);
        viewModel.LatestError.ShouldNotBeNull().Title.ShouldBe("Second");
        viewModel.Errors.Count.ShouldBe(2);
        viewModel.ClearCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Refresh_NotifiesSnapshotProperties()
    {
        var sink = new UserErrorSink(() => DateTimeOffset.UnixEpoch);
        var viewModel = new DiagnosticsViewModel(
            sink,
            new FakeAppLog("logs", "logs\\today.log"));
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        sink.Add(UserErrorSeverity.Error, "Failure", "message");

        viewModel.Refresh();

        changed.ShouldContain(nameof(DiagnosticsViewModel.Errors));
        changed.ShouldContain(nameof(DiagnosticsViewModel.ErrorCount));
        changed.ShouldContain(nameof(DiagnosticsViewModel.LatestError));
        viewModel.ErrorCount.ShouldBe(1);
    }

    [Fact]
    public void ClearCommand_ClearsSinkAndProjection()
    {
        var sink = new UserErrorSink(() => DateTimeOffset.UnixEpoch);
        sink.Add(UserErrorSeverity.Error, "Failure", "message");
        var viewModel = new DiagnosticsViewModel(
            sink,
            new FakeAppLog("logs", "logs\\today.log"));

        viewModel.ClearCommand.Execute(null);

        sink.Errors.ShouldBeEmpty();
        viewModel.ErrorCount.ShouldBe(0);
        viewModel.LatestError.ShouldBeNull();
        viewModel.ClearCommand.CanExecute(null).ShouldBeFalse();
    }

    private sealed record FakeAppLog(
        string LogDirectory,
        string CurrentLogPath) : IAppLog
    {
        public void Information(string message) { }
        public void Error(Exception exception, string message) { }
    }
}
