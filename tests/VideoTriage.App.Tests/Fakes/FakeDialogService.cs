using VideoTriage.App.Services;

namespace VideoTriage.App.Tests.Fakes;

public sealed class FakeDialogService : IDialogService
{
    public string? Result { get; set; }
    public string? LastInitialFolder { get; private set; }

    public string? ChooseFolder(string? initialFolder)
    {
        LastInitialFolder = initialFolder;
        return Result;
    }
}
