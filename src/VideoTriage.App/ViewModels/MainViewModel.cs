using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoTriage.App.Services;
using VideoTriage.Core.Models;
using VideoTriage.Core.Pipeline;
using VideoTriage.Core.Probing;
using VideoTriage.Core.State;

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
    private readonly Func<string, IActiveRunJournal>? _activeRunJournalFactory;
    private readonly IThumbnailService? _thumbnailService;
    private readonly IApplicationWorkLifetime? _workLifetime;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _scanCts;
    private PauseToken? _pauseToken;
    private string? _lastRunDataDirectory;
    private SummaryViewModel? _lastSummary;
    private string? _selectedFolder;
    private bool _isScanning;
    private RunState _runState = RunState.Idle;
    private string? _statusMessage;
    private int _queueRemainingCount;
    private readonly HashSet<Task> _thumbnailTasks = [];
    private readonly object _thumbnailLock = new();
    private readonly Dictionary<string, FileItemViewModel> _queueIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileProgress> _pendingProgress =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingLock = new();

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
        DiagnosticsViewModel? diagnostics = null,
        Func<string, IActiveRunJournal>? activeRunJournalFactory = null,
        IThumbnailService? thumbnailService = null,
        IApplicationWorkLifetime? workLifetime = null)
    {
        _scanner = scanner;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
        _pipelineProvider = pipelineProvider;
        _appLog = appLog;
        _userErrors = userErrors;
        _activeRunJournalFactory = activeRunJournalFactory;
        _thumbnailService = thumbnailService;
        _workLifetime = workLifetime;
        Settings = settings;
        Diagnostics = diagnostics;
        _optionsFactory = optionsFactory
            ?? (settings is null ? () => new TriageOptions() : settings.ToTriageOptions);
        Prerequisites = prerequisiteService.Check();
        ChooseFolderCommand = new AsyncRelayCommand(
            ChooseFolderAsync,
            () => _scanner is not null && !IsScanning && RunState == RunState.Idle);
        StartCommand = new AsyncRelayCommand(StartAsync, CanStart);
        Items.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                _queueIndex.Clear();
            if (e.NewItems is not null)
                foreach (FileItemViewModel row in e.NewItems)
                    _queueIndex[row.FilePath] = row;
            if (e.OldItems is not null)
                foreach (FileItemViewModel row in e.OldItems)
                    _queueIndex.Remove(row.FilePath);
            StartCommand.NotifyCanExecuteChanged();
        };
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
        // Cancel previous scan's thumbnails and wait for them to drain
        if (_scanCts is not null)
        {
            await CancelAndWaitAsync();
            _scanCts.Dispose();
            _scanCts = null;
        }

        if (_scanner is null || IsScanning)
            return;

        var folder = _dialogService.ChooseFolder(SelectedFolder);
        if (string.IsNullOrWhiteSpace(folder))
            return;

        SelectedFolder = folder;
        IsScanning = true;
        _dispatcher.Post(() =>
        {
            foreach (var row in Items)
                row.Thumbnail = null;
            Items.Clear();
        });

        _scanCts = new CancellationTokenSource();

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
                        TrackThumbnail(row, result.FilePath, streamIndex);
                }));

            await _scanner.ScanAsync(
                folder,
                recursive: Settings?.Recursive ?? true,
                progress: progress,
                cancellationToken: CancellationToken.None);

            // Check for an active-run file left by a previous crash
            var dataDirectory = Path.Combine(folder, new TriageOptions().DataDirectoryName);
            var activeRun = _activeRunJournalFactory?.Invoke(dataDirectory)?.Load();
            if (activeRun is not null)
            {
                var msg = $"⚠ Previous run was interrupted at '{Path.GetFileName(activeRun.CurrentFile ?? "?")}'" +
                          $" (phase: {activeRun.CurrentPhase}, {activeRun.CompletedFiles}/{activeRun.TotalFiles} completed)." +
                          " The replacement journal contains recovery information.";
                _appLog?.Information(msg);
                // TODO: surface in Diagnostics panel (Phase 4)
            }
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
        Items.Count > 0 &&
        _pipelineProvider?.Pipeline is not null &&
        (Settings?.CanRun ?? true);

    private async Task StartAsync()
    {
        if (!CanStart())
            return;

        lock (_pendingLock)
            _pendingProgress.Clear();
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
            var progress = new InlineProgress<FileProgress>(PostLatest);
            var pipeline = _pipelineProvider!.Pipeline
                ?? throw new InvalidOperationException("Required video tools are unavailable.");
            var options = _optionsFactory();

            var runTask = pipeline.RunAsync(
                SelectedFolder!,
                options,
                recursive: Settings?.Recursive ?? true,
                progress,
                _pauseToken,
                _runCts.Token);

            _workLifetime?.Track(runTask, _runCts);

            var summary = await runTask;
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
        if (!_queueIndex.TryGetValue(fullPath, out var row))
            return;

        row.Apply(fp);

        var i = Items.IndexOf(row);
        if (i < 0) return;

        if (fp.Phase == TriagePhase.Encoding && fp.EncodeProgress is null && i > 0)
            Items.Move(i, 0);
        else if (fp.Phase == TriagePhase.Done && i < Items.Count - 1)
            Items.Move(i, Items.Count - 1);

        if (fp.Phase == TriagePhase.Done)
            QueueRemainingCount = Math.Max(0, QueueRemainingCount - 1);
    }

    private void PostLatest(FileProgress fp)
    {
        bool isFirst;
        lock (_pendingLock)
        {
            isFirst = !_pendingProgress.ContainsKey(fp.FilePath);
            _pendingProgress[fp.FilePath] = fp;
        }

        if (!isFirst) return;

        _dispatcher.Post(() =>
        {
            FileProgress latest;
            lock (_pendingLock)
            {
                if (!_pendingProgress.TryGetValue(fp.FilePath, out latest!)) return;
                _pendingProgress.Remove(fp.FilePath);
            }
            ApplyProgress(latest);
        });
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

    private void TrackThumbnail(FileItemViewModel row, string filePath, int streamIndex)
    {
        if (_thumbnailService is null) return;
        var cts = _scanCts;
        if (cts is null) return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_thumbnailLock)
            _thumbnailTasks.Add(tcs.Task);

        _ = Task.Run(async () =>
        {
            try
            {
                var bitmap = await _thumbnailService.GetAsync(filePath, streamIndex, cts.Token);
                if (bitmap is not null)
                    _dispatcher.Post(() => row.Thumbnail = bitmap);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _appLog?.Information($"[Warning] Thumbnail extraction failed for '{Path.GetFileName(filePath)}': {ex.Message}");
            }
            finally
            {
                lock (_thumbnailLock)
                    _thumbnailTasks.Remove(tcs.Task);
                tcs.SetResult();
            }
        });
    }

    public async Task CancelAndWaitAsync()
    {
        _scanCts?.Cancel();
        Task[] pending;
        lock (_thumbnailLock)
            pending = [.. _thumbnailTasks];
        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException) { /* thumbnail tasks didn't drain; process is exiting */ }
            catch (OperationCanceledException) { }
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
