using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoTriage.App.Services;

namespace VideoTriage.App.ViewModels;

public sealed class DiagnosticsViewModel : ObservableObject
{
    private readonly IUserErrorSink _errorSink;
    private readonly IAppLog _appLog;

    public DiagnosticsViewModel(IUserErrorSink errorSink, IAppLog appLog)
    {
        _errorSink = errorSink;
        _appLog = appLog;
        ClearCommand = new RelayCommand(Clear, () => ErrorCount > 0);
    }

    public string LogPath => _appLog.CurrentLogPath;
    public IReadOnlyList<UserError> Errors => _errorSink.Errors;
    public int ErrorCount => Errors.Count;
    public UserError? LatestError => Errors.LastOrDefault();
    public IRelayCommand ClearCommand { get; }

    public void Refresh()
    {
        OnPropertyChanged(nameof(LogPath));
        OnPropertyChanged(nameof(Errors));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(LatestError));
        ClearCommand.NotifyCanExecuteChanged();
    }

    private void Clear()
    {
        _errorSink.Clear();
        Refresh();
    }
}
