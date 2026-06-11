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
    private readonly IExplorerLauncher? _explorerLauncher;
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
    private string? _interruptedRunNotice;
    private int _completedInRun;
    private int _totalInRun;
    private string? _runProgressText;
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
        IApplicationWorkLifetime? workLifetime = null,
        IExplorerLauncher? explorerLauncher = null)
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
        _explorerLauncher = explorerLauncher;
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
            // Process removals BEFORE additions: a Move (and Replace) raises CollectionChanged
            // with the SAME row in both OldItems and NewItems. Add-then-remove would net-remove
            // the row from the index, dropping its later progress updates and its summary thumbnail.
            if (e.OldItems is not null)
                foreach (FileItemViewModel row in e.OldItems)
                    _queueIndex.Remove(row.FilePath);
            if (e.NewItems is not null)
                foreach (FileItemViewModel row in e.NewItems)
                    _queueIndex[row.FilePath] = row;
            StartCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(StartBlockedReason));
            OnPropertyChanged(nameof(QueueSummaryText));
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
        OpenLogCommand = new RelayCommand(OpenLog);
        DismissInterruptedNoticeCommand = new RelayCommand(() => InterruptedRunNotice = null);
        if (settings is not null)
            settings.PropertyChanged += (_, _) =>
            {
                StartCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(StartBlockedReason));
                OnPropertyChanged(nameof(QueueSummaryText));
            };
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
    public IRelayCommand OpenLogCommand { get; }
    public IRelayCommand DismissInterruptedNoticeCommand { get; }

    public string? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                StartCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(StartBlockedReason));
                OnPropertyChanged(nameof(QueueSummaryText));
            }
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

    public string? InterruptedRunNotice
    {
        get => _interruptedRunNotice;
        private set => SetProperty(ref _interruptedRunNotice, value);
    }

    public string? RunProgressText
    {
        get => _runProgressText;
        private set => SetProperty(ref _runProgressText, value);
    }

    private void UpdateRunProgress() => RunProgressText = $"{_completedInRun} of {_totalInRun}";

    public string? StartBlockedReason
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SelectedFolder)) return "Choose a folder to scan.";
            if (Items.Count == 0) return "No candidates found in this folder.";
            if (_pipelineProvider?.Pipeline is null) return "Required video tools are unavailable.";
            if (Settings is { CanRun: false }) return Settings.ValidationMessage ?? "Fix settings before starting.";
            return null;
        }
    }

    public string QueueSummaryText
    {
        get
        {
            var count = Items.Count;
            if (count == 0) return "No candidates";
            var totalBytes = Items.Sum(i => i.SourceBytes);
            var noun = count == 1 ? "candidate" : "candidates";
            return $"{count} {noun} · {VideoTriage.Core.Formatting.HumanSize.Format(totalBytes)}";
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
        await ScanFolderAsync(folder);
    }

    private async Task ScanFolderAsync(string folder)
    {
        if (_scanner is null || IsScanning)
            return;

        // Cancel previous scan's thumbnails and wait for them to drain
        if (_scanCts is not null)
        {
            await CancelAndWaitAsync();
            _scanCts.Dispose();
            _scanCts = null;
        }

        InterruptedRunNotice = null;
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
                    TrackThumbnail(row, result.FilePath, result.Stats?.AttachedPicStreamIndex ?? IThumbnailService.VideoStream);
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
                InterruptedRunNotice = msg;
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
        _totalInRun = Items.Count;
        _completedInRun = 0;
        RunProgressText = null;
        UpdateRunProgress();
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
                Items.Select(r => r.FilePath).ToList(),
                options,
                progress,
                _pauseToken,
                _runCts.Token);

            _workLifetime?.Track(runTask, _runCts);

            var summary = await runTask;
            _lastRunDataDirectory = options.DryRun
                ? null
                : Path.Combine(SelectedFolder!, options.DataDirectoryName);
            var thumbs = _queueIndex.ToDictionary(
                kv => kv.Key, kv => kv.Value.Thumbnail, StringComparer.OrdinalIgnoreCase);
            LastSummary = new SummaryViewModel(summary, thumbs, _explorerLauncher);
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
            StatusMessage = "Run failed — see log";
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

    /// <summary>
    /// Headless automation entry point for the screenshot harness: scans <paramref name="folder"/>
    /// without the folder picker, optionally runs the pipeline, then writes a completion signal file.
    /// <paramref name="autoStart"/> triggers the REAL (destructive) pipeline, so the App only enables
    /// it in DEBUG builds. Not used in normal interactive operation.
    /// </summary>
    public async Task RunAutomationAsync(string folder, bool autoStart, string? doneSignalPath)
    {
        try
        {
            SelectedFolder = folder;
            await ScanFolderAsync(folder);

            // Let in-flight thumbnail extraction finish so the queue is fully populated before a run.
            Task[] pending;
            lock (_thumbnailLock)
                pending = _thumbnailTasks.ToArray();
            if (pending.Length > 0)
                await Task.WhenAll(pending);

            if (autoStart)
            {
                // Session-only confirm (never persisted) so CanStart passes under the default
                // Permanent delete mode. Automation runs against disposable test clips.
                if (Settings is not null)
                    Settings.ConfirmPermanentDelete = true;
                if (CanStart())
                    await StartAsync();
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(doneSignalPath))
            {
                try { File.WriteAllText(doneSignalPath!, DateTimeOffset.UtcNow.ToString("o")); }
                catch { /* best-effort completion signal */ }
            }
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
        {
            QueueRemainingCount = Math.Max(0, QueueRemainingCount - 1);
            _completedInRun++;
            UpdateRunProgress();
        }
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
        OpenDataDirectoryCommand.NotifyCanExecuteChanged();
        if (!string.IsNullOrWhiteSpace(SelectedFolder))
            _ = ScanFolderAsync(SelectedFolder!);
        else
            QueueRemainingCount = Items.Count;
    }

    private void OpenLog()
    {
        var path = _appLog?.CurrentLogPath;
        if (!string.IsNullOrWhiteSpace(path))
            _explorerLauncher?.Open(path);
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
