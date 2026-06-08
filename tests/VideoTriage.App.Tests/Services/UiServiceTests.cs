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
