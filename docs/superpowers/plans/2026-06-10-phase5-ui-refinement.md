# Phase 5 — UI Refinement & Summary Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the run experience honest and legible — consistent toolbar, a summary that shows what actually happened to each candidate (with sizes/thumbnails), real moving encode progress, no silently-dropped outcomes — plus three behavioral bug fixes.

**Architecture:** Mostly App-layer WPF/MVVM. Two small `VideoTriage.Core` additions (run timing on `TriageSummary`, ETA on `FileProgress`, multi-line HandBrake progress parsing). A shared `TriageOutcomeDisplay` helper is the single source of truth for outcome labels/colors/grouping, consumed by both queue and summary. No change to the safety engine.

**Tech Stack:** .NET 10, WPF, WPF-UI (Fluent/Mica), CommunityToolkit.Mvvm, xUnit + Shouldly.

**Spec:** `docs/superpowers/specs/2026-06-10-phase5-ui-refinement-design.md` (read §0 outcome taxonomy first).

**Conventions for every task:**
- Build the App project (not the solution — the `.wapproj` packaging project needs an SDK that isn't installed): `dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Debug -warnaserror`
- Run Core tests: `dotnet test tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj --filter "<name>"`
- Run App tests: `dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "<name>"`
- Commit trailers on every commit:
  ```
  Co-authored-by: Codex <noreply@openai.com>
  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  ```

---

## File map

**Core (create):**
- `src/VideoTriage.Core/Encoding/HandBrakeProgressAccumulator.cs` — stateful multi-line JSON progress reader.

**Core (modify):**
- `src/VideoTriage.Core/Encoding/HandBrakeProgressParser.cs` — parse one complete JSON object → progress + ETA.
- `src/VideoTriage.Core/Encoding/HandBrakeEncoder.cs` — feed lines into accumulator.
- `src/VideoTriage.Core/Models/PipelineModels.cs` — `FileProgress.EtaSeconds`; `TriageSummary.StartedAtUtc`/`CompletedAtUtc`.
- `src/VideoTriage.Core/Pipeline/TriagePipeline.cs` — record start/complete time; pass ETA through.

**App (create):**
- `src/VideoTriage.App/ViewModels/TriageOutcomeDisplay.cs` — labels/colors/grouping + `IsProcessed`.

**App (modify):**
- `src/VideoTriage.App/ViewModels/FileItemViewModel.cs` — `DoneText` via helper.
- `src/VideoTriage.App/ViewModels/SummaryFileResult.cs` — expanded row record.
- `src/VideoTriage.App/ViewModels/SummaryViewModel.cs` — filter, tiles, legend, timing, severity, reveal.
- `src/VideoTriage.App/ViewModels/SettingsViewModel.cs` — auto-apply + per-field validation.
- `src/VideoTriage.App/ViewModels/MainViewModel.cs` — StartBlockedReason, InterruptedRunNotice, back-to-queue rescan, overall progress/ETA, open-log, queue header, status severity, pass thumbnails+launcher to summary.
- `src/VideoTriage.App/Views/MainWindow.xaml` — toolbar, sidebar (remove diagnostics, slim preset), queue header, recovery banner, status bar, Start tooltip.
- `src/VideoTriage.App/Views/MainWindow.xaml.cs` — deferred close.
- `src/VideoTriage.App/Views/SummaryView.xaml` — table B, donut legend, tiles, timing.
- `src/VideoTriage.App/Views/SettingsView.xaml` — per-field validation, remove Save button.
- `src/VideoTriage.App/Services/ServiceCollectionExtensions.cs` — inject `IExplorerLauncher` into `MainViewModel`; drop `DiagnosticsViewModel` wiring if orphaned.

**App (delete):**
- `src/VideoTriage.App/Views/DiagnosticsView.xaml` (+ `.cs`) — removed from UI.

---

## Task 1: Multi-line HandBrake progress parser (+ ETA)

Fixes the stuck-progress bug. HandBrake `--json` emits pretty-printed objects with trailing commas across many lines.

**Files:**
- Modify: `src/VideoTriage.Core/Encoding/HandBrakeProgressParser.cs`
- Create: `src/VideoTriage.Core/Encoding/HandBrakeProgressAccumulator.cs`
- Test: `tests/VideoTriage.Core.Tests/Encoding/HandBrakeProgressParserTests.cs` (existing — extend)
- Test: `tests/VideoTriage.Core.Tests/Encoding/HandBrakeProgressAccumulatorTests.cs` (create)

- [ ] **Step 1: Write failing test for complete-object parsing with trailing commas + ETA**

Create `tests/VideoTriage.Core.Tests/Encoding/HandBrakeProgressAccumulatorTests.cs`:

```csharp
using Shouldly;
using VideoTriage.Core.Encoding;

namespace VideoTriage.Core.Tests.Encoding;

public sealed class HandBrakeProgressAccumulatorTests
{
    private static readonly string[] WorkingObject =
    [
        "Progress: {",
        "    \"State\": \"WORKING\",",
        "    \"Working\": {",
        "        \"Progress\": 0.42,",
        "        \"ETASeconds\": 87,",
        "    }",
        "}",
    ];

    [Fact]
    public void Accumulate_MultiLineWorkingObject_EmitsProgressAndEta()
    {
        var acc = new HandBrakeProgressAccumulator();
        HandBrakeProgress? emitted = null;
        foreach (var line in WorkingObject)
        {
            var r = acc.Append(line);
            if (r is not null) emitted = r;
        }

        emitted.ShouldNotBeNull();
        emitted!.Progress.ShouldBe(0.42, 0.0001);
        emitted.EtaSeconds.ShouldBe(87);
    }

    [Fact]
    public void Accumulate_NonWorkingObjects_EmitNothing()
    {
        var acc = new HandBrakeProgressAccumulator();
        string[] noise =
        [
            "Version: {", "    \"Version\": {", "    },", "}",
            "Progress: {", "    \"Muxing\": { \"Progress\": 0.0 },", "    \"State\": \"MUXING\"", "}",
            "Progress: {", "    \"State\": \"WORKDONE\",", "    \"WorkDone\": {", "    }", "}",
        ];
        var got = noise.Select(acc.Append).Where(x => x is not null).ToList();
        got.ShouldBeEmpty();
    }

    [Fact]
    public void Accumulate_ProgressClampedToUnitInterval()
    {
        var acc = new HandBrakeProgressAccumulator();
        HandBrakeProgress? emitted = null;
        foreach (var line in new[] { "Progress: {", "\"Working\": { \"Progress\": 1.5 }", "}" })
            emitted = acc.Append(line) ?? emitted;
        emitted!.Progress.ShouldBe(1.0);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

`dotnet test tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj --filter "HandBrakeProgressAccumulator"`
Expected: FAIL — `HandBrakeProgressAccumulator` / `HandBrakeProgress` do not exist.

- [ ] **Step 3: Rewrite the parser to parse a complete JSON string, and add the result type**

Replace `src/VideoTriage.Core/Encoding/HandBrakeProgressParser.cs`:

```csharp
using System.Text.Json;

namespace VideoTriage.Core.Encoding;

public sealed record HandBrakeProgress(double Progress, int? EtaSeconds);

public static class HandBrakeProgressParser
{
    private static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = true };

    /// <summary>Parses one complete HandBrake --json object. Returns null unless it is a WORKING progress object.</summary>
    public static HandBrakeProgress? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json, Options);
            if (!document.RootElement.TryGetProperty("Working", out var working) ||
                !working.TryGetProperty("Progress", out var progress) ||
                !progress.TryGetDouble(out var value))
            {
                return null;
            }

            int? eta = null;
            if (working.TryGetProperty("ETASeconds", out var etaEl) &&
                etaEl.TryGetInt32(out var etaValue) && etaValue >= 0)
            {
                eta = etaValue;
            }

            return new HandBrakeProgress(Math.Clamp(value, 0, 1), eta);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Create the accumulator**

Create `src/VideoTriage.Core/Encoding/HandBrakeProgressAccumulator.cs`:

```csharp
using System.Text;

namespace VideoTriage.Core.Encoding;

/// <summary>
/// Feeds HandBrakeCLI --json stdout/stderr lines and emits a <see cref="HandBrakeProgress"/>
/// when a complete top-level JSON object has been seen. HandBrake pretty-prints objects across
/// many lines (with trailing commas), so a single line never contains a whole object.
/// </summary>
public sealed class HandBrakeProgressAccumulator
{
    private const int MaxBufferedChars = 64 * 1024; // guard against a never-closing object
    private readonly StringBuilder _buffer = new();
    private int _depth;
    private bool _capturing;

    /// <summary>Appends a line; returns parsed progress when an object completes, else null.</summary>
    public HandBrakeProgress? Append(string? line)
    {
        if (line is null) return null;

        foreach (var ch in line)
        {
            if (ch == '{')
            {
                _capturing = true;
                _depth++;
            }

            if (_capturing)
                _buffer.Append(ch);

            if (ch == '}' && _capturing)
            {
                _depth--;
                if (_depth == 0)
                {
                    var json = _buffer.ToString();
                    _buffer.Clear();
                    _capturing = false;
                    return HandBrakeProgressParser.TryParse(json);
                }
            }
        }

        if (_capturing)
        {
            _buffer.Append('\n');
            if (_buffer.Length > MaxBufferedChars)
            {
                _buffer.Clear();
                _depth = 0;
                _capturing = false;
            }
        }

        return null;
    }
}
```

- [ ] **Step 5: Update the existing parser tests to the new API**

Open `tests/VideoTriage.Core.Tests/Encoding/HandBrakeProgressParserTests.cs`. Replace any call to the old `TryParseProgress(line)` with `TryParse(completeJson)` returning `HandBrakeProgress?`. Example replacement test body:

```csharp
[Fact]
public void TryParse_WorkingProgress_ReturnsValue()
{
    var json = "{ \"State\": \"WORKING\", \"Working\": { \"Progress\": 0.5 } }";
    HandBrakeProgressParser.TryParse(json)!.Progress.ShouldBe(0.5);
}

[Fact]
public void TryParse_NonWorking_ReturnsNull()
{
    HandBrakeProgressParser.TryParse("{ \"State\": \"MUXING\" }").ShouldBeNull();
}
```

- [ ] **Step 6: Run tests to verify they pass**

`dotnet test tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj --filter "HandBrakeProgress"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/VideoTriage.Core/Encoding/HandBrakeProgressParser.cs src/VideoTriage.Core/Encoding/HandBrakeProgressAccumulator.cs tests/VideoTriage.Core.Tests/Encoding/
git commit -m "fix(core): parse multi-line HandBrake JSON progress and ETA"
```

---

## Task 2: Wire accumulator into the encoder

**Files:**
- Modify: `src/VideoTriage.Core/Encoding/HandBrakeEncoder.cs`
- Test: `tests/VideoTriage.Core.Tests/Encoding/HandBrakeEncoderTests.cs` (existing — adjust if it asserts progress)

- [ ] **Step 1: Update the encoder to feed lines into one accumulator and report `Working.Progress`**

In `src/VideoTriage.Core/Encoding/HandBrakeEncoder.cs`, replace the `outputLines` construction (currently calls `HandBrakeProgressParser.TryParseProgress`) with a shared accumulator:

```csharp
var accumulator = new HandBrakeProgressAccumulator();
var outputLines = new InlineProgress<string>(line =>
{
    var update = accumulator.Append(line);
    if (update is not null)
        progress?.Report(update.Progress);
});
```

Leave the rest of `EncodeAsync` unchanged (both `StandardOutputLines` and `StandardErrorLines` point at this same `outputLines`).

- [ ] **Step 2: Build to verify it compiles**

`dotnet build src/VideoTriage.Core/VideoTriage.Core.csproj -c Debug -warnaserror`
Expected: 0 errors. (If `HandBrakeEncoderTests` referenced the old method, update it to the new API; the encoder progress is driven through `IProgress<double>` and tested at integration level.)

- [ ] **Step 3: Run encoding tests**

`dotnet test tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj --filter "HandBrakeEncoder"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/VideoTriage.Core/Encoding/HandBrakeEncoder.cs tests/VideoTriage.Core.Tests/Encoding/
git commit -m "fix(core): drive encoder progress from multi-line accumulator"
```

---

## Task 3: Add ETA + run timing to the models and pipeline

**Files:**
- Modify: `src/VideoTriage.Core/Models/PipelineModels.cs`
- Modify: `src/VideoTriage.Core/Pipeline/TriagePipeline.cs`
- Test: `tests/VideoTriage.Core.Tests/Pipeline/TriagePipelineTests.cs` (existing — add timing assertion)

- [ ] **Step 1: Write failing test for run timing on the summary**

Add to `tests/VideoTriage.Core.Tests/Pipeline/TriagePipelineTests.cs`:

```csharp
[Fact]
public async Task RunAsync_PopulatesRunTiming()
{
    var fakes = PipelineFakes.Candidate();
    var before = DateTimeOffset.UtcNow;

    var result = await fakes.Pipeline.RunAsync(@"C:\Videos", [PipelineFakes.FilePath], new TriageOptions());

    result.StartedAtUtc.ShouldBeGreaterThanOrEqualTo(before);
    result.CompletedAtUtc.ShouldBeGreaterThanOrEqualTo(result.StartedAtUtc);
}
```

- [ ] **Step 2: Run to verify it fails**

`dotnet test tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj --filter "RunAsync_PopulatesRunTiming"`
Expected: FAIL — `StartedAtUtc`/`CompletedAtUtc` do not exist.

- [ ] **Step 3: Add the model fields**

In `src/VideoTriage.Core/Models/PipelineModels.cs`, add to `FileProgress` (after `FinalPath`):

```csharp
    public int? EtaSeconds { get; init; }
```

Add to `TriageSummary` (after `BytesSaved`, before `Files`):

```csharp
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
```

- [ ] **Step 4: Record timing in the pipeline**

In `src/VideoTriage.Core/Pipeline/TriagePipeline.cs`, capture a start time near the top of `RunAsync` (reuse the existing `startedAtUtc` if present, else add `var runStartedAtUtc = DateTimeOffset.UtcNow;` before the file loop). Where the method builds and returns the final `TriageSummary` (the `Summarize`/return site), add:

```csharp
StartedAtUtc = runStartedAtUtc,
CompletedAtUtc = DateTimeOffset.UtcNow,
```

If a `Summarize(...)` helper constructs the summary, thread `runStartedAtUtc` into it and set both fields there.

- [ ] **Step 5: Fix all `TriageSummary` construction sites and fakes**

Search the solution for `new TriageSummary` and `EmptySummary`. Add the two required fields everywhere:
- `tests/VideoTriage.App.Tests/Fakes/FakeTriagePipeline.cs` `EmptySummary()` → add `StartedAtUtc = DateTimeOffset.UtcNow, CompletedAtUtc = DateTimeOffset.UtcNow,`.
- Any other inline `new TriageSummary { ... }` in tests.

Run: `dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Debug -warnaserror` and fix each "required member not set" error the same way.

- [ ] **Step 6: Run tests to verify they pass**

`dotnet test tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj --filter "TriagePipeline"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/VideoTriage.Core/Models/PipelineModels.cs src/VideoTriage.Core/Pipeline/TriagePipeline.cs tests/
git commit -m "feat(core): add run timing to summary and ETA to file progress"
```

> Note: forwarding `EtaSeconds` from the encoder's progress callback into the per-file `FileProgress` (so the UI can show ETA) is wired in Task 11; the model field exists from here.

---

## Task 4: `TriageOutcomeDisplay` shared helper

Single source of truth for labels/colors/grouping. Used by queue rows (Task 5) and summary (Task 6).

**Files:**
- Create: `src/VideoTriage.App/ViewModels/TriageOutcomeDisplay.cs`
- Test: `tests/VideoTriage.App.Tests/ViewModels/TriageOutcomeDisplayTests.cs` (create)

- [ ] **Step 1: Write failing test covering every enum value**

Create `tests/VideoTriage.App.Tests/ViewModels/TriageOutcomeDisplayTests.cs`:

```csharp
using Shouldly;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class TriageOutcomeDisplayTests
{
    [Fact]
    public void Label_CoversEveryOutcome_NoBlanksNoEnumNames()
    {
        foreach (TriageOutcome o in Enum.GetValues<TriageOutcome>())
        {
            var label = TriageOutcomeDisplay.Label(o);
            label.ShouldNotBeNullOrWhiteSpace();
            label.ShouldNotBe(o.ToString()); // never the raw enum name
        }
    }

    [Theory]
    [InlineData(TriageOutcome.Replaced, true)]
    [InlineData(TriageOutcome.ReplacePartial, true)]
    [InlineData(TriageOutcome.GrewKeptOriginal, true)]
    [InlineData(TriageOutcome.EncodeFailed, true)]
    [InlineData(TriageOutcome.ReplaceFailed, true)]
    [InlineData(TriageOutcome.OutputInvalid, true)]
    [InlineData(TriageOutcome.InsufficientSpace, true)]
    [InlineData(TriageOutcome.Cancelled, true)]
    [InlineData(TriageOutcome.SkippedAlreadyAv1, false)]
    [InlineData(TriageOutcome.SkippedLowBpp, false)]
    [InlineData(TriageOutcome.InvalidMetadata, false)]
    [InlineData(TriageOutcome.DryRunCandidate, false)]
    [InlineData(TriageOutcome.AlreadyCompleted, false)]
    public void IsProcessed_PartitionsOutcomes(TriageOutcome o, bool processed) =>
        TriageOutcomeDisplay.IsProcessed(o).ShouldBe(processed);

    [Fact]
    public void GroupColor_IsAValidHexForProcessedOutcomes()
    {
        foreach (TriageOutcome o in Enum.GetValues<TriageOutcome>())
            if (TriageOutcomeDisplay.IsProcessed(o))
                TriageOutcomeDisplay.GroupColor(o).ShouldStartWith("#");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "TriageOutcomeDisplay"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement the helper**

Create `src/VideoTriage.App/ViewModels/TriageOutcomeDisplay.cs`:

```csharp
using VideoTriage.Core.Models;

namespace VideoTriage.App.ViewModels;

/// <summary>Single source of truth for how each <see cref="TriageOutcome"/> is shown.</summary>
public static class TriageOutcomeDisplay
{
    /// <summary>True when the file entered the encode pipeline (shown in the run summary).</summary>
    public static bool IsProcessed(TriageOutcome outcome) => outcome switch
    {
        TriageOutcome.Replaced or TriageOutcome.ReplacePartial
            or TriageOutcome.GrewKeptOriginal
            or TriageOutcome.OutputInvalid or TriageOutcome.EncodeFailed or TriageOutcome.ReplaceFailed
            or TriageOutcome.InsufficientSpace
            or TriageOutcome.Cancelled => true,
        _ => false,
    };

    public static string Label(TriageOutcome? outcome) => outcome switch
    {
        TriageOutcome.Replaced => "Replaced",
        TriageOutcome.ReplacePartial => "Replaced (recoverable partial)",
        TriageOutcome.GrewKeptOriginal => "Kept — encode was larger",
        TriageOutcome.OutputInvalid => "Verification failed — kept original",
        TriageOutcome.EncodeFailed => "Encode failed — kept original",
        TriageOutcome.ReplaceFailed => "Replace failed — kept original",
        TriageOutcome.InsufficientSpace => "Skipped — not enough free space",
        TriageOutcome.Cancelled => "Stopped",
        TriageOutcome.SkippedAlreadyAv1 => "Already AV1",
        TriageOutcome.SkippedLowBpp => "Below threshold",
        TriageOutcome.InvalidMetadata => "Couldn't read metadata",
        TriageOutcome.DryRunCandidate => "Would re-encode (dry run)",
        TriageOutcome.AlreadyCompleted => "Already processed",
        _ => "Done",
    };

    /// <summary>Coarse group used for the donut legend and status-bar severity.</summary>
    public static string GroupKey(TriageOutcome outcome) => outcome switch
    {
        TriageOutcome.Replaced or TriageOutcome.ReplacePartial => "Replaced",
        TriageOutcome.GrewKeptOriginal => "Kept larger",
        TriageOutcome.OutputInvalid or TriageOutcome.EncodeFailed or TriageOutcome.ReplaceFailed => "Failed",
        TriageOutcome.InsufficientSpace => "Low space",
        TriageOutcome.Cancelled => "Stopped",
        _ => "Other",
    };

    public static string GroupColor(TriageOutcome outcome) => GroupKey(outcome) switch
    {
        "Replaced" => "#36C98F",
        "Kept larger" => "#F5A524",
        "Failed" => "#F05252",
        "Low space" => "#5B8DEF",
        "Stopped" => "#8B93A7",
        _ => "#8B93A7",
    };

    /// <summary>True when an outcome should turn the status bar amber rather than green.</summary>
    public static bool IsWarning(TriageOutcome outcome) =>
        IsProcessed(outcome) && outcome is not (TriageOutcome.Replaced or TriageOutcome.ReplacePartial);
}
```

- [ ] **Step 4: Run to verify pass**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "TriageOutcomeDisplay"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/VideoTriage.App/ViewModels/TriageOutcomeDisplay.cs tests/VideoTriage.App.Tests/ViewModels/TriageOutcomeDisplayTests.cs
git commit -m "feat(app): add TriageOutcomeDisplay (labels, colors, processed partition)"
```

---

## Task 5: Complete `FileItemViewModel.DoneText` via the helper

**Files:**
- Modify: `src/VideoTriage.App/ViewModels/FileItemViewModel.cs`
- Test: `tests/VideoTriage.App.Tests/ViewModels/FileItemViewModelProgressTests.cs` (existing — extend)

- [ ] **Step 1: Write failing test that every processed outcome yields its helper label**

Add to `tests/VideoTriage.App.Tests/ViewModels/FileItemViewModelProgressTests.cs`:

```csharp
[Theory]
[InlineData(TriageOutcome.InsufficientSpace)]
[InlineData(TriageOutcome.EncodeFailed)]
[InlineData(TriageOutcome.OutputInvalid)]
public void Apply_Done_UsesOutcomeDisplayLabel(TriageOutcome outcome)
{
    var vm = new FileItemViewModel(@"C:\Videos\clip.mp4");
    vm.Apply(new FileProgress
    {
        FilePath = @"C:\Videos\clip.mp4",
        Phase = TriagePhase.Done,
        Outcome = outcome,
    });
    vm.StatusText.ShouldBe(TriageOutcomeDisplay.Label(outcome));
}
```

- [ ] **Step 2: Run to verify it fails**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "Apply_Done_UsesOutcomeDisplayLabel"`
Expected: FAIL — `InsufficientSpace`/`EncodeFailed` currently fall through to `Message`/blank.

- [ ] **Step 3: Replace `DoneText` to delegate to the helper**

In `src/VideoTriage.App/ViewModels/FileItemViewModel.cs`, replace the body of `DoneText` so the status label always comes from the helper. Keep the size-delta logic (`OldSizeText`/`SavedText`) in `Apply` unchanged. New `DoneText`:

```csharp
private static string DoneText(FileProgress progressEvent, double? computedSavedPct = null)
{
    if (progressEvent.Outcome is TriageOutcome.Replaced or TriageOutcome.ReplacePartial)
    {
        var pct = (computedSavedPct ?? progressEvent.SavedPercent ?? 0)
            .ToString("0.#", CultureInfo.InvariantCulture);
        return $"{TriageOutcomeDisplay.Label(progressEvent.Outcome)} · saved {pct}%";
    }

    return progressEvent.Outcome is { } o
        ? TriageOutcomeDisplay.Label(o)
        : progressEvent.Message ?? "Done";
}
```

Add `using VideoTriage.Core.Models;` if not present (it is). Confirm the existing `Apply_Replaced_*` tests still pass — the replaced label now reads "Replaced · saved 67.5%"; update those assertions to `vm.StatusText.ShouldContain("saved 67.5%")` if they hard-coded "Saved 67.5%".

- [ ] **Step 4: Run tests to verify pass**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "FileItemViewModel"`
Expected: PASS (fix any updated replaced-label assertions).

- [ ] **Step 5: Commit**

```bash
git add src/VideoTriage.App/ViewModels/FileItemViewModel.cs tests/VideoTriage.App.Tests/ViewModels/FileItemViewModelProgressTests.cs
git commit -m "feat(app): complete queue-row outcome coverage via TriageOutcomeDisplay"
```

---

## Task 6: Redesign `SummaryViewModel` + `SummaryFileResult`

Filter to processed; tiles; legend; timing; severity; reveal targets; thumbnails.

**Files:**
- Modify: `src/VideoTriage.App/ViewModels/SummaryFileResult.cs`
- Modify: `src/VideoTriage.App/ViewModels/SummaryViewModel.cs`
- Test: `tests/VideoTriage.App.Tests/ViewModels/SummaryViewModelTests.cs` (create)

- [ ] **Step 1: Write failing tests for filtering, reconciliation, severity, sizes**

Create `tests/VideoTriage.App.Tests/ViewModels/SummaryViewModelTests.cs`:

```csharp
using Shouldly;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class SummaryViewModelTests
{
    private static FileProgress Done(string path, TriageOutcome o, long src = 0, long? outBytes = null, double? saved = null) =>
        new()
        {
            FilePath = path,
            Phase = TriagePhase.Done,
            Outcome = o,
            Source = src == 0 ? null : new VideoStats
            {
                FilePath = path, CodecName = "h264", Width = 1920, Height = 1080,
                FramesPerSecond = 30, Duration = TimeSpan.FromMinutes(1), FileSizeBytes = src, HasAudio = true,
            },
            OutputBytes = outBytes,
            SavedPercent = saved,
        };

    private static TriageSummary Summary(params FileProgress[] files) => new()
    {
        Scanned = files.Length, Candidates = files.Length, Replaced = 0, Marginal = 0,
        Grew = 0, Invalid = 0, Failed = 0, Skipped = 0, BytesSaved = 0,
        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
        CompletedAtUtc = DateTimeOffset.UtcNow,
        Files = files,
    };

    [Fact]
    public void Files_ExcludeNonProcessedOutcomes()
    {
        var vm = new SummaryViewModel(Summary(
            Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50),
            Done(@"C:\b.mp4", TriageOutcome.SkippedAlreadyAv1),
            Done(@"C:\c.mp4", TriageOutcome.SkippedLowBpp),
            Done(@"C:\d.mp4", TriageOutcome.InsufficientSpace, 2000)));

        vm.Files.Select(f => f.FileName).ShouldBe(["a.mp4", "d.mp4"], ignoreOrder: true);
        vm.ProcessedCount.ShouldBe(2);
    }

    [Fact]
    public void Segments_ReconcileWithProcessedCount()
    {
        var vm = new SummaryViewModel(Summary(
            Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50),
            Done(@"C:\e.mp4", TriageOutcome.GrewKeptOriginal, 1000, 1100),
            Done(@"C:\f.mp4", TriageOutcome.EncodeFailed, 1000)));

        vm.Segments.Sum(s => s.Count).ShouldBe(vm.ProcessedCount);
        vm.Segments.Select(s => s.Label).ShouldContain("Replaced");
        vm.Segments.Select(s => s.Label).ShouldContain("Kept larger");
        vm.Segments.Select(s => s.Label).ShouldContain("Failed");
    }

    [Fact]
    public void Severity_IsWarning_WhenAnyNonReplacedProcessed()
    {
        var ok = new SummaryViewModel(Summary(Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50)));
        ok.Severity.ShouldBe(SummarySeverity.Success);

        var warn = new SummaryViewModel(Summary(
            Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50),
            Done(@"C:\g.mp4", TriageOutcome.InsufficientSpace, 2000)));
        warn.Severity.ShouldBe(SummarySeverity.Warning);
    }

    [Fact]
    public void ReplacedRow_HasSizeTransitionAndSaved()
    {
        var vm = new SummaryViewModel(Summary(Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50)));
        var row = vm.Files.Single();
        row.OldSizeText.ShouldNotBeNullOrEmpty();
        row.NewSizeText.ShouldNotBeNullOrEmpty();
        row.SavedText.ShouldContain("50");
        row.StatusLabel.ShouldBe("Replaced");
    }

    [Fact]
    public void DurationText_IsPresent()
    {
        var vm = new SummaryViewModel(Summary(Done(@"C:\a.mp4", TriageOutcome.Replaced, 1000, 500, 50)));
        vm.DurationText.ShouldNotBeNullOrWhiteSpace();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "SummaryViewModel"`
Expected: FAIL — new members don't exist.

- [ ] **Step 3: Expand `SummaryFileResult`**

Replace `src/VideoTriage.App/ViewModels/SummaryFileResult.cs`:

```csharp
using System.Windows.Media;

namespace VideoTriage.App.ViewModels;

public sealed record SummaryFileResult(
    string FileName,
    string FullPath,
    string StatusLabel,
    string StatusColor,
    string OldSizeText,
    string NewSizeText,
    string? SavedText,
    string? FinalPath,
    string RevealTargetPath,
    ImageSource? Thumbnail);
```

- [ ] **Step 4: Add the severity enum + rewrite `SummaryViewModel`**

Add `public enum SummarySeverity { None, Success, Warning }` (top of `SummaryViewModel.cs` namespace) and rewrite the class:

```csharp
using System.Globalization;
using System.Windows.Media;
using VideoTriage.Core.Formatting;
using VideoTriage.Core.Models;

namespace VideoTriage.App.ViewModels;

public enum SummarySeverity { None, Success, Warning }

public sealed class SummaryViewModel
{
    public SummaryViewModel(
        TriageSummary summary,
        IReadOnlyDictionary<string, ImageSource?>? thumbnails = null)
    {
        var processed = summary.Files
            .Where(f => f.Phase == TriagePhase.Done && f.Outcome is { } o && TriageOutcomeDisplay.IsProcessed(o))
            .ToArray();

        ProcessedCount = processed.Length;
        ReplacedCount = processed.Count(f => f.Outcome is TriageOutcome.Replaced or TriageOutcome.ReplacePartial);
        KeptOriginalCount = processed.Count(f => f.Outcome is TriageOutcome.GrewKeptOriginal);
        BytesSaved = summary.BytesSaved;
        BytesSavedText = HumanSize.Format(summary.BytesSaved);

        var totalSourceBytes = processed
            .Where(f => f.Outcome is TriageOutcome.Replaced or TriageOutcome.ReplacePartial)
            .Sum(f => f.Source?.FileSizeBytes ?? 0);
        var reductionPercent = totalSourceBytes == 0 ? 0 : 100d * summary.BytesSaved / totalSourceBytes;
        OverallReductionText = reductionPercent.ToString("0.0", CultureInfo.CurrentCulture) + "%";

        var duration = summary.CompletedAtUtc - summary.StartedAtUtc;
        CompletedAtText = summary.CompletedAtUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
        DurationText = duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s"
            : duration.TotalMinutes >= 1
                ? $"{duration.Minutes}m {duration.Seconds}s"
                : $"{duration.Seconds}s";

        Segments = processed
            .GroupBy(f => TriageOutcomeDisplay.GroupKey(f.Outcome!.Value))
            .Select(g => new SummarySegment(g.Key, g.Count(), TriageOutcomeDisplay.GroupColor(g.First().Outcome!.Value)))
            .ToArray();

        Severity = ProcessedCount == 0
            ? SummarySeverity.None
            : processed.Any(f => TriageOutcomeDisplay.IsWarning(f.Outcome!.Value))
                ? SummarySeverity.Warning
                : SummarySeverity.Success;

        Files = processed.Select(f =>
        {
            var outcome = f.Outcome!.Value;
            var isReplaced = outcome is TriageOutcome.Replaced or TriageOutcome.ReplacePartial;
            var oldBytes = f.Source?.FileSizeBytes;
            thumbnails?.TryGetValue(System.IO.Path.GetFullPath(f.FilePath), out var thumb);
            var reveal = !string.IsNullOrWhiteSpace(f.FinalPath) ? f.FinalPath! : f.FilePath;
            return new SummaryFileResult(
                FileName: System.IO.Path.GetFileName(f.FilePath),
                FullPath: f.FilePath,
                StatusLabel: TriageOutcomeDisplay.Label(outcome),
                StatusColor: TriageOutcomeDisplay.GroupColor(outcome),
                OldSizeText: oldBytes is { } ob ? HumanSize.Format(ob) : "",
                NewSizeText: isReplaced && f.OutputBytes is { } nb ? HumanSize.Format(nb) : "",
                SavedText: isReplaced && f.SavedPercent is { } sp
                    ? sp.ToString("0.0", CultureInfo.CurrentCulture) + "%"
                    : null,
                FinalPath: f.FinalPath,
                RevealTargetPath: reveal,
                Thumbnail: thumbnails is not null ? GetThumb(thumbnails, f.FilePath) : null);
        }).ToArray();
    }

    private static ImageSource? GetThumb(IReadOnlyDictionary<string, ImageSource?> thumbs, string path) =>
        thumbs.TryGetValue(System.IO.Path.GetFullPath(path), out var img) ? img : null;

    public int ProcessedCount { get; }
    public int ReplacedCount { get; }
    public int KeptOriginalCount { get; }
    public long BytesSaved { get; }
    public string BytesSavedText { get; }
    public string OverallReductionText { get; }
    public string CompletedAtText { get; }
    public string DurationText { get; }
    public SummarySeverity Severity { get; }
    public IReadOnlyList<SummarySegment> Segments { get; }
    public IReadOnlyList<SummaryFileResult> Files { get; }
}
```

(Remove the now-unused `ScannedCount`/`CandidateCount`/`KeptCount`/`AverageReduction*` members; update any references — see Task 12 for the view.)

- [ ] **Step 5: Run tests to verify pass**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "SummaryViewModel"`
Expected: PASS. Then build the App project and fix any references to removed members (`MainViewModelSummaryTests`, `SummaryView.xaml` bindings handled in Task 12).

- [ ] **Step 6: Commit**

```bash
git add src/VideoTriage.App/ViewModels/SummaryViewModel.cs src/VideoTriage.App/ViewModels/SummaryFileResult.cs tests/VideoTriage.App.Tests/ViewModels/SummaryViewModelTests.cs
git commit -m "feat(app): redesign SummaryViewModel (filter, tiles, legend, timing, severity)"
```

---

## Task 7: `SettingsViewModel` — auto-apply + per-field validation

**Files:**
- Modify: `src/VideoTriage.App/ViewModels/SettingsViewModel.cs`
- Test: `tests/VideoTriage.App.Tests/ViewModels/SettingsViewModelTests.cs` (existing — extend)

- [ ] **Step 1: Write failing tests for auto-save + per-field errors**

Add to `tests/VideoTriage.App.Tests/ViewModels/SettingsViewModelTests.cs` (use the existing fake store; if it records saves, assert on it — otherwise add a counting fake):

```csharp
[Fact]
public void ValidChange_PersistsImmediately_NoSaveCommandNeeded()
{
    var store = new CountingSettingsStore();
    var vm = new SettingsViewModel(store);
    vm.MinimumFreeGigabytes = 7;
    store.SaveCount.ShouldBeGreaterThanOrEqualTo(1);
    store.Last!.MinimumFreeGigabytes.ShouldBe(7);
}

[Fact]
public void InvalidChange_DoesNotPersist_AndFlagsFieldError()
{
    var store = new CountingSettingsStore();
    var vm = new SettingsViewModel(store);
    var saalesBefore = store.SaveCount;
    vm.CandidateBppThreshold = 5; // > 1, invalid
    ((System.ComponentModel.INotifyDataErrorInfo)vm).HasErrors.ShouldBeTrue();
    store.SaveCount.ShouldBe(saalesBefore);
}

private sealed class CountingSettingsStore : ISettingsStore
{
    public int SaveCount { get; private set; }
    public AppSettings? Last { get; private set; }
    public AppSettings Load() => new();
    public void Save(AppSettings settings) { SaveCount++; Last = settings; }
}
```

(Add `using VideoTriage.App.Models;` / `VideoTriage.App.Services;` as needed.)

- [ ] **Step 2: Run to verify it fails**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "SettingsViewModel"`
Expected: FAIL — no auto-save; no `INotifyDataErrorInfo`.

- [ ] **Step 3: Implement `INotifyDataErrorInfo` + auto-apply**

In `src/VideoTriage.App/ViewModels/SettingsViewModel.cs`: implement `INotifyDataErrorInfo`, validate the two numeric fields per-field, and persist on every valid change. Replace `SetValidatedProperty` and add error plumbing:

```csharp
// class declaration:
public sealed class SettingsViewModel : ObservableObject, System.ComponentModel.INotifyDataErrorInfo
{
    private readonly Dictionary<string, string> _errors = new();

    public event EventHandler<System.ComponentModel.DataErrorsChangedEventArgs>? ErrorsChanged;
    public bool HasErrors => _errors.Count > 0;
    public System.Collections.IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && _errors.TryGetValue(propertyName, out var e) ? new[] { e } : Array.Empty<string>();

    private void SetError(string property, string? message)
    {
        var had = _errors.ContainsKey(property);
        if (message is null)
        {
            if (had) { _errors.Remove(property); ErrorsChanged?.Invoke(this, new(property)); }
        }
        else if (!had || _errors[property] != message)
        {
            _errors[property] = message;
            ErrorsChanged?.Invoke(this, new(property));
        }
    }

    private void Validate()
    {
        SetError(nameof(CandidateBppThreshold),
            CandidateBppThreshold is <= 0 or > 1 ? "Must be greater than 0 and at most 1." : null);
        SetError(nameof(MinimumFreeGigabytes),
            MinimumFreeGigabytes < 1 ? "Must be at least 1 GB." : null);
    }
```

Replace `SetValidatedProperty` so it validates, recomputes gates, and **auto-persists when valid**:

```csharp
    private void SetValidatedProperty<T>(ref T field, T value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName)) return;
        Validate();
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanRun));
        if (CanSave) _store.Save(CurrentSettings());
    }
```

Remove the `SaveCommand`/`Save()` (now unused; the view's Save button is removed in Task 13). Keep `ValidationMessage`, `CanSave`, `CanRun` as-is (they still gate `Start`). `ConfirmPermanentDelete` stays uninvolved in `CurrentSettings()` (already true) — never persisted.

- [ ] **Step 4: Run tests to verify pass**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "SettingsViewModel"`
Expected: PASS. Build App; if anything referenced `SaveCommand`, remove it (Task 13 removes the XAML button).

- [ ] **Step 5: Commit**

```bash
git add src/VideoTriage.App/ViewModels/SettingsViewModel.cs tests/VideoTriage.App.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "feat(app): auto-apply settings and per-field validation"
```

---

## Task 8: `MainViewModel` — StartBlockedReason + Open-log + queue header + status severity

**Files:**
- Modify: `src/VideoTriage.App/ViewModels/MainViewModel.cs`
- Test: `tests/VideoTriage.App.Tests/ViewModels/MainViewModelRunTests.cs` (extend)

- [ ] **Step 1: Write failing tests**

Add to `tests/VideoTriage.App.Tests/ViewModels/MainViewModelRunTests.cs` (uses existing `MakeViewModel` helper):

```csharp
[Fact]
public void StartBlockedReason_NoFolder_ExplainsWhy()
{
    var vm = MakeViewModel(new FakeTriagePipeline([]));
    vm.StartBlockedReason.ShouldNotBeNullOrWhiteSpace();
}

[Fact]
public void StartBlockedReason_Ready_IsNull()
{
    var vm = MakeViewModel(new FakeTriagePipeline([]));
    vm.SelectedFolder = @"C:\Videos";
    vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));
    vm.StartBlockedReason.ShouldBeNull();
}

[Fact]
public void QueueSummaryText_ShowsCountAndTotalSize()
{
    var vm = MakeViewModel(new FakeTriagePipeline([]));
    vm.SelectedFolder = @"C:\Videos";
    vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));
    vm.QueueSummaryText.ShouldContain("1 candidate");
}
```

- [ ] **Step 2: Run to verify it fails**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "StartBlockedReason|QueueSummaryText"`
Expected: FAIL — members don't exist.

- [ ] **Step 3: Add `IExplorerLauncher` dependency + new members**

In `MainViewModel` constructor, add parameter `IExplorerLauncher? explorerLauncher = null` and store it (`_explorerLauncher`). Add an `OpenLogCommand` and the computed properties. After `QueueRemainingCount`:

```csharp
    private readonly IExplorerLauncher? _explorerLauncher;

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

    public IRelayCommand OpenLogCommand { get; }
```

Initialize in the constructor: `OpenLogCommand = new RelayCommand(OpenLog);` and add:

```csharp
    private void OpenLog()
    {
        var path = _appLog?.CurrentLogPath;
        if (!string.IsNullOrWhiteSpace(path))
            _explorerLauncher?.Open(path);
    }
```

Notify `StartBlockedReason`/`QueueSummaryText` whenever inputs change: in the `Items.CollectionChanged` handler and `SelectedFolder` setter and the `settings.PropertyChanged` handler, add `OnPropertyChanged(nameof(StartBlockedReason)); OnPropertyChanged(nameof(QueueSummaryText));`.

Add `SourceBytes` to `FileItemViewModel`: a public `long SourceBytes { get; private set; }` set inside `ApplyProbe` from `result.Stats.FileSizeBytes` (default 0). (Small edit to `FileItemViewModel`.)

- [ ] **Step 4: Run tests to verify pass**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "StartBlockedReason|QueueSummaryText"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/VideoTriage.App/ViewModels/MainViewModel.cs src/VideoTriage.App/ViewModels/FileItemViewModel.cs tests/VideoTriage.App.Tests/ViewModels/MainViewModelRunTests.cs
git commit -m "feat(app): Start-blocked reason, queue summary, open-log command"
```

---

## Task 9: `MainViewModel` — interrupted-run notice + back-to-queue rescan + status state + thumbnails into summary

**Files:**
- Modify: `src/VideoTriage.App/ViewModels/MainViewModel.cs`
- Test: `tests/VideoTriage.App.Tests/ViewModels/MainViewModelRunTests.cs` (extend)

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void DismissInterruptedNotice_ClearsIt()
{
    var vm = MakeViewModel(new FakeTriagePipeline([]));
    // simulate detection
    vm.GetType().GetProperty(nameof(vm.InterruptedRunNotice))!; // exists
    vm.InterruptedRunNotice.ShouldBeNull();
    vm.DismissInterruptedNoticeCommand.Execute(null); // no-op when null, must not throw
}

[Fact]
public async Task BackToQueue_RescansFolder()
{
    var scanner = new RecordingScanner();
    var vm = MakeViewModel(new FakeTriagePipeline([]), scanner: scanner);
    vm.SelectedFolder = @"C:\Videos";
    vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));
    await vm.StartCommand.ExecuteAsync(null);   // LastSummary set
    scanner.ScanCount = 0;
    vm.BackToQueueCommand.Execute(null);
    await Task.Delay(50);
    scanner.ScanCount.ShouldBeGreaterThanOrEqualTo(1);
}
```

Add a `RecordingScanner : IFolderProbeScanner` fake (increments `ScanCount`, returns an empty `FolderScanSummary`) if `MakeViewModel` doesn't already accept a scanner — extend `MakeViewModel` with an optional `scanner` parameter that defaults to a no-op scanner.

- [ ] **Step 2: Run to verify it fails**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "InterruptedNotice|BackToQueue_Rescans"`
Expected: FAIL.

- [ ] **Step 3: Implement notice + rescan + status state**

Add fields/members to `MainViewModel`:

```csharp
    private string? _interruptedRunNotice;
    public string? InterruptedRunNotice
    {
        get => _interruptedRunNotice;
        private set => SetProperty(ref _interruptedRunNotice, value);
    }
    public IRelayCommand DismissInterruptedNoticeCommand { get; }
```

Initialize `DismissInterruptedNoticeCommand = new RelayCommand(() => InterruptedRunNotice = null);`.

In `ChooseFolderAsync`, replace the existing `// TODO: surface in Diagnostics panel (Phase 4)` block: when `activeRun is not null`, set
```csharp
InterruptedRunNotice = msg;
```
(keep the existing `_appLog?.Information(msg);`).

Rewrite `BackToQueue` to re-scan current reality:
```csharp
    private void BackToQueue()
    {
        _lastRunDataDirectory = null;
        LastSummary = null;
        OpenDataDirectoryCommand.NotifyCanExecuteChanged();
        if (!string.IsNullOrWhiteSpace(SelectedFolder))
            _ = ChooseFolderRescanAsync(SelectedFolder!);
        else
            QueueRemainingCount = Items.Count;
    }
```
Add a small `ChooseFolderRescanAsync(folder)` that runs the same scan body as `ChooseFolderAsync` but without re-opening the folder dialog (extract the scan body of `ChooseFolderAsync` into a private `ScanFolderAsync(string folder)` and call it from both). This keeps the queue current after replacements.

Add a status-bar severity passthrough used by the view: expose `LastSummary.Severity` (already on the summary VM) — no new field needed; the view binds `LastSummary.Severity`.

- [ ] **Step 4: Pass queue thumbnails into the summary**

Where `StartAsync` builds the summary (`LastSummary = new SummaryViewModel(summary);`), build a thumbnail map from the queue and pass it:
```csharp
var thumbs = _queueIndex.ToDictionary(
    kv => kv.Key, kv => kv.Value.Thumbnail, StringComparer.OrdinalIgnoreCase);
LastSummary = new SummaryViewModel(summary, thumbs);
```

- [ ] **Step 5: Run tests to verify pass**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "MainViewModel"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/VideoTriage.App/ViewModels/MainViewModel.cs tests/VideoTriage.App.Tests/ViewModels/MainViewModelRunTests.cs
git commit -m "feat(app): interrupted-run notice, back-to-queue rescan, summary thumbnails"
```

---

## Task 10: `MainViewModel` — overall progress / ETA + forward per-file ETA

**Files:**
- Modify: `src/VideoTriage.App/ViewModels/MainViewModel.cs`
- Modify: `src/VideoTriage.Core/Pipeline/TriagePipeline.cs` (forward ETA into `FileProgress`)
- Test: `tests/VideoTriage.App.Tests/ViewModels/MainViewModelRunTests.cs` (extend)

- [ ] **Step 1: Write failing test for overall progress text**

```csharp
[Fact]
public async Task RunProgressText_ShowsCompletedOfTotalDuringRun()
{
    var vm = MakeViewModel(new FakeTriagePipeline(
    [
        new FileProgress { FilePath = @"C:\Videos\clip.mp4", Phase = TriagePhase.Done, Outcome = TriageOutcome.Replaced },
    ]));
    vm.SelectedFolder = @"C:\Videos";
    vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip.mp4"));
    vm.Items.Add(new FileItemViewModel(@"C:\Videos\clip2.mp4"));
    await vm.StartCommand.ExecuteAsync(null);
    // After a Done event for 1 of 2:
    vm.RunProgressText.ShouldContain("of 2");
}
```

- [ ] **Step 2: Run to verify it fails**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "RunProgressText"`
Expected: FAIL.

- [ ] **Step 3: Forward ETA from the encoder progress into `FileProgress` (Core)**

In `TriagePipeline.cs`, the encode progress callback currently reports `EncodeProgress`. Where it builds the per-file `Report(...)` during `Encoding`, include the latest ETA if available. Simplest: the encoder's `IProgress<double>` only carries the fraction; to also carry ETA, change the pipeline's encode progress handler to capture ETA from a shared variable updated alongside progress. **Minimal approach:** keep the existing `IProgress<double>` for the bar, and have the pipeline emit `EtaSeconds = null` for now if threading ETA is non-trivial — the UI falls back to "k of N". *(If the encoder is later extended to an `IProgress<HandBrakeProgress>`, populate `EtaSeconds`. For this task, wire the count-based progress reliably; ETA is best-effort and may stay null.)*

- [ ] **Step 4: Add `RunProgressText` to `MainViewModel`**

```csharp
    private int _completedInRun;
    private int _totalInRun;
    private string? _runProgressText;
    public string? RunProgressText
    {
        get => _runProgressText;
        private set => SetProperty(ref _runProgressText, value);
    }

    private void UpdateRunProgress() =>
        RunProgressText = RunState is RunState.Running or RunState.Paused
            ? $"{_completedInRun} of {_totalInRun}"
            : null;
```

In `StartAsync`, set `_totalInRun = Items.Count; _completedInRun = 0; UpdateRunProgress();` before starting. In `ApplyProgress` (where a `Done` phase is handled), increment `_completedInRun` once per file reaching `Done` and call `UpdateRunProgress()`. Clear `RunProgressText` in the run `finally`.

- [ ] **Step 5: Run tests to verify pass**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "RunProgressText"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/VideoTriage.App/ViewModels/MainViewModel.cs src/VideoTriage.Core/Pipeline/TriagePipeline.cs tests/VideoTriage.App.Tests/ViewModels/MainViewModelRunTests.cs
git commit -m "feat(app): overall run progress (k of N) in status bar"
```

---

## Task 11: Inject `IExplorerLauncher` into `MainViewModel` (DI) + drop orphaned Diagnostics wiring

**Files:**
- Modify: `src/VideoTriage.App/Services/ServiceCollectionExtensions.cs`
- Test: `tests/VideoTriage.App.Tests/Services/ServiceCollectionExtensionsTests.cs` (existing — should still pass)

- [ ] **Step 1: Pass the launcher when constructing `MainViewModel`**

In the `MainViewModel` factory lambda in `ServiceCollectionExtensions.cs`, add the argument:
```csharp
explorerLauncher: sp.GetRequiredService<IExplorerLauncher>(),
```
`IExplorerLauncher` is already registered (`services.TryAddSingleton<IExplorerLauncher, ExplorerLauncher>();`). Leave the `DiagnosticsViewModel` registration in place for now (logging path still used); it is simply no longer shown.

- [ ] **Step 2: Build + run DI smoke test**

`dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Debug -warnaserror`
`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "ServiceCollectionExtensions"`
Expected: PASS (`MainViewModel` resolves).

- [ ] **Step 3: Commit**

```bash
git add src/VideoTriage.App/Services/ServiceCollectionExtensions.cs
git commit -m "chore(app): inject IExplorerLauncher into MainViewModel"
```

---

## Task 12: Close-twice fix

**Files:**
- Modify: `src/VideoTriage.App/Views/MainWindow.xaml.cs`

- [ ] **Step 1: Defer the programmatic close**

In `OnWindowClosing`, replace the `finally` block:

```csharp
        finally
        {
            _closeConfirmed = true;
            // Close() must not run re-entrantly inside the Closing event (it throws
            // InvalidOperationException when idle cleanup completes synchronously).
            // Schedule it as a fresh dispatcher operation after this cycle unwinds.
            Dispatcher.BeginInvoke(new Action(Close));
        }
```

Remove the now-defunct `try { Close(); } catch (InvalidOperationException) { }`.

- [ ] **Step 2: Build to verify it compiles**

`dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Debug -warnaserror`
Expected: 0 errors.

- [ ] **Step 3: Manual verification note**

WPF window close is not unit-testable headlessly. Record in the PR: *launch app, click X once (idle) → closes on first click; start a run, click X → cleanup runs then closes.*

- [ ] **Step 4: Commit**

```bash
git add src/VideoTriage.App/Views/MainWindow.xaml.cs
git commit -m "fix(app): close window on first click (defer re-entrant Close)"
```

---

## Task 13: `MainWindow.xaml` — toolbar, sidebar, queue header, banner, status bar, Start tooltip

**Files:**
- Modify: `src/VideoTriage.App/Views/MainWindow.xaml`
- Delete: `src/VideoTriage.App/Views/DiagnosticsView.xaml` (+ `.cs`)
- Test: `tests/VideoTriage.App.Tests/Views/MainWindowMarkupTests.cs` (extend — this suite asserts on XAML text)

- [ ] **Step 1: Write failing markup tests**

Add to `tests/VideoTriage.App.Tests/Views/MainWindowMarkupTests.cs` (it reads the XAML file as text; mirror existing helper for locating the file):

```csharp
[Fact]
public void Toolbar_BindsStartStopPauseResumeBackOpenData()
{
    var xaml = ReadMainWindowXaml();
    xaml.ShouldContain("{Binding StartCommand}");
    xaml.ShouldContain("{Binding StopCommand}");
    xaml.ShouldContain("{Binding BackToQueueCommand}");
    xaml.ShouldContain("{Binding OpenDataDirectoryCommand}");
}

[Fact]
public void Sidebar_HasNoDiagnosticsExpander()
{
    ReadMainWindowXaml().ShouldNotContain("DiagnosticsView");
}

[Fact]
public void StatusBar_BindsRunProgressAndQueueSummary()
{
    var xaml = ReadMainWindowXaml();
    xaml.ShouldContain("RunProgressText");
    xaml.ShouldContain("QueueSummaryText");
}

[Fact]
public void RecoveryBanner_BindsInterruptedNotice()
{
    ReadMainWindowXaml().ShouldContain("InterruptedRunNotice");
}
```

(If `ReadMainWindowXaml()` doesn't exist, add it: read `src/VideoTriage.App/Views/MainWindow.xaml` relative to the test assembly via the existing pattern in the file.)

- [ ] **Step 2: Run to verify it fails**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "MainWindowMarkup"`
Expected: FAIL.

- [ ] **Step 3: Edit the toolbar (rows 117–212)**

Replace the toolbar `StackPanel` so only the contextual pair shows. Keep `x:Name`s. Target markup:

```xml
<StackPanel Orientation="Horizontal">
    <!-- Primary: Start (idle) / Stop (running|paused) / Back to queue (complete) -->
    <ui:Button x:Name="StartButton" Content="Start" Appearance="Primary"
               Width="104" Height="34" Command="{Binding StartCommand}"
               ToolTip="{Binding StartBlockedReason}"
               Visibility="{Binding RunState, Converter={StaticResource RunStateToVisibility}, ConverterParameter=Idle}" />
    <Button x:Name="StopButton" Content="Stop" Width="104" Height="34"
            Background="{StaticResource DangerBrush}" Foreground="#2A0808"
            Command="{Binding StopCommand}"
            Visibility="{Binding RunState, Converter={StaticResource RunStateToVisibility}, ConverterParameter=RunningOrPaused}" />
    <Button Content="Back to queue" Width="120" Height="34" Margin="0"
            Command="{Binding BackToQueueCommand}"
            Visibility="{Binding LastSummary, Converter={StaticResource NotNullToVisibility}}" />
    <!-- Secondary -->
    <Button x:Name="PauseButton" Content="Pause" Width="104" Height="34" Margin="8,0,0,0"
            Command="{Binding PauseCommand}"
            Visibility="{Binding RunState, Converter={StaticResource RunStateToVisibility}, ConverterParameter=Running}" />
    <Button x:Name="ResumeButton" Content="Resume" Width="104" Height="34" Margin="8,0,0,0"
            Command="{Binding ResumeCommand}"
            Visibility="{Binding RunState, Converter={StaticResource RunStateToVisibility}, ConverterParameter=Paused}" />
    <Button Content="Open run data" Width="120" Height="34" Margin="8,0,0,0"
            Command="{Binding OpenDataDirectoryCommand}"
            Visibility="{Binding LastSummary, Converter={StaticResource NotNullToVisibility}}" />
    <!-- Scanning indicator (unchanged) -->
    <StackPanel Margin="16,0,0,0" Orientation="Horizontal" VerticalAlignment="Center"
                Visibility="{Binding IsScanning, Converter={StaticResource BoolToVisibility}}">
        <ui:ProgressRing IsIndeterminate="True" Width="16" Height="16" />
        <TextBlock Text="Scanning…" Margin="8,0,0,0" VerticalAlignment="Center" />
    </StackPanel>
</StackPanel>
```

Add the needed converters to `FluentWindow.Resources` (create `src/VideoTriage.App/Converters/` value converters): `RunStateToVisibility` (compares `RunState` to the parameter, supporting `Idle`, `Running`, `Paused`, `RunningOrPaused`), `NotNullToVisibility`, `BoolToVisibility`. Each is a tiny `IValueConverter`. (If the project already uses style-trigger visibility, you may instead keep `Style.Triggers` as today — but converters keep the markup readable. Either is acceptable; pick one and be consistent.)

- [ ] **Step 4: Sidebar — remove Diagnostics, slim Preset, add queue header + recovery banner**

In the sidebar `StackPanel`: delete the `<Expander> … DiagnosticsView …</Expander>` block. Slim the Preset block to one caption line:
```xml
<TextBlock Margin="0,24,0,4" Text="Preset" FontWeight="SemiBold" Foreground="{StaticResource AccentBrush}" />
<TextBlock Text="VideoTriage AV1 · read-only scan" Opacity="0.7" TextWrapping="Wrap" />
```

In the main content area (above the queue `ListBox`), add the recovery banner and queue header:
```xml
<Border Background="#33E8C35A" CornerRadius="6" Padding="12,8" Margin="16,16,16,0"
        Visibility="{Binding InterruptedRunNotice, Converter={StaticResource NotNullToVisibility}}">
    <DockPanel>
        <Button DockPanel.Dock="Right" Content="Dismiss" Command="{Binding DismissInterruptedNoticeCommand}" />
        <Button DockPanel.Dock="Right" Content="Open run data" Margin="0,0,8,0" Command="{Binding OpenDataDirectoryCommand}" />
        <TextBlock Text="{Binding InterruptedRunNotice}" TextWrapping="Wrap" VerticalAlignment="Center" />
    </DockPanel>
</Border>
<TextBlock Margin="16,12,16,0" Opacity="0.8" Text="{Binding QueueSummaryText}"
           Visibility="{Binding LastSummary, Converter={StaticResource NullToVisibility}}" />
```

- [ ] **Step 5: Status bar — progress + Open-log + severity color (rows 320–336)**

Replace the status `Border` content:
```xml
<Border Grid.Row="2" Padding="18,0" BorderBrush="#22000000" BorderThickness="0,1,0,0"
        Background="{Binding LastSummary.Severity, Converter={StaticResource SeverityToBrush}}">
    <Grid>
        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
            <Border BorderBrush="#735CC8FF" BorderThickness="1" CornerRadius="4" Padding="8,2">
                <TextBlock Text="{Binding QueueRemainingCount, StringFormat=Queue: {0} files}" />
            </Border>
            <TextBlock Margin="14,0,0,0" VerticalAlignment="Center" Opacity="0.85"
                       Text="{Binding RunProgressText}" />
        </StackPanel>
        <StackPanel HorizontalAlignment="Right" VerticalAlignment="Center" Orientation="Horizontal">
            <TextBlock Text="{Binding StatusMessage}" VerticalAlignment="Center" />
            <ui:HyperlinkButton Content="Open log" Margin="8,0,0,0" Command="{Binding OpenLogCommand}"
                                Visibility="{Binding StatusMessage, Converter={StaticResource RunFailedToVisibility}}" />
        </StackPanel>
    </Grid>
</Border>
```

Add `SeverityToBrush` converter (`None`→transparent, `Success`→`#335AD17F`, `Warning`→`#33E8C35A`) and a simple visibility converter for the Open-log link (show when `StatusMessage` starts with "Run failed"). Update the failure `StatusMessage` in `MainViewModel` (Task currently sets "Run failed. See Diagnostics for details.") to **"Run failed — see log"** so it no longer references the removed panel; the Open-log link sits beside it.

- [ ] **Step 6: Delete `DiagnosticsView`**

Delete `src/VideoTriage.App/Views/DiagnosticsView.xaml` and `DiagnosticsView.xaml.cs`. Build; fix any remaining reference.

- [ ] **Step 7: Run markup tests + build**

`dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Debug -warnaserror`
`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "MainWindowMarkup"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/VideoTriage.App/Views/MainWindow.xaml src/VideoTriage.App/Converters/ tests/VideoTriage.App.Tests/Views/MainWindowMarkupTests.cs
git rm src/VideoTriage.App/Views/DiagnosticsView.xaml src/VideoTriage.App/Views/DiagnosticsView.xaml.cs
git commit -m "feat(app): toolbar morph, remove diagnostics, queue header, recovery banner, status bar"
```

---

## Task 14: `SummaryView.xaml` — table B, donut legend, tiles, timing, thumbnails, reveal

**Files:**
- Modify: `src/VideoTriage.App/Views/SummaryView.xaml`
- Test: `tests/VideoTriage.App.Tests/Views/` (add a markup test mirroring the MainWindow pattern if the suite supports SummaryView; else manual)

- [ ] **Step 1: Update the stat tiles + header timing**

Replace the `UniformGrid` tiles to the four meaningful tiles + relabel; add completion/duration under the title:
```xml
<StackPanel>
    <TextBlock Text="Run complete" FontSize="28" FontWeight="SemiBold" />
    <TextBlock Opacity="0.7">
        <Run Text="Completed " /><Run Text="{Binding CompletedAtText, Mode=OneWay}" />
        <Run Text=" · " /><Run Text="{Binding DurationText, Mode=OneWay}" />
    </TextBlock>
</StackPanel>
...
<UniformGrid Grid.Column="1" Columns="2" Margin="28,0,0,0">
    <StackPanel Margin="8"><TextBlock Text="Space saved" Opacity="0.65" /><TextBlock Text="{Binding BytesSavedText}" FontSize="24" /></StackPanel>
    <StackPanel Margin="8"><TextBlock Text="Replaced" Opacity="0.65" /><TextBlock Text="{Binding ReplacedCount}" FontSize="24" /></StackPanel>
    <StackPanel Margin="8"><TextBlock Text="Kept original" Opacity="0.65" /><TextBlock Text="{Binding KeptOriginalCount}" FontSize="24" /></StackPanel>
    <StackPanel Margin="8"><TextBlock Text="Overall reduction" Opacity="0.65" /><TextBlock Text="{Binding OverallReductionText}" FontSize="24" /></StackPanel>
</UniformGrid>
```

- [ ] **Step 2: Add a donut legend beside the ring**

Wrap the donut `Grid` and a legend `ItemsControl` (bound to `Segments`) in a horizontal `StackPanel`:
```xml
<ItemsControl ItemsSource="{Binding Segments}" Margin="16,0,0,0" VerticalAlignment="Center">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal" Margin="0,3">
                <Border Width="10" Height="10" CornerRadius="2" VerticalAlignment="Center"
                        Background="{Binding Color}" />
                <TextBlock Margin="8,0,0,0" VerticalAlignment="Center">
                    <Run Text="{Binding Label, Mode=OneWay}" /><Run Text="  " /><Run Text="{Binding Count, Mode=OneWay}" FontWeight="SemiBold" />
                </TextBlock>
            </StackPanel>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```
(`SummarySegment.Color` is a hex string; bind through a `StringToBrush` converter, or change the swatch `Background` to use a small converter. Add `StringToBrush` if not present.)

- [ ] **Step 3: Rebuild the file table as Approach B with thumbnail + reveal**

Replace the `DataGrid` with one carrying thumbnail, filename, status pill, size transition, saved, and a reveal action. Use a `DataGrid` with `CellTemplate`s or a `ListView`/`ItemsControl`. Target (ItemsControl rows for full template control):
```xml
<ItemsControl Grid.Row="2" ItemsSource="{Binding Files}">
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <Grid Margin="0,0,0,8">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="64" /><ColumnDefinition Width="2*" />
          <ColumnDefinition Width="Auto" /><ColumnDefinition Width="*" />
          <ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <Border Width="56" Height="32" CornerRadius="4" ClipToBounds="True" Background="#247F7F7F">
          <Image Source="{Binding Thumbnail}" Stretch="Uniform"
                 HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Border>
        <TextBlock Grid.Column="1" Margin="10,0" VerticalAlignment="Center"
                   Text="{Binding FileName}" TextTrimming="CharacterEllipsis"
                   ToolTip="{Binding FullPath}" />
        <Border Grid.Column="2" VerticalAlignment="Center" CornerRadius="10" Padding="8,2"
                Background="{Binding StatusColor, Converter={StaticResource StringToBrushFaint}}">
          <TextBlock Text="{Binding StatusLabel}" Foreground="{Binding StatusColor, Converter={StaticResource StringToBrush}}" />
        </Border>
        <TextBlock Grid.Column="3" Margin="12,0" VerticalAlignment="Center">
          <Run Text="{Binding OldSizeText, Mode=OneWay}" Foreground="{StaticResource DangerBrush}" />
          <Run Text=" → " /><Run Text="{Binding NewSizeText, Mode=OneWay}" Foreground="{StaticResource SuccessBrush}" />
        </TextBlock>
        <TextBlock Grid.Column="4" VerticalAlignment="Center" Foreground="{StaticResource SuccessBrush}"
                   Text="{Binding SavedText}" />
        <Button Grid.Column="5" Margin="8,0,0,0" Content="Reveal"
                Command="{Binding DataContext.RevealCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                CommandParameter="{Binding RevealTargetPath}" />
      </Grid>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

Add a `RevealCommand` to `SummaryViewModel`: inject an `IExplorerLauncher?` into its constructor (default null) — update the `new SummaryViewModel(summary, thumbs)` site in `MainViewModel` to `new SummaryViewModel(summary, thumbs, _explorerLauncher)` — and implement:
```csharp
public IRelayCommand<string> RevealCommand { get; }
// in ctor:
RevealCommand = new RelayCommand<string>(p =>
{
    if (string.IsNullOrWhiteSpace(p)) return;
    var dir = System.IO.Path.GetDirectoryName(p);
    if (!string.IsNullOrWhiteSpace(dir)) _explorerLauncher?.Open(dir);
});
```
(Add `_explorerLauncher` field + `using CommunityToolkit.Mvvm.Input;`.)

- [ ] **Step 4: Build + manual check**

`dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Debug -warnaserror`
Expected: 0 errors. Manual: run a triage, confirm table shows thumbnail, pill, size transition, saved %, and Reveal opens the folder.

- [ ] **Step 5: Commit**

```bash
git add src/VideoTriage.App/Views/SummaryView.xaml src/VideoTriage.App/ViewModels/SummaryViewModel.cs src/VideoTriage.App/Converters/
git commit -m "feat(app): summary table B with thumbnails, legend, timing, reveal"
```

---

## Task 15: `SettingsView.xaml` — per-field validation, remove Save button; full-frame queue thumbnail

**Files:**
- Modify: `src/VideoTriage.App/Views/SettingsView.xaml`
- Modify: `src/VideoTriage.App/Views/MainWindow.xaml` (queue-row thumbnail `Stretch`)

- [ ] **Step 1: Per-field validation + drop Save**

In `SettingsView.xaml`, bind the two numeric `TextBox`es with `ValidatesOnNotifyDataErrors=True` and add helper text; remove the `Save settings` `Button`:
```xml
<TextBlock Margin="0,10,0,4" Text="Candidate BPP threshold" />
<TextBox Text="{Binding CandidateBppThreshold, UpdateSourceTrigger=PropertyChanged, ValidatesOnNotifyDataErrors=True}" />
<TextBlock Text="Allowed range 0–1" Opacity="0.55" FontSize="11" />

<TextBlock Margin="0,10,0,4" Text="Minimum free space (GB)" />
<TextBox Text="{Binding MinimumFreeGigabytes, UpdateSourceTrigger=PropertyChanged, ValidatesOnNotifyDataErrors=True}" />
<TextBlock Text="At least 1 GB" Opacity="0.55" FontSize="11" />
```
Add a `Validation.ErrorTemplate` (red border) in resources, or rely on the WPF-UI default error adorner. Delete the `<Button … Content="Save settings" …/>`.

- [ ] **Step 2: Full-frame queue thumbnail**

In `MainWindow.xaml` queue-row template, change the thumbnail `Image` to full-frame centered:
```xml
<Image Source="{Binding Thumbnail, Mode=OneWay}" Stretch="Uniform"
       HorizontalAlignment="Center" VerticalAlignment="Center" />
```

- [ ] **Step 3: Build + manual check**

`dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Debug -warnaserror`
Manual: enter an invalid threshold → field shows error, Start blocked with tooltip reason; thumbnails show whole frame centered.

- [ ] **Step 4: Commit**

```bash
git add src/VideoTriage.App/Views/SettingsView.xaml src/VideoTriage.App/Views/MainWindow.xaml
git commit -m "feat(app): per-field settings validation, auto-apply UI, full-frame thumbnails"
```

---

## Task 16: Queue-count regression test (bug 1c)

**Files:**
- Test: `tests/VideoTriage.App.Tests/ViewModels/MainViewModelRunTests.cs` (add)

- [ ] **Step 1: Write the regression test**

```csharp
[Fact]
public async Task AfterScan_QueueRemainingCount_EqualsCandidateRows()
{
    var scanner = new RecordingScanner(emit:
    [
        Candidate(@"C:\Videos\a.mp4"),
        Candidate(@"C:\Videos\b.mp4"),
        Candidate(@"C:\Videos\c.mp4"),
    ]);
    var vm = MakeViewModel(new FakeTriagePipeline([]), scanner: scanner);
    vm.SelectedFolder = @"C:\Videos";
    await vm.ChooseFolderRescanForTest(@"C:\Videos"); // or trigger scan path used by ChooseFolder
    vm.QueueRemainingCount.ShouldBe(vm.Items.Count);
    vm.Items.Count.ShouldBe(3);
}
```

Use the `RecordingScanner` from Task 9, extended to emit candidate `ProbeResult`s via the progress callback. `Candidate(path)` builds a `ProbeResult` with a `Candidate` classification (mirror `FileItemViewModelTests` helper). Expose the extracted `ScanFolderAsync` (Task 9) as internal for the test, or drive through `ChooseFolderCommand` with a fake dialog returning the folder.

- [ ] **Step 2: Run**

`dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj --filter "QueueRemainingCount_EqualsCandidateRows"`
Expected: PASS (if it FAILS, the count is set before the last row is added — set `QueueRemainingCount = Items.Count` in the scan `finally` after all `Items.Add`).

- [ ] **Step 3: Commit**

```bash
git add tests/VideoTriage.App.Tests/ViewModels/MainViewModelRunTests.cs
git commit -m "test(app): regression — queue count equals candidate rows after scan"
```

---

## Task 17: Full-suite verification

- [ ] **Step 1: Build App + run all App and Core tests**

```bash
dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Debug -warnaserror
dotnet test tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj
dotnet test tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj
```
Expected: all green, 0 warnings.

- [ ] **Step 2: Manual smoke (regenerate test videos first)**

```powershell
C:\Users\cayov\VideoTriageTestData\Generate-TestVideos.ps1
```
Launch the built exe, choose the videos folder, and verify: moving encode %, status bar green on clean run / amber with a skip, summary table B (thumbnail + pill + size transition + saved + reveal), donut legend, no diagnostics panel, settings auto-apply + field validation, Start tooltip when blocked, single-click close.

- [ ] **Step 3: Commit any fixes, then finish via `superpowers:finishing-a-development-branch`.**

---

## Self-review notes (coverage map)

| Spec § | Task |
|---|---|
| §0 outcome taxonomy | 4 (helper), 5 (queue), 6 (summary) |
| §1a progress bug | 1, 2 |
| §1b close-twice | 12 |
| §1c queue count | 16 |
| §2a button bar | 13 |
| §2b summary table B + reveal | 6, 14 |
| §2c donut filtered + legend + tiles | 6, 14 |
| §2d full-frame thumbnails | 14 (summary), 15 (queue) |
| §2e remove diagnostics + open-log | 13 |
| §2f slim preset | 13 |
| §2g status-bar severity | 6 (state), 13 (brush) |
| §3a queue summary header | 8 (text), 13 (view) |
| §3b run timing | 3, 6, 14 |
| §3c overall progress / ETA | 10 |
| §3d settings auto-apply | 7, 15 |
| §3e per-field validation | 7, 15 |
| §3f back-to-queue rescan | 9 |
| §4a complete outcome coverage | 4, 5, 6 |
| §4b crash-recovery banner | 9 (state), 13 (banner) |
| §4c Start-blocked reason | 8 (text), 13 (tooltip) |
| §4e overall-reduction relabel | 6, 14 |
