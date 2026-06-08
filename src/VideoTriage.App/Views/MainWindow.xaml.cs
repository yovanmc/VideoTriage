using VideoTriage.App.ViewModels;
using Wpf.Ui.Controls;

namespace VideoTriage.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
