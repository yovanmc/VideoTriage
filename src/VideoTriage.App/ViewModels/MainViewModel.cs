using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoTriage.App.Services;
using VideoTriage.Core.Models;
using VideoTriage.Core.Pipeline;
using VideoTriage.Core.Probing;

namespace VideoTriage.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IFolderProbeScanner? _scanner;
    private readonly IDialogService _dialogService;
    private readonly IUiDispatcher _dispatcher;
    private readonly ITriagePipelineProvider? _pipelineProvider;
    private readonly Func<TriageOptions> _optionsFactory;
    private CancellationTokenSource? _runCts;
    private PauseToken? _pauseToken;
    private string? _selectedFolder;
    private bool _isScanning;
    private RunState _runState = RunState.Idle;

    public MainViewModel(
        IFolderProbeScanner? scanner,
        IDialogService dialogService,
        IUiDispatcher dispatcher,
        IPrerequisiteService prerequisiteService,
        ITriagePipelineProvider? pipelineProvider = null,
        Func<TriageOptions>? optionsFactory = null,
        SettingsViewModel? settings = null)
    {
        _scanner = scanner;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
        _pipelineProvider = pipelineProvider;
        Settings = settings;
        _optionsFactory = optionsFactory
            ?? (settings is null ? () => new TriageOptions() : settings.ToTriageOptions);
        Prerequisites = prerequisiteService.Check();
        ChooseFolderCommand = new AsyncRelayCommand(
            ChooseFolderAsync,
            () => _scanner is not null && !IsScanning && RunState == RunState.Idle);
        StartCommand = new AsyncRelayCommand(StartAsync, CanStart);
        PauseCommand = new RelayCommand(Pause, () => RunState == RunState.Running);
        ResumeCommand = new RelayCommand(Resume, () => RunState == RunState.Paused);
        StopCommand = new RelayCommand(
            Stop,
            () => RunState is RunState.Running or RunState.Paused);
        if (settings is not null)
            settings.PropertyChanged += (_, _) => StartCommand.NotifyCanExecuteChanged();
    }

    public ObservableCollection<FileItemViewModel> Items { get; } = [];
    public IReadOnlyList<ToolPrerequisiteStatus> Prerequisites { get; }
    public SettingsViewModel? Settings { get; }
    public IAsyncRelayCommand ChooseFolderCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IRelayCommand PauseCommand { get; }
    public IRelayCommand ResumeCommand { get; }
    public IRelayCommand StopCommand { get; }

    public string? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
                StartCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                ChooseFolderCommand.NotifyCanExecuteChanged();
                StartCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RunState RunState
    {
        get => _runState;
        private set
        {
            if (SetProperty(ref _runState, value))
                NotifyCommandState();
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

    private bool CanStart() =>
        !IsScanning &&
        RunState == RunState.Idle &&
        !string.IsNullOrWhiteSpace(SelectedFolder) &&
        _pipelineProvider?.Pipeline is not null &&
        (Settings?.CanRun ?? true);

    private async Task StartAsync()
    {
        if (!CanStart())
            return;

        _runCts = new CancellationTokenSource();
        _pauseToken = new PauseToken();
        RunState = RunState.Running;
        try
        {
            var progress = new InlineProgress<FileProgress>(fp =>
                _dispatcher.Post(() => ApplyProgress(fp)));
            var pipeline = _pipelineProvider!.Pipeline
                ?? throw new InvalidOperationException("Required video tools are unavailable.");

            _ = await pipeline.RunAsync(
                SelectedFolder!,
                _optionsFactory(),
                recursive: true,
                progress,
                _pauseToken,
                _runCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            RunState = RunState.Idle;
            _runCts.Dispose();
            _runCts = null;
            _pauseToken = null;
            NotifyCommandState();
        }
    }

    private void ApplyProgress(FileProgress fp)
    {
        var fullPath = Path.GetFullPath(fp.FilePath);
        foreach (var row in Items)
        {
            if (string.Equals(row.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                row.Apply(fp);
                return;
            }
        }
    }

    private void Pause()
    {
        _pauseToken?.Pause();
        RunState = RunState.Paused;
    }

    private void Resume()
    {
        _pauseToken?.Resume();
        RunState = RunState.Running;
    }

    private void Stop()
    {
        RunState = RunState.Stopping;
        _runCts?.Cancel();
    }

    private void NotifyCommandState()
    {
        ChooseFolderCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
