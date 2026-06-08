using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoTriage.App.Services;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;

namespace VideoTriage.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IFolderProbeScanner? _scanner;
    private readonly IDialogService _dialogService;
    private readonly IUiDispatcher _dispatcher;
    private string? _selectedFolder;
    private bool _isScanning;

    public MainViewModel(
        IFolderProbeScanner? scanner,
        IDialogService dialogService,
        IUiDispatcher dispatcher,
        IPrerequisiteService prerequisiteService)
    {
        _scanner = scanner;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
        Prerequisites = prerequisiteService.Check();
        ChooseFolderCommand = new AsyncRelayCommand(
            ChooseFolderAsync,
            () => _scanner is not null && !IsScanning);
    }

    public ObservableCollection<FileItemViewModel> Items { get; } = [];
    public IReadOnlyList<ToolPrerequisiteStatus> Prerequisites { get; }
    public IAsyncRelayCommand ChooseFolderCommand { get; }

    public string? SelectedFolder
    {
        get => _selectedFolder;
        private set => SetProperty(ref _selectedFolder, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
                ChooseFolderCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task ChooseFolderAsync()
    {
        if (_scanner is null || IsScanning)
            return;

        var folder = _dialogService.ChooseFolder(SelectedFolder);
        if (string.IsNullOrWhiteSpace(folder))
            return;

        SelectedFolder = folder;
        IsScanning = true;
        _dispatcher.Post(Items.Clear);

        try
        {
            var progress = new InlineProgress<ProbeResult>(result =>
                _dispatcher.Post(() =>
                {
                    var row = new FileItemViewModel(result.FilePath);
                    row.ApplyProbe(result);
                    Items.Add(row);
                }));

            await _scanner.ScanAsync(
                folder,
                progress: progress,
                cancellationToken: CancellationToken.None);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
