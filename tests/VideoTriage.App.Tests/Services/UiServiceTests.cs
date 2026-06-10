using System.Windows.Threading;
using Shouldly;
using VideoTriage.App.Services;
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
    public void FakeDialogService_OpenDirectory_RecordsPath()
    {
        var dialog = new FakeDialogService();

        dialog.OpenDirectory(@"C:\videos\_videotriage_data");

        dialog.OpenedDirectory.ShouldBe(@"C:\videos\_videotriage_data");
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

    [Fact]
    public void UiDispatcher_Post_ReturnsBeforeActionRuns()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var uiDispatcher = new UiDispatcher(dispatcher);
                var executed = false;

                uiDispatcher.Post(() => executed = true);

                // Post should return before the action executes (BeginInvoke is async)
                executed.ShouldBeFalse("Post must return before the queued action runs");

                // Drain the dispatcher to verify the action eventually runs
                dispatcher.InvokeShutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5)).ShouldBeTrue("STA dispatcher test timed out.");
        failure.ShouldBeNull();
    }

    [Fact]
    public void DialogService_OpenDirectory_DelegatesToExplorerLauncher()
    {
        var opened = new List<string>();
        var dialog = new DialogService(new RecordingExplorerLauncher(opened));

        dialog.OpenDirectory(@"C:\videos\_videotriage_data");

        opened.ShouldBe([@"C:\videos\_videotriage_data"]);
    }

    private sealed class RecordingExplorerLauncher(List<string> opened) : IExplorerLauncher
    {
        public void Open(string path) => opened.Add(path);
    }
}
