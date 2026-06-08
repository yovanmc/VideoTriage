# Run Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Connect Start, Pause, Resume, and Stop to the real pipeline with correct command state, progress updates, and cancellation cleanup.

**Architecture:** `MainViewModel` receives `ITriagePipelineProvider`, owns one run CTS and `PauseToken`, and disables Start when the provider has no pipeline. Pipeline progress updates existing queue rows on the UI dispatcher; command predicates derive from a single `RunState` enum.

**Tech Stack:** CommunityToolkit.Mvvm async commands, cancellation tokens, xUnit, Shouldly.

> **Reconciliation with the folder-scan-queue-ui plan (canonical `FileItemViewModel`).** That
> earlier plan already created `FileItemViewModel` with a single constructor
> `FileItemViewModel(string filePath)` and an **immutable** `public string FilePath { get; }`
> (set once at construction during the scan phase). This plan does **not** reintroduce a
> parameterless constructor or a settable `FilePath`. Rows are always created with the string
> constructor; this plan only *adds* the `Apply(FileProgress)` method, which updates status/progress
> text and never reassigns `FilePath`/`FileName`. All tests below construct rows as
> `new FileItemViewModel(@"C:\Videos\clip.mp4")` so the row identity matches the incoming
> `FileProgress.FilePath`. `MainViewModel` locates the row to update by matching
> `FileProgress.FilePath` to an existing `Items[i].FilePath`.

---

## Scope Check

This plan controls an existing pipeline and queue. It does not add settings persistence or summary UI.

## Execution Corrections

These corrections are authoritative where task snippets below differ from current `main`:

- `MainViewModel` already has folder scanning dependencies. Add run-control dependencies after the
  existing constructor parameters: `ITriagePipelineProvider` and `Func<TriageOptions>`. Keep existing
  scan tests working by allowing tests to pass these explicitly where run commands are exercised.
- `SelectedFolder` is currently set only by folder selection. For run-control command state and
  tests, make its setter public and notify `StartCommand` when it changes. Do not bypass the dialog
  in production behavior.
- `IFolderProbeScanner.ScanAsync` returns `Task<IReadOnlyList<ProbeResult>>` and accepts
  `(string folderPath, TriageOptions? options = null, bool recursive = false,
  IProgress<ProbeResult>? progress = null, CancellationToken cancellationToken = default)`. Any
  no-op scanner fake must match this exact signature.
- Use the same deterministic dispatch pattern introduced by the queue UI milestone:
  `InlineProgress<FileProgress>` calling `_dispatcher.Post(...)`. Do not use `Progress<T>`, whose
  callbacks can run after command tasks complete.
- Format all progress and saved-percent text with `CultureInfo.InvariantCulture`.
- Task 1 green command should run `FileItemViewModelProgressTests`, not `MainViewModelRunTests`.
- Update `ServiceCollectionExtensions` so the registered `MainViewModel` receives the real
  `ITriagePipelineProvider` and `Func<TriageOptions>`.

## File Structure

```text
src/VideoTriage.App/ViewModels/
  RunState.cs
  MainViewModel.cs
  FileItemViewModel.cs
src/VideoTriage.App/Views/MainWindow.xaml
tests/VideoTriage.App.Tests/
  Fakes/FakeTriagePipeline.cs
  ViewModels/MainViewModelRunTests.cs
```

### Task 1: Map Pipeline Progress To Rows

**Files:**
- Modify: `FileItemViewModel.cs`
- Create: `MainViewModelRunTests.cs`

- [ ] **Step 1: Write failing phase tests**

Create `tests/VideoTriage.App.Tests/ViewModels/FileItemViewModelProgressTests.cs`:

```csharp
using Shouldly;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class FileItemViewModelProgressTests
{
    [Fact]
    public void Apply_EncodingProgress_ShowsPercent()
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mp4",
            Phase = TriagePhase.Encoding,
            EncodeProgress = 0.43
        });

        vm.StatusText.ShouldBe("Encoding 43%");
        vm.Progress.ShouldBe(43);
    }

    [Theory]
    [InlineData(TriagePhase.Verifying, "Verifying output")]
    [InlineData(TriagePhase.EmbeddingPoster, "Embedding poster")]
    [InlineData(TriagePhase.Replacing, "Replacing original")]
    public void Apply_ActivePhase_ShowsPhaseText(TriagePhase phase, string expected)
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");

        vm.Apply(new FileProgress { FilePath = @"C:\Videos\clip.mp4", Phase = phase });

        vm.StatusText.ShouldBe(expected);
    }

    [Theory]
    [InlineData(TriageOutcome.OutputInvalid, "Verification failed; original kept")]
    [InlineData(TriageOutcome.GrewKeptOriginal, "Encode grew; original kept")]
    [InlineData(TriageOutcome.Cancelled, "Cancelled; original kept")]
    [InlineData(TriageOutcome.ReplacePartial, "Saved as recoverable partial")]
    public void Apply_TerminalOutcome_ShowsSafetyText(TriageOutcome outcome, string expected)
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mp4",
            Phase = TriagePhase.Done,
            Outcome = outcome,
            FinalPath = @"C:\Videos\clip.mp4"
        });

        vm.StatusText.ShouldBe(expected);
    }

    [Fact]
    public void Apply_Replaced_ShowsSavedPercentAndFinalPath()
    {
        var vm = new FileItemViewModel(@"C:\Videos\clip.mov");

        vm.Apply(new FileProgress
        {
            FilePath = @"C:\Videos\clip.mov",
            Phase = TriagePhase.Done,
            Outcome = TriageOutcome.Replaced,
            SavedPercent = 68.7,
            FinalPath = @"C:\Videos\clip.mp4"
        });

        vm.StatusText.ShouldBe("Saved 68.7%");
        vm.SavedText.ShouldContain(@"C:\Videos\clip.mp4");
    }
}
```

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.App.Tests -c Debug --filter FileItemViewModelProgressTests`

Expected: `Apply(FileProgress)` missing.

- [ ] **Step 3: Implement exact mapping**

Add this method to `FileItemViewModel`:

```csharp
public void Apply(FileProgress progressEvent)
{
    // FilePath/FileName are immutable (set by the constructor during the scan phase) — do not
    // reassign them here. The caller has already matched this row to progressEvent.FilePath.
    if (progressEvent.EncodeProgress.HasValue)
    {
        Progress = Math.Round(progressEvent.EncodeProgress.Value * 100, 1);
    }

    StatusText = progressEvent.Phase switch
    {
        TriagePhase.Encoding => $"Encoding {Progress:0.#}%",
        TriagePhase.Verifying => "Verifying output",
        TriagePhase.EmbeddingPoster => "Embedding poster",
        TriagePhase.Replacing => "Replacing original",
        TriagePhase.Done => DoneText(progressEvent),
        _ => progressEvent.Phase.ToString()
    };

    if (!string.IsNullOrWhiteSpace(progressEvent.FinalPath))
    {
        SavedText = progressEvent.FinalPath;
    }
}

private static string DoneText(FileProgress progressEvent) =>
    progressEvent.Outcome switch
    {
        TriageOutcome.Replaced => $"Saved {progressEvent.SavedPercent:0.#}%",
        TriageOutcome.ReplacePartial => "Saved as recoverable partial",
        TriageOutcome.OutputInvalid => "Verification failed; original kept",
        TriageOutcome.GrewKeptOriginal => "Encode grew; original kept",
        TriageOutcome.Cancelled => "Cancelled; original kept",
        _ => progressEvent.Message ?? "Done"
    };
```

- [ ] **Step 4: Run green and commit**

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter MainViewModelRunTests
git add src/VideoTriage.App/ViewModels/FileItemViewModel.cs tests/VideoTriage.App.Tests/ViewModels
git commit -m "feat(app): map pipeline phases to queue state"
```

### Task 2: Implement Command State Machine

**Files:**
- Create: `src/VideoTriage.App/ViewModels/RunState.cs`
- Modify: `src/VideoTriage.App/ViewModels/MainViewModel.cs`
- Create: `tests/VideoTriage.App.Tests/Fakes/FakeTriagePipeline.cs`

- [ ] **Step 1: Write failing command tests**

```csharp
using Shouldly;
using VideoTriage.App.Tests.Fakes;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed partial class MainViewModelRunTests
{
    [Fact]
    public void StartCommand_NoFolder_CannotExecute()
    {
        var vm = MakeViewModel();
        vm.StartCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void StartCommand_MissingPrerequisites_CannotExecute()
    {
        var vm = MakeViewModel(pipeline: null);
        vm.SelectedFolder = @"C:\Videos";

        vm.StartCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task StartCommand_Progress_UpdatesExistingRowsOnDispatcher()
    {
        var pipeline = new FakeTriagePipeline([
            new FileProgress
            {
                FilePath = @"C:\Videos\clip.mp4",
                Phase = TriagePhase.Encoding,
                EncodeProgress = 0.25
            }
        ]);
        var dispatcher = new InlineUiDispatcher();
        var vm = MakeViewModel(pipeline, dispatcher);
        vm.SelectedFolder = @"C:\Videos";
        vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));

        await vm.StartCommand.ExecuteAsync(null);

        vm.Items[0].StatusText.ShouldBe("Encoding 25%");
        dispatcher.PostCount.ShouldBe(1);
        vm.RunState.ShouldBe(RunState.Idle);
    }

    [Fact]
    public async Task PauseAndResume_UpdatePauseTokenAndState()
    {
        var pipeline = new BlockingTriagePipeline();
        var vm = MakeViewModel(pipeline);
        vm.SelectedFolder = @"C:\Videos";

        var run = vm.StartCommand.ExecuteAsync(null);
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        vm.PauseCommand.Execute(null);
        vm.RunState.ShouldBe(RunState.Paused);
        pipeline.PauseToken!.IsPaused.ShouldBeTrue();

        vm.ResumeCommand.Execute(null);
        vm.RunState.ShouldBe(RunState.Running);
        pipeline.PauseToken!.IsPaused.ShouldBeFalse();

        vm.StopCommand.Execute(null);
        await run;
        vm.RunState.ShouldBe(RunState.Idle);
    }
}
```

The tests above depend on test doubles and a builder that this plan MUST define (they are not
provided by any earlier plan). Create them alongside the tests:

```csharp
// tests/VideoTriage.App.Tests/Fakes/InlineUiDispatcher.cs
// Runs posted actions synchronously so progress is observable without a real Dispatcher.
public sealed class InlineUiDispatcher : IUiDispatcher
{
    public int PostCount { get; private set; }
    public void Post(Action action) { PostCount++; action(); }
}

// tests/VideoTriage.App.Tests/Fakes/FakeTriagePipeline.cs
public sealed class FakeTriagePipeline(IReadOnlyList<FileProgress> events) : ITriagePipeline
{
    public async Task<TriageSummary> RunAsync(string folder, TriageOptions options,
        bool recursive = false, IProgress<FileProgress>? progress = null,
        PauseToken? pauseToken = null, CancellationToken cancellationToken = default)
    {
        foreach (var e in events) { cancellationToken.ThrowIfCancellationRequested(); progress?.Report(e); }
        await Task.Yield();
        return EmptySummary.With(replaced: 0);
    }
}

// tests/VideoTriage.App.Tests/Fakes/BlockingTriagePipeline.cs
// Signals Started, captures the PauseToken, and blocks until cancelled so Pause/Resume/Stop
// can be exercised against a live run.
public sealed class BlockingTriagePipeline : ITriagePipeline
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public PauseToken? PauseToken { get; private set; }

    public async Task<TriageSummary> RunAsync(string folder, TriageOptions options,
        bool recursive = false, IProgress<FileProgress>? progress = null,
        PauseToken? pauseToken = null, CancellationToken cancellationToken = default)
    {
        PauseToken = pauseToken;
        Started.TrySetResult();
        try { await Task.Delay(Timeout.Infinite, cancellationToken); }
        catch (OperationCanceledException) { }
        return EmptySummary.With(replaced: 0);
    }
}
```

`MakeViewModel` is a partial-class helper on `MainViewModelRunTests`. A `null` pipeline models the
"prerequisites missing" case (`provider.Pipeline == null`); a non-null pipeline is wrapped in a stub
provider. `StubPipelineProvider` and `NoopFolderProbeScanner` are one-line test doubles:

```csharp
public sealed partial class MainViewModelRunTests
{
    private static MainViewModel MakeViewModel(
        ITriagePipeline? pipeline = null,   // null => prerequisites missing (Pipeline == null)
        IUiDispatcher? dispatcher = null)   // default: InlineUiDispatcher
        => new(
            scanner: new NoopFolderProbeScanner(),
            pipelineProvider: new StubPipelineProvider(pipeline),
            dispatcher: dispatcher ?? new InlineUiDispatcher(),
            optionsFactory: () => new TriageOptions());
}

public sealed class StubPipelineProvider(ITriagePipeline? pipeline) : ITriagePipelineProvider
{
    public ITriagePipeline? Pipeline { get; } = pipeline;
}

// Folder scanning is out of scope for run tests; this scanner does nothing.
public sealed class NoopFolderProbeScanner : IFolderProbeScanner
{
    public Task ScanAsync(string folder, bool recursive, IProgress<ProbeResult> progress,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
```

`EmptySummary.With(...)` is a tiny test helper returning a `TriageSummary` with all counts zero
except those passed; define it once under `tests/VideoTriage.App.Tests/Fakes/`. (Match the
`NoopFolderProbeScanner.ScanAsync` signature to the actual `IFolderProbeScanner` defined in the
folder-scan-queue-ui plan; adjust if that interface differs.)

> **Settings coupling.** The settings-persistence plan has not landed yet, so `MainViewModel` does
> **not** reference a concrete settings store here. Inject `Func<TriageOptions> optionsFactory`
> (default `() => new TriageOptions()`); the settings plan later replaces the registered factory with
> `() => settingsStore.Current.ToTriageOptions()`. The `StartAsync` body below calls
> `optionsFactory()` rather than `settings.ToTriageOptions()`.

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.App.Tests -c Debug --filter MainViewModelRunTests`

Expected: command/state members missing.

- [ ] **Step 3: Add state**

```csharp
namespace VideoTriage.App.ViewModels;
public enum RunState { Idle, Running, Paused, Stopping }
```

Use this implementation shape inside `MainViewModel`:

```csharp
[ObservableProperty] private RunState runState = RunState.Idle;
private CancellationTokenSource? runCts;
private PauseToken? pauseToken;
private readonly ITriagePipelineProvider pipelineProvider;

public IAsyncRelayCommand StartCommand { get; }
public IRelayCommand PauseCommand { get; }
public IRelayCommand ResumeCommand { get; }
public IRelayCommand StopCommand { get; }

private async Task StartAsync()
{
    runCts = new CancellationTokenSource();
    pauseToken = new PauseToken();
    RunState = RunState.Running;
    try
    {
        var progress = new Progress<FileProgress>(fp =>
            dispatcher.Post(() => ApplyProgress(fp)));
        var pipeline = pipelineProvider.Pipeline
            ?? throw new InvalidOperationException("Required video tools are unavailable.");
        // run-controls does not consume the summary; the post-run-summary plan promotes this to a
        // bound `LastSummary` property. Keep it local here so this plan compiles on its own.
        _ = await pipeline.RunAsync(
            SelectedFolder!,
            optionsFactory(),               // injected Func<TriageOptions>; settings plan swaps this later
            recursive: true,
            progress,
            pauseToken,
            runCts.Token);
    }
    catch (OperationCanceledException)
    {
        // Stop() cancelled the run; this is expected and not an error.
    }
    finally
    {
        RunState = RunState.Idle;
        runCts.Dispose();
        runCts = null;
        pauseToken = null;
        NotifyCommandState();
    }
}

// Matches an incoming progress event to an existing queue row by FilePath and updates it.
// Always invoked on the UI thread via dispatcher.Post(...).
private void ApplyProgress(FileProgress fp)
{
    foreach (var row in Items)
    {
        if (string.Equals(row.FilePath, Path.GetFullPath(fp.FilePath), StringComparison.OrdinalIgnoreCase))
        {
            row.Apply(fp);
            return;
        }
    }
}

// Re-evaluates CanExecute for all four commands after any state transition.
private void NotifyCommandState()
{
    StartCommand.NotifyCanExecuteChanged();
    PauseCommand.NotifyCanExecuteChanged();
    ResumeCommand.NotifyCanExecuteChanged();
    StopCommand.NotifyCanExecuteChanged();
}

private void Pause()
{
    pauseToken?.Pause();
    RunState = RunState.Paused;
    NotifyCommandState();
}

private void Resume()
{
    pauseToken?.Resume();
    RunState = RunState.Running;
    NotifyCommandState();
}

private void Stop()
{
    RunState = RunState.Stopping;
    runCts?.Cancel();
    NotifyCommandState();
}
```

- [ ] **Step 4: Run green**

Run: `dotnet test tests/VideoTriage.App.Tests -c Debug --filter MainViewModelRunTests`

Expected: all command tests pass.

- [ ] **Step 5: Bind controls and commit**

Bind the toolbar to the four commands. Show Pause while Running and Resume while Paused. Disable
Start when prerequisites are missing, no folder is selected, scanning is active, or state is not Idle.

```powershell
dotnet build VideoTriage.sln -c Release
dotnet test VideoTriage.sln -c Release --no-build
git add src/VideoTriage.App tests/VideoTriage.App.Tests
git commit -m "feat(app): add start pause resume and stop controls"
```

## Self-Review

- Stop is cancellation, not process termination from the UI.
- `finally` always returns commands to a usable state.
- Progress updates existing rows by normalized path.
- No UI code performs destructive filesystem actions.

## Execution Handoff

Execute on `feature/run-controls` after the folder queue UI is integrated.
