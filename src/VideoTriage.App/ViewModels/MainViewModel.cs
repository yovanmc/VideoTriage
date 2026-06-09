using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
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
    private readonly IAppLog? _appLog;
    private readonly IUserErrorSink? _userErrors;
    private CancellationTokenSource? _runCts;
    private PauseToken? _pauseToken;
    private string? _lastRunDataDirectory;
    private SummaryViewModel? _lastSummary;
    private string? _selectedFolder;
    private bool _isScanning;
    private RunState _runState = RunState.Idle;
    private string? _statusMessage;
    private int _queueRemainingCount;

    public MainViewModel(
        IFolderProbeScanner? scanner,
        IDialogService dialogService,
        IUiDispatcher dispatcher,
        IPrerequisiteService prerequisiteService,
        ITriagePipelineProvider? pipelineProvider = null,
        Func<TriageOptions>? optionsFactory = null,
        SettingsViewModel? settings = null,
        IAppLog? appLog = null,
        IUserErrorSink? userErrors = null,
        DiagnosticsViewModel? diagnostics = null)
    {
        _scanner = scanner;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
        _pipelineProvider = pipelineProvider;
        _appLog = appLog;
        _userErrors = userErrors;
        Settings = settings;
        Diagnostics = diagnostics;
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
        OpenDataDirectoryCommand = new RelayCommand(
            OpenDataDirectory,
            () => _lastRunDataDirectory is not null);
        BackToQueueCommand = new RelayCommand(BackToQueue, () => LastSummary is not null);
        if (settings is not null)
            settings.PropertyChanged += (_, _) => StartCommand.NotifyCanExecuteChanged();
    }

    public ObservableCollection<FileItemViewModel> Items { get; } = [];
    public IReadOnlyList<ToolPrerequisiteStatus> Prerequisites { get; }
    public SettingsViewModel? Settings { get; }
    public DiagnosticsViewModel? Diagnostics { get; }
    public IAsyncRelayCommand ChooseFolderCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IRelayCommand PauseCommand { get; }
    public IRelayCommand ResumeCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand OpenDataDirectoryCommand { get; }
    public IRelayCommand BackToQueueCommand { get; }

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

    public SummaryViewModel? LastSummary
    {
        get => _lastSummary;
        private set
        {
            if (!SetProperty(ref _lastSummary, value))
                return;

            OpenDataDirectoryCommand.NotifyCanExecuteChanged();
            BackToQueueCommand.NotifyCanExecuteChanged();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int QueueRemainingCount
    {
        get => _queueRemainingCount;
        private set => SetProperty(ref _queueRemainingCount, value);
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
                    if (result.Classification?.Outcome != ClassificationOutcome.Candidate)
                        return;
                    var row = new FileItemViewModel(result.FilePath);
                    row.ApplyProbe(result);
                    Items.Add(row);
                    if (result.Stats?.AttachedPicStreamIndex is { } streamIndex)
                        _ = ExtractThumbnailAsync(row, result.FilePath, streamIndex);
                }));

            await _scanner.ScanAsync(
                folder,
                progress: progress,
                cancellationToken: CancellationToken.None);
        }
        finally
        {
            IsScanning = false;
            QueueRemainingCount = Items.Count;
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
        _lastRunDataDirectory = null;
        LastSummary = null;
        StatusMessage = null;
        QueueRemainingCount = Items.Count;
        OpenDataDirectoryCommand.NotifyCanExecuteChanged();
        RunState = RunState.Running;
        try
        {
            var progress = new InlineProgress<FileProgress>(fp =>
                _dispatcher.Post(() => ApplyProgress(fp)));
            var pipeline = _pipelineProvider!.Pipeline
                ?? throw new InvalidOperationException("Required video tools are unavailable.");
            var options = _optionsFactory();

            var summary = await pipeline.RunAsync(
                SelectedFolder!,
                options,
                recursive: true,
                progress,
                _pauseToken,
                _runCts.Token);
            _lastRunDataDirectory = options.DryRun
                ? null
                : Path.Combine(SelectedFolder!, options.DataDirectoryName);
            LastSummary = new SummaryViewModel(summary);
        }
        catch (OperationCanceledException)
        {
            StatusMessage =
                "Run stopped. Completed replacements may remain; review the queue before retrying.";
        }
        catch (Exception exception)
        {
            _appLog?.Error(exception, $"Video triage failed for '{SelectedFolder}'.");
            _userErrors?.Add(
                UserErrorSeverity.Error,
                "Run failed",
                "VideoTriage stopped unexpectedly. Completed replacements may already be present. " +
                $"Review the queue and log before retrying: {_appLog?.CurrentLogPath ?? "log unavailable"}",
                exception.Message);
            Diagnostics?.Refresh();
            StatusMessage = "Run failed. See Diagnostics for details.";
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
        for (var i = 0; i < Items.Count; i++)
        {
            if (!string.Equals(Items[i].FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                continue;

            Items[i].Apply(fp);

            if (fp.Phase == TriagePhase.Encoding && fp.EncodeProgress is null && i > 0)
                Items.Move(i, 0);
            else if (fp.Phase == TriagePhase.Done && i < Items.Count - 1)
                Items.Move(i, Items.Count - 1);

            if (fp.Phase == TriagePhase.Done)
                QueueRemainingCount = Math.Max(0, QueueRemainingCount - 1);

            return;
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

    private void OpenDataDirectory()
    {
        if (_lastRunDataDirectory is not null)
            _dialogService.OpenDirectory(_lastRunDataDirectory);
    }

    private void BackToQueue()
    {
        _lastRunDataDirectory = null;
        LastSummary = null;
        QueueRemainingCount = Items.Count;
        OpenDataDirectoryCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCommandState()
    {
        ChooseFolderCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private async Task ExtractThumbnailAsync(FileItemViewModel row, string filePath, int streamIndex)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"vt_thumb_{Guid.NewGuid():N}.png");
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{filePath}\" -map 0:{streamIndex} -frames:v 1 -loglevel quiet \"{temp}\" -y",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            proc.Start();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0 && File.Exists(temp) && new FileInfo(temp).Length > 0)
            {
                using var memStream = new MemoryStream(File.ReadAllBytes(temp));
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memStream;
                bitmap.DecodePixelWidth = 96;
                bitmap.EndInit();
                bitmap.Freeze();
                _dispatcher.Post(() => row.Thumbnail = bitmap);
            }
        }
        catch
        {
            // Thumbnail extraction is best-effort; failures leave Thumbnail as null
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
