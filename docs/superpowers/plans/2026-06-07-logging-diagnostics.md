# Logging Diagnostics And User Errors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist rolling application logs and present concise, bounded, user-facing diagnostic errors with a discoverable log path.

**Architecture:** A small `Microsoft.Extensions.Logging` provider writes one UTF-8 text file per UTC day under `%LocalAppData%\VideoTriage\Logs`. `AppLog` is the App-facing logging facade, while `UserErrorSink` stores at most 200 sanitized UI notifications and `DiagnosticsViewModel` projects them without exposing stack traces by default.

**Tech Stack:** .NET 10, WPF, Microsoft.Extensions.Logging, CommunityToolkit.Mvvm, xUnit, Shouldly.

---

## Scope Check

This plan owns App diagnostics only. Core result JSON Lines remain owned by the resumability plan.
It assumes prerequisites composition, run controls, and post-run summary are integrated.

**Working directory for every command:** `C:\Agent Projects\VideoTriage`

## Execution Corrections

- Register one singleton `DiagnosticsViewModel`, expose it from `MainViewModel`, and render the
  diagnostics view in the existing shell; a view that is not reachable does not satisfy the goal.
- After adding a run failure to `IUserErrorSink`, call `DiagnosticsViewModel.Refresh()` so its
  snapshot properties notify the UI.
- Preserve the current `RunState` command state machine and post-run summary behavior. Add a
  `StatusMessage` property rather than replacing the established run-control model.
- Test composition with isolated temporary log paths; tests never write to the user's LocalAppData.

## File Structure

```text
src/VideoTriage.App/Services/UserErrorSeverity.cs          CREATE
src/VideoTriage.App/Services/UserError.cs                  CREATE
src/VideoTriage.App/Services/IUserErrorSink.cs             CREATE
src/VideoTriage.App/Services/UserErrorSink.cs              CREATE
src/VideoTriage.App/Services/RollingFileLogPath.cs         CREATE
src/VideoTriage.App/Services/RollingFileLoggerProvider.cs  CREATE
src/VideoTriage.App/Services/IAppLog.cs                    CREATE
src/VideoTriage.App/Services/AppLog.cs                     CREATE
src/VideoTriage.App/Services/ServiceCollectionExtensions.cs MODIFY - register logging
src/VideoTriage.App/ViewModels/DiagnosticsViewModel.cs     CREATE
src/VideoTriage.App/ViewModels/MainViewModel.cs            MODIFY - exception policy
src/VideoTriage.App/Views/DiagnosticsView.xaml             CREATE
src/VideoTriage.App/Views/DiagnosticsView.xaml.cs          CREATE
tests/VideoTriage.App.Tests/Services/UserErrorSinkTests.cs CREATE
tests/VideoTriage.App.Tests/Services/RollingFileLoggerProviderTests.cs CREATE
tests/VideoTriage.App.Tests/ViewModels/DiagnosticsViewModelTests.cs CREATE
tests/VideoTriage.App.Tests/ViewModels/MainViewModelDiagnosticsTests.cs CREATE
```

### Task 1: Add A Bounded User Error Sink

**Files:**
- Create: `src/VideoTriage.App/Services/UserErrorSeverity.cs`
- Create: `src/VideoTriage.App/Services/UserError.cs`
- Create: `src/VideoTriage.App/Services/IUserErrorSink.cs`
- Create: `src/VideoTriage.App/Services/UserErrorSink.cs`
- Create: `tests/VideoTriage.App.Tests/Services/UserErrorSinkTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/VideoTriage.App.Tests/Services/UserErrorSinkTests.cs`:

```csharp
using Shouldly;
using VideoTriage.App.Services;

namespace VideoTriage.App.Tests.Services;

public sealed class UserErrorSinkTests
{
    [Fact]
    public void Add_RecordsAllFields()
    {
        var now = new DateTimeOffset(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);
        var sink = new UserErrorSink(() => now);

        sink.Add(UserErrorSeverity.Error, "Run failed", "The file was not changed.", "boom");

        sink.Errors.ShouldBe([
            new UserError(now, UserErrorSeverity.Error, "Run failed",
                "The file was not changed.", "boom")
        ]);
    }

    [Fact]
    public void Add_MoreThanTwoHundred_KeepsNewestTwoHundred()
    {
        var sink = new UserErrorSink(() => DateTimeOffset.UnixEpoch);

        for (var index = 0; index < 205; index++)
        {
            sink.Add(UserErrorSeverity.Warning, $"title-{index}", $"message-{index}");
        }

        sink.Errors.Count.ShouldBe(200);
        sink.Errors[0].Title.ShouldBe("title-5");
        sink.Errors[^1].Title.ShouldBe("title-204");
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var sink = new UserErrorSink(() => DateTimeOffset.UnixEpoch);
        sink.Add(UserErrorSeverity.Info, "Ready", "Ready.");

        sink.Clear();

        sink.Errors.ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Run red**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter UserErrorSinkTests
```

Expected: build fails with `CS0234` or `CS0246` because the user-error types do not exist.

- [ ] **Step 3: Add the complete production types**

Create `src/VideoTriage.App/Services/UserErrorSeverity.cs`:

```csharp
namespace VideoTriage.App.Services;

public enum UserErrorSeverity
{
    Info,
    Warning,
    Error
}
```

Create `src/VideoTriage.App/Services/UserError.cs`:

```csharp
namespace VideoTriage.App.Services;

public sealed record UserError(
    DateTimeOffset Timestamp,
    UserErrorSeverity Severity,
    string Title,
    string Message,
    string? Detail);
```

Create `src/VideoTriage.App/Services/IUserErrorSink.cs`:

```csharp
namespace VideoTriage.App.Services;

public interface IUserErrorSink
{
    IReadOnlyList<UserError> Errors { get; }
    void Add(UserErrorSeverity severity, string title, string message, string? detail = null);
    void Clear();
}
```

Create `src/VideoTriage.App/Services/UserErrorSink.cs`:

```csharp
namespace VideoTriage.App.Services;

public sealed class UserErrorSink(Func<DateTimeOffset>? utcNow = null) : IUserErrorSink
{
    private const int Capacity = 200;
    private readonly object _gate = new();
    private readonly List<UserError> _errors = [];
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public IReadOnlyList<UserError> Errors
    {
        get
        {
            lock (_gate)
            {
                return _errors.ToArray();
            }
        }
    }

    public void Add(
        UserErrorSeverity severity,
        string title,
        string message,
        string? detail = null)
    {
        lock (_gate)
        {
            _errors.Add(new UserError(_utcNow(), severity, title, message, detail));
            if (_errors.Count > Capacity)
            {
                _errors.RemoveRange(0, _errors.Count - Capacity);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _errors.Clear();
        }
    }
}
```

- [ ] **Step 4: Run green**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter UserErrorSinkTests
```

Expected: `Passed!` and `Failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add src/VideoTriage.App/Services/UserErrorSeverity.cs src/VideoTriage.App/Services/UserError.cs src/VideoTriage.App/Services/IUserErrorSink.cs src/VideoTriage.App/Services/UserErrorSink.cs tests/VideoTriage.App.Tests/Services/UserErrorSinkTests.cs
git commit -m "feat(app): collect user-facing diagnostic errors"
```

Expected: commit succeeds.

### Task 2: Add The Daily Rolling File Provider

**Files:**
- Create: `src/VideoTriage.App/Services/RollingFileLogPath.cs`
- Create: `src/VideoTriage.App/Services/RollingFileLoggerProvider.cs`
- Create: `tests/VideoTriage.App.Tests/Services/RollingFileLoggerProviderTests.cs`

- [ ] **Step 1: Write the failing provider test**

Create `tests/VideoTriage.App.Tests/Services/RollingFileLoggerProviderTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Shouldly;
using VideoTriage.App.Services;

namespace VideoTriage.App.Tests.Services;

public sealed class RollingFileLoggerProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "VideoTriage.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Log_WritesTimestampLevelCategoryAndMessageToDailyFile()
    {
        var now = new DateTimeOffset(2026, 6, 7, 12, 30, 0, TimeSpan.Zero);
        var paths = new RollingFileLogPath(_directory, () => now);
        using var provider = new RollingFileLoggerProvider(paths, () => now);
        var logger = provider.CreateLogger("VideoTriage.Tests");

        logger.LogError(new InvalidOperationException("boom"), "Run {RunId} failed", 42);

        var text = File.ReadAllText(Path.Combine(_directory, "videotriage-20260607.log"));
        text.ShouldContain("2026-06-07T12:30:00.0000000+00:00");
        text.ShouldContain("Error");
        text.ShouldContain("VideoTriage.Tests");
        text.ShouldContain("Run 42 failed");
        text.ShouldContain("InvalidOperationException: boom");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run red**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter RollingFileLoggerProviderTests
```

Expected: build fails with `CS0246` because `RollingFileLogPath` and
`RollingFileLoggerProvider` do not exist.

- [ ] **Step 3: Add the path service**

Create `src/VideoTriage.App/Services/RollingFileLogPath.cs`:

```csharp
namespace VideoTriage.App.Services;

public sealed class RollingFileLogPath(
    string logDirectory,
    Func<DateTimeOffset>? utcNow = null)
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public string LogDirectory { get; } = logDirectory;

    public string CurrentLogPath =>
        Path.Combine(LogDirectory, $"videotriage-{_utcNow():yyyyMMdd}.log");
}
```

- [ ] **Step 4: Add the complete provider**

Create `src/VideoTriage.App/Services/RollingFileLoggerProvider.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace VideoTriage.App.Services;

public sealed class RollingFileLoggerProvider(
    RollingFileLogPath paths,
    Func<DateTimeOffset>? utcNow = null) : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public ILogger CreateLogger(string categoryName) =>
        new RollingFileLogger(categoryName, paths, _gate, _utcNow);

    public void Dispose()
    {
    }

    private sealed class RollingFileLogger(
        string category,
        RollingFileLogPath paths,
        object gate,
        Func<DateTimeOffset> utcNow) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var line = $"{utcNow():O} [{logLevel}] {category}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (gate)
            {
                Directory.CreateDirectory(paths.LogDirectory);
                File.AppendAllText(
                    paths.CurrentLogPath,
                    line + Environment.NewLine,
                    System.Text.Encoding.UTF8);
            }
        }
    }
}
```

- [ ] **Step 5: Run green**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter RollingFileLoggerProviderTests
```

Expected: `Passed!` and `Failed: 0`; the test removes its isolated log directory.

- [ ] **Step 6: Commit**

```powershell
git add src/VideoTriage.App/Services/RollingFileLogPath.cs src/VideoTriage.App/Services/RollingFileLoggerProvider.cs tests/VideoTriage.App.Tests/Services/RollingFileLoggerProviderTests.cs
git commit -m "feat(app): add daily rolling file logger"
```

Expected: commit succeeds.

### Task 3: Add The App Logging Facade And Register Diagnostics

**Files:**
- Create: `src/VideoTriage.App/Services/IAppLog.cs`
- Create: `src/VideoTriage.App/Services/AppLog.cs`
- Modify: `src/VideoTriage.App/Services/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Add the logging facade**

Create `src/VideoTriage.App/Services/IAppLog.cs`:

```csharp
namespace VideoTriage.App.Services;

public interface IAppLog
{
    string LogDirectory { get; }
    string CurrentLogPath { get; }
    void Information(string message);
    void Error(Exception exception, string message);
}
```

Create `src/VideoTriage.App/Services/AppLog.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace VideoTriage.App.Services;

public sealed class AppLog(
    ILogger<AppLog> logger,
    RollingFileLogPath paths) : IAppLog
{
    public string LogDirectory => paths.LogDirectory;
    public string CurrentLogPath => paths.CurrentLogPath;

    public void Information(string message) => logger.LogInformation("{Message}", message);

    public void Error(Exception exception, string message) =>
        logger.LogError(exception, "{Message}", message);
}
```

- [ ] **Step 2: Register logging in composition**

At the top of `ServiceCollectionExtensions.cs`, add:

```csharp
using Microsoft.Extensions.Logging;
```

Inside `AddVideoTriage`, before calling `AddVideoTriageForTests`, add:

```csharp
var logDirectory = Path.Combine(localAppData, "VideoTriage", "Logs");
services.AddSingleton(new RollingFileLogPath(logDirectory));
services.AddSingleton<ILoggerProvider, RollingFileLoggerProvider>();
services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information));
services.AddSingleton<IAppLog, AppLog>();
services.AddSingleton<IUserErrorSink, UserErrorSink>();
```

Inside `AddVideoTriageForTests`, add idempotent fallback registrations so composition tests remain
isolated:

```csharp
services.TryAddSingleton(new RollingFileLogPath(
    Path.Combine(dataDirectory, "Logs")));
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<ILoggerProvider, RollingFileLoggerProvider>());
services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information));
services.TryAddSingleton<IAppLog, AppLog>();
services.TryAddSingleton<IUserErrorSink, UserErrorSink>();
```

- [ ] **Step 3: Run the composition green gate**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter "RollingFileLoggerProviderTests|ServiceCollectionExtensionsTests"
```

Expected: `Passed!` and `Failed: 0`.

- [ ] **Step 4: Commit**

```powershell
git add src/VideoTriage.App/Services/IAppLog.cs src/VideoTriage.App/Services/AppLog.cs src/VideoTriage.App/Services/ServiceCollectionExtensions.cs
git commit -m "feat(app): register application diagnostics logging"
```

Expected: commit succeeds.

### Task 4: Project Diagnostics Into A ViewModel And View

**Files:**
- Create: `src/VideoTriage.App/ViewModels/DiagnosticsViewModel.cs`
- Create: `src/VideoTriage.App/Views/DiagnosticsView.xaml`
- Create: `src/VideoTriage.App/Views/DiagnosticsView.xaml.cs`
- Create: `tests/VideoTriage.App.Tests/ViewModels/DiagnosticsViewModelTests.cs`

- [ ] **Step 1: Write the failing ViewModel tests**

Create `tests/VideoTriage.App.Tests/ViewModels/DiagnosticsViewModelTests.cs`:

```csharp
using Shouldly;
using VideoTriage.App.Services;
using VideoTriage.App.ViewModels;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class DiagnosticsViewModelTests
{
    [Fact]
    public void Constructor_ProjectsLogPathCountAndLatestError()
    {
        var sink = new UserErrorSink(() => DateTimeOffset.UnixEpoch);
        sink.Add(UserErrorSeverity.Warning, "First", "one");
        sink.Add(UserErrorSeverity.Error, "Second", "two", "detail");
        var log = new FakeAppLog(@"C:\logs", @"C:\logs\videotriage-20260607.log");

        var viewModel = new DiagnosticsViewModel(sink, log);

        viewModel.LogPath.ShouldBe(@"C:\logs\videotriage-20260607.log");
        viewModel.ErrorCount.ShouldBe(2);
        viewModel.LatestError!.Title.ShouldBe("Second");
        viewModel.Errors.Count.ShouldBe(2);
    }

    [Fact]
    public void ClearCommand_ClearsSinkAndProjection()
    {
        var sink = new UserErrorSink(() => DateTimeOffset.UnixEpoch);
        sink.Add(UserErrorSeverity.Error, "Failure", "message");
        var viewModel = new DiagnosticsViewModel(
            sink, new FakeAppLog("logs", "logs\\today.log"));

        viewModel.ClearCommand.Execute(null);

        sink.Errors.ShouldBeEmpty();
        viewModel.ErrorCount.ShouldBe(0);
        viewModel.LatestError.ShouldBeNull();
    }

    private sealed record FakeAppLog(
        string LogDirectory,
        string CurrentLogPath) : IAppLog
    {
        public void Information(string message) { }
        public void Error(Exception exception, string message) { }
    }
}
```

- [ ] **Step 2: Run red**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter DiagnosticsViewModelTests
```

Expected: build fails with `CS0234` because `DiagnosticsViewModel` does not exist.

- [ ] **Step 3: Add the complete ViewModel**

Create `src/VideoTriage.App/ViewModels/DiagnosticsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoTriage.App.Services;

namespace VideoTriage.App.ViewModels;

public sealed partial class DiagnosticsViewModel(
    IUserErrorSink errorSink,
    IAppLog appLog) : ObservableObject
{
    public string LogPath => appLog.CurrentLogPath;
    public IReadOnlyList<UserError> Errors => errorSink.Errors;
    public int ErrorCount => Errors.Count;
    public UserError? LatestError => Errors.LastOrDefault();

    [RelayCommand]
    private void Clear()
    {
        errorSink.Clear();
        OnPropertyChanged(nameof(Errors));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(LatestError));
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(LogPath));
        OnPropertyChanged(nameof(Errors));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(LatestError));
    }
}
```

- [ ] **Step 4: Add the complete view**

Create `src/VideoTriage.App/Views/DiagnosticsView.xaml`:

```xml
<UserControl x:Class="VideoTriage.App.Views.DiagnosticsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <DockPanel>
            <TextBlock Text="Diagnostics" FontSize="24" FontWeight="SemiBold" />
            <Button DockPanel.Dock="Right"
                    Content="Clear"
                    Command="{Binding ClearCommand}"
                    Padding="14,6" />
        </DockPanel>

        <StackPanel Grid.Row="1" Margin="0,16,0,16">
            <TextBlock Text="Detailed log" FontWeight="SemiBold" />
            <TextBox Text="{Binding LogPath}"
                     IsReadOnly="True"
                     BorderThickness="0"
                     Background="Transparent" />
        </StackPanel>

        <ListBox Grid.Row="2" ItemsSource="{Binding Errors}">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Expander Margin="0,0,0,8">
                        <Expander.Header>
                            <StackPanel>
                                <TextBlock Text="{Binding Title}" FontWeight="SemiBold" />
                                <TextBlock Text="{Binding Message}" TextWrapping="Wrap" />
                            </StackPanel>
                        </Expander.Header>
                        <TextBox Text="{Binding Detail}"
                                 IsReadOnly="True"
                                 TextWrapping="Wrap"
                                 Visibility="{Binding Detail,
                                     Converter={StaticResource NullToVisibilityConverter}}" />
                    </Expander>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </Grid>
</UserControl>
```

If the integrated shell does not define `NullToVisibilityConverter`, replace only the `TextBox`
visibility binding with `Visibility="Visible"`; the detail remains collapsed inside the `Expander`
and no new converter is needed.

Create `src/VideoTriage.App/Views/DiagnosticsView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace VideoTriage.App.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 5: Run green**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter DiagnosticsViewModelTests
dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Debug
```

Expected: tests report `Failed: 0`; App build reports `0 Error(s)`.

- [ ] **Step 6: Commit**

```powershell
git add src/VideoTriage.App/ViewModels/DiagnosticsViewModel.cs src/VideoTriage.App/Views/DiagnosticsView.xaml src/VideoTriage.App/Views/DiagnosticsView.xaml.cs tests/VideoTriage.App.Tests/ViewModels/DiagnosticsViewModelTests.cs
git commit -m "feat(app): add diagnostics view and error projection"
```

Expected: commit succeeds.

### Task 5: Apply Exception Policy To Run Startup

**Files:**
- Modify: `src/VideoTriage.App/ViewModels/MainViewModel.cs`
- Create: `tests/VideoTriage.App.Tests/ViewModels/MainViewModelDiagnosticsTests.cs`

- [ ] **Step 1: Write the failing run-failure test**

Create `tests/VideoTriage.App.Tests/ViewModels/MainViewModelDiagnosticsTests.cs` using the existing
MainViewModel test fixture and fakes from the run-controls plan:

```csharp
[Fact]
public async Task StartAsync_PipelineThrows_LogsAddsFriendlyErrorAndResetsRunState()
{
    var pipeline = new ThrowingPipeline(new IOException("disk failed"));
    var appLog = new RecordingAppLog();
    var errors = new UserErrorSink(() => DateTimeOffset.UnixEpoch);
    var viewModel = CreateViewModel(pipeline, appLog, errors);
    viewModel.SelectedFolder = @"C:\Videos";

    await viewModel.StartCommand.ExecuteAsync(null);

    viewModel.IsRunning.ShouldBeFalse();
    appLog.Exceptions.Single().Message.ShouldBe("disk failed");
    errors.Errors.Single().Title.ShouldBe("Run failed");
    errors.Errors.Single().Message.ShouldContain("original files were left unchanged");
    errors.Errors.Single().Message.ShouldContain(appLog.CurrentLogPath);
}
```

The test fixture's `ThrowingPipeline` must throw from `RunAsync`; `RecordingAppLog` must record the
exception passed to `Error`.

- [ ] **Step 2: Run red**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter StartAsync_PipelineThrows_LogsAddsFriendlyErrorAndResetsRunState
```

Expected: test fails because the exception escapes or no user error is recorded.

- [ ] **Step 3: Inject diagnostics dependencies**

Add these constructor dependencies and fields to `MainViewModel`:

```csharp
private readonly IAppLog _appLog;
private readonly IUserErrorSink _userErrors;
```

Assign them from constructor parameters:

```csharp
IAppLog appLog,
IUserErrorSink userErrors
```

- [ ] **Step 4: Replace the run command's exception boundary**

Keep the integrated run-controls command setup, progress handling, cancellation handling, and summary
navigation. Wrap the existing `RunAsync` call with this exact boundary:

```csharp
IsRunning = true;
try
{
    var summary = await _pipeline.RunAsync(
        SelectedFolder,
        _settings.ToTriageOptions(),
        Recursive,
        _progress,
        _pauseToken,
        _runCancellation.Token);

    LastSummary = new SummaryViewModel(summary);
    ShowSummary();
}
catch (OperationCanceledException)
{
    StatusMessage = "Run stopped. Original files were left unchanged.";
}
catch (Exception exception)
{
    _appLog.Error(exception, $"Video triage failed for '{SelectedFolder}'.");
    _userErrors.Add(
        UserErrorSeverity.Error,
        "Run failed",
        $"VideoTriage could not finish the run. The original files were left unchanged. " +
        $"Details: {_appLog.CurrentLogPath}",
        exception.Message);
    StatusMessage = "Run failed. See Diagnostics for details.";
}
finally
{
    IsRunning = false;
    IsPaused = false;
    _runCancellation?.Dispose();
    _runCancellation = null;
    StartCommand.NotifyCanExecuteChanged();
    PauseCommand.NotifyCanExecuteChanged();
    ResumeCommand.NotifyCanExecuteChanged();
    StopCommand.NotifyCanExecuteChanged();
}
```

Use the already-integrated field names if the run-controls plan generated equivalent names. Do not
change the policy: cancellation is informational, exceptions are logged, the UI error is concise,
and `finally` always resets command state.

- [ ] **Step 5: Run green and the full gate**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter "MainViewModelDiagnosticsTests|DiagnosticsViewModelTests|UserErrorSinkTests"
dotnet build VideoTriage.sln -c Release
dotnet test VideoTriage.sln -c Release --no-build
```

Expected: selected tests and full suite report `Failed: 0`; build reports `0 Error(s)`.

- [ ] **Step 6: Commit**

```powershell
git add src/VideoTriage.App/ViewModels/MainViewModel.cs tests/VideoTriage.App.Tests/ViewModels/MainViewModelDiagnosticsTests.cs
git commit -m "feat(app): report run failures through diagnostics"
```

Expected: commit succeeds.

## Self-Review

### Spec Coverage

- Logs are written under `%LocalAppData%\VideoTriage\Logs` and roll by UTC date.
- Full exception details go to logs; UI messages are short and include the current log path.
- The error sink is thread-safe, snapshot-based, clearable, and capped at 200 entries.
- Details are behind an expander; raw stack traces are never placed in the user-error sink.
- Cancellation is not logged as an error and exceptions cannot leave `IsRunning` stuck.

### Placeholder And Type Scan

- Literal tests, red failures, complete production code, green commands, and commits are present.
- The one integration instruction in `MainViewModel` preserves existing run-control names while
  specifying the entire required exception boundary and observable behavior.
- No third-party logging package is introduced.

## Execution Handoff

Execute on `feature/logging-diagnostics` from updated `main` after post-run summary is integrated.
Use `superpowers:subagent-driven-development`; reviewers must verify log-path disclosure, bounded
UI errors, and run-state reset before merge.
