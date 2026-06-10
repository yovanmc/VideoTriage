using System.Windows.Threading;

namespace VideoTriage.App.Services;

public sealed class UiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        dispatcher.BeginInvoke(() =>
        {
            try { action(); }
            catch (Exception) { /* UI-update exceptions are non-fatal; discard silently */ }
        }, DispatcherPriority.Background);
    }
}
