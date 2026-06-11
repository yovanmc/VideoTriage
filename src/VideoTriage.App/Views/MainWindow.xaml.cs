using VideoTriage.App.Services;
using VideoTriage.App.ViewModels;
using Wpf.Ui.Controls;

namespace VideoTriage.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly IApplicationWorkLifetime _workLifetime;
    private readonly MainViewModel _viewModel;
    private bool _closeConfirmed;

    public MainWindow(MainViewModel viewModel, IApplicationWorkLifetime workLifetime)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _workLifetime = workLifetime;
        Closing += OnWindowClosing;
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeConfirmed) return;
        e.Cancel = true;
        IsEnabled = false;
        try
        {
            await _workLifetime.StopAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            System.Windows.MessageBox.Show(
                "A video process did not stop within 10 seconds. The application will close. " +
                "You may need to end 'ffmpeg.exe' or 'HandBrakeCLI.exe' manually.",
                "VideoTriage",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }

        try
        {
            await _viewModel.CancelAndWaitAsync();
        }
        finally
        {
            _closeConfirmed = true;
            // Close() must not run re-entrantly inside the Closing event (it throws
            // InvalidOperationException when idle cleanup completes synchronously).
            // Schedule it as a fresh dispatcher operation after this cycle unwinds.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }
}
