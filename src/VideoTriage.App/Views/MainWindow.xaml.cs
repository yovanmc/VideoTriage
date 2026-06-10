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
            await _viewModel.CancelAndWaitAsync();
        }
        finally
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
