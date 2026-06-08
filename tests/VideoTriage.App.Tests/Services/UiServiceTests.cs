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
    public void RecordingUiDispatcher_Post_ExecutesActionAndCountsCall()
    {
        var dispatcher = new RecordingUiDispatcher();
        var executed = false;

        dispatcher.Post(() => executed = true);

        executed.ShouldBeTrue();
        dispatcher.PostCount.ShouldBe(1);
    }

    [Fact]
    public void UiDispatcher_Post_ExecutesBeforeReturning()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = new UiDispatcher(Dispatcher.CurrentDispatcher);
                var executed = false;

                dispatcher.Post(() => executed = true);

                executed.ShouldBeTrue();
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
}
