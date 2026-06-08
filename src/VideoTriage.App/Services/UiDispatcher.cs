using System.Windows.Threading;

namespace VideoTriage.App.Services;

public sealed class UiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        dispatcher.BeginInvoke(action);
    }
}
