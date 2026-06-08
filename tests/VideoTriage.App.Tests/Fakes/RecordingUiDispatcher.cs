using VideoTriage.App.Services;

namespace VideoTriage.App.Tests.Fakes;

public sealed class RecordingUiDispatcher : IUiDispatcher
{
    public int PostCount { get; private set; }

    public void Post(Action action)
    {
        PostCount++;
        action();
    }
}
