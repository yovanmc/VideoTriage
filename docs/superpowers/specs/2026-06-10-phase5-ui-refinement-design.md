# Phase 5 — UI refinement & summary redesign (design spec)

**Status:** Approved through brainstorming dialogue 2026-06-10. Untracked per workflow (do not commit).

**Goal:** Make the run experience honest and legible — a consistent toolbar, a summary that shows what actually happened to each candidate (with sizes), real encode progress, and no silently-dropped outcomes — while fixing three behavioral bugs surfaced during testing.

**Architecture:** Pure App-layer (WPF/MVVM) work plus two small `VideoTriage.Core` additions (run timing on `TriageSummary`, ETA/progress parsing). No change to the safety engine (verify-before-delete, journal, recovery, free-space preflight). Target user: someone batch-shrinking a personal library who must trust that originals are safe.

**Tech stack:** .NET 10, WPF, WPF-UI (Fluent/Mica), CommunityToolkit.Mvvm, xUnit + Shouldly.

---

## 0. Outcome taxonomy (foundational)

Many items below depend on one shared classification of `TriageOutcome`. Define it once.

**Processed candidates** — entered the encode pipeline; **shown** in the summary table, counted in the donut and tiles:

| Outcome | Humanized label | Donut group (color) |
|---|---|---|
| `Replaced` | Replaced | Replaced — `#36C98F` |
| `ReplacePartial` | Replaced (recoverable partial) | Replaced — `#36C98F` |
| `GrewKeptOriginal` | Kept — encode was larger | Kept larger — `#F5A524` |
| `OutputInvalid` | Verification failed — kept original | Failed — `#F05252` |
| `EncodeFailed` | Encode failed — kept original | Failed — `#F05252` |
| `ReplaceFailed` | Replace failed — kept original | Failed — `#F05252` |
| `InsufficientSpace` | Skipped — not enough free space | Low space — `#5B8DEF` |
| `Cancelled` | Stopped | Stopped — `#8B93A7` |

**Excluded** — never entered the encode pipeline; **hidden** from the summary table, donut, and tiles (still shown in the live queue while scanning):

`SkippedAlreadyAv1` ("Already AV1") · `SkippedLowBpp` ("Below threshold") · `InvalidMetadata` ("Couldn't read metadata") · `DryRunCandidate` ("Would re-encode (dry run)") · `AlreadyCompleted` ("Already processed").

**Rule:** the summary universe = files whose `Outcome` is in the *Processed* set. The donut center count, all tiles, and the table all draw from this same filtered set so they always reconcile. (Resolves the original "don't show already-AV1/below-threshold" ask *and* the reviewer's "InsufficientSpace silently vanishes" bug together.)

A single helper (e.g. `TriageOutcomeDisplay`) owns the label string and donut group/color for each outcome, consumed by both `FileItemViewModel` (queue rows) and `SummaryViewModel` (summary). This guarantees complete coverage — no outcome falls through to a raw enum name or blank.

---

## 1. Bug fixes

### 1a. Encode progress stuck at 0 → 100
**Root cause (confirmed against live HandBrake output):** HandBrakeCLI `--json` emits *pretty-printed, multi-line* progress objects with *trailing commas*, e.g.

```
Progress: {
    "State": "WORKING",
    "Working": {
        "Progress": 0.9916666746139526,
    }
}
```

`HandBrakeProgressParser.TryParseProgress` parses a *single line* (`"Progress: {"` → `"{"`), always fails, returns null; progress never updates and the Done event slams the bar to 100.

**Design:** replace the stateless per-line parser with a small **stateful accumulator** fed each stdout/stderr line:
- Detect the start of a JSON object (`{`), accumulate lines, track brace depth, and attempt a parse when depth returns to 0.
- Parse with `JsonDocumentOptions { AllowTrailingCommas = true }`.
- On a complete object, read `Working.Progress` (0–1, clamped) → report. Also read `Working.ETASeconds` (see §3c) when present.
- Reset the buffer after each complete object; bound the buffer so a never-closing object can't grow unbounded.

`HandBrakeEncoder` switches from calling a static method to feeding lines into an instance of the accumulator. `HandBrakeProgressParser` keeps a pure helper that parses one *complete* JSON string (easy to unit-test) plus the new line-accumulator wrapper.

**Tests:** feed the real multi-line+trailing-comma sample and assert progress emits; feed split-across-lines fragments; assert `Working.Progress` and `ETASeconds` extraction; assert no false positives on `Version`/`Muxing`/`WorkDone` objects.

### 1b. Close-twice
**Root cause:** `MainWindow.OnWindowClosing` cancels the first close (`e.Cancel = true`), runs cleanup, then calls `Close()` again. When idle, all cleanup (`StopAsync`, `CancelAndWaitAsync`) completes **synchronously**, so that second `Close()` runs **re-entrantly inside the `Closing` event**, throws `InvalidOperationException`, which is caught and swallowed — window stays open; a second user click is required.

**Design:** defer the programmatic close out of the event:
```csharp
finally
{
    _closeConfirmed = true;
    Dispatcher.BeginInvoke(new Action(Close));
}
```
So `Close()` runs as a fresh dispatcher operation after the `Closing` cycle unwinds. One click closes. Keep the existing 10s `StopAsync` timeout + MessageBox path. Remove the now-unnecessary `catch (InvalidOperationException)` swallow (or keep as defense, but it should no longer fire).

**Tests:** WPF window close is hard to unit-test headlessly; cover via a small extracted close-coordinator if cheap, otherwise rely on manual verification noted in the plan. At minimum, assert `CancelAndWaitAsync`/`StopAsync` complete promptly when idle.

### 1c. Queue count "2 vs 3"
Screenshot (stale Phase-3 build) showed "Queue: 2 files" with 3 candidate rows. **Design:** verify on current build that `QueueRemainingCount == Items.Count` after a scan completes; add a regression test asserting the post-scan count equals the candidate-row count. Fix only if it still reproduces.

---

## 2. Layout / visual redesign

### 2a. Top button bar — morphing single-primary model
One rule: exactly one **primary** (accent) action + at most one **ghost** secondary; never a dead/no-op button. Uniform height (34px) and min-width (104px). Reserve the bar's space so buttons don't reflow as they swap.

| State | Primary | Secondary |
|---|---|---|
| Idle / scanned (candidates > 0) | **Start** | — |
| Scanning | **Scanning…** (spinner, disabled) | — |
| Running | **Stop** (danger red) | *Pause* (ghost) |
| Paused | **Stop** (danger red) | *Resume* (ghost) |
| Run complete | **Back to queue** | *Open run data* (ghost) |

Driven by `RunState` + `LastSummary` via styles/triggers (as today) but consolidated so only the relevant pair is ever present. `Stop` is danger-styled because it interrupts an in-flight, file-mutating run.

### 2b. Run-complete summary table — Approach B
Columns: **File · Status · Size · Saved**, plus a **thumbnail** at the row start.
- **Thumbnail:** the poster frame (§2d), full-frame centered.
- **File:** filename only (not full path); full path as tooltip. For replaced rows, expose `FinalPath` (tooltip or secondary line). **Row action "reveal in Explorer"** (required) via existing `IExplorerLauncher`: opens the file's containing folder (the `FinalPath` for replaced rows, else the source path). Surface as a row-hover button or context action.
- **Status:** color-coded pill using the §0 label + group color.
- **Size:** `9.2 MB → 5.9 MB` (old red → new green) for replaced; original size only for kept/failed; `6.1 → 6.4 MB` for grew.
- **Saved:** percent (green) for replaced/partial; blank otherwise.
- Rows limited to the §0 *Processed* set.

`SummaryFileResult` gains the fields needed (old/new bytes, humanized status, group color, final path, thumbnail source). `SummaryViewModel` builds rows from the filtered set.

### 2c. Donut + stat tiles — filtered + legend
- Donut segments use the §0 *Processed* groups: Replaced / Kept larger / Failed / Low space / Stopped. Center count = processed candidates.
- **Add a legend** beside the ring: swatch + label + count per group. Doubles as the per-outcome breakdown.
- **Stat tiles** rework to meaningful, non-conflated values: **Space saved** · **Replaced** (count) · **Kept original** (grew-larger count) · **Overall reduction** (see §4e). "Kept" no longer conflates skipped/grew/failed — skipped are gone entirely; grew and failed are distinct in the legend.

### 2d. Full-frame thumbnails
Queue rows and summary rows: center the poster frame and show the **whole frame** (`Stretch="Uniform"`, `HorizontalAlignment`/`VerticalAlignment=Center`) instead of `UniformToFill` center-crop, so the content is recognizable. Keep the rounded frame; letterbox within it.

### 2e. Remove the Diagnostics panel + Open-log affordance
- Remove the Diagnostics `Expander` (and its `DiagnosticsView`) from the sidebar — fixes the overflow *and* the stray bare-`0` control. Logging to disk is unchanged; `DiagnosticsViewModel`/`UserErrorSink` may remain for logging but are no longer shown (remove cleanly if orphaned).
- **Replace the failure dead-end:** the current `StatusMessage = "Run failed. See Diagnostics for details."` must change. On failure, show an actionable status: **"Run failed — Open log"** where *Open log* opens the current log file via `IExplorerLauncher`. The log path comes from `IAppLog.CurrentLogPath`.

### 2f. Slim the Preset block
The sidebar's static Preset section ("VideoTriage AV1 / Read-only scan before encoding") collapses to a single one-line caption. Frees vertical space.

### 2g. Status bar color on completion
The bottom status `Border` is color-coded by run result (**severity coloring, required**):
- **Green** (success) when every processed file succeeded (`Replaced`/`ReplacePartial`).
- **Amber** (warning) when the run completed but any file was `GrewKeptOriginal`, `OutputInvalid`, `EncodeFailed`, `ReplaceFailed`, `InsufficientSpace`, or `Cancelled`.
- **Neutral grey** when no run has completed (idle/queue).

Computed from the §0 processed set on the `SummaryViewModel`.

---

## 3. New features

### 3a. Queue summary header
Above the queue list, a one-line header from `Items`: **"N candidates · X total"** (X = `HumanSize` of summed source sizes). No projected-savings (unknown pre-encode); omit rather than guess.

### 3b. Run timing
`TriageSummary` gains `StartedAtUtc` and `CompletedAtUtc` (or `StartedAtUtc` + `Duration`). `TriagePipeline.RunAsync` records both. `SummaryView` shows **"Completed h:mm tt · 2m 14s"**. Update `TriageSummary` construction sites and fakes/tests.

### 3c. Overall run progress / ETA
During a run, the status bar (its empty center) shows **"k of N · ~Xm left"**:
- `k` = completed processed-count, `N` = total candidates (from the queue).
- ETA best-effort: extend `FileProgress` with optional `EtaSeconds` sourced from HandBrake's `Working.ETASeconds` (parsed in §1a). Overall ETA ≈ current-file ETA + average-per-completed-file × remaining files. If ETA unavailable, show "k of N" alone (never a fake number).
- Clears when not running.

### 3d. Settings auto-apply
Remove the **Save settings** button. Persist `CurrentSettings()` automatically whenever a setting changes *and is valid* (`CanSave`). `ConfirmPermanentDelete` stays **session-only** (never persisted) — a deliberate safety re-confirmation each launch. Implementation: in `SetValidatedProperty`, after validation, if valid, persist. Pairs with §3e (don't persist invalid input).

### 3e. Per-field settings validation
`CandidateBppThreshold` and `MinimumFreeGigabytes` get **per-field** validation: red border/adorner on the offending `TextBox` + helper text ("0–1", "≥ 1 GB"). Use `INotifyDataErrorInfo` (preferred for per-field) or `ValidationRules`. Keep the existing single `ValidationMessage` summary or fold it into per-field — per-field is the goal. Invalid fields block auto-apply (§3d) and `Start`.

### 3f. Back-to-queue freshness
After a run, **Back to queue** must reflect reality (originals were replaced/recycled, so the old rows are stale). Design: Back to queue clears the prior run state and **re-scans `SelectedFolder`**, repopulating the queue from current disk state (replaced files now classify as Already-AV1 and drop out). The user sees an accurate, current queue rather than ghosts.

---

## 4. From independent review

### 4a. Complete outcome coverage
Covered by §0 — the shared `TriageOutcomeDisplay` helper guarantees every outcome has a label and (if processed) a donut group; `FileItemViewModel.DoneText` and `SummaryViewModel` both use it. No fall-through to raw enum/blank. `InsufficientSpace` is visible (table + donut "Low space" + counted).

### 4b. Crash-recovery banner
`MainViewModel.ChooseFolderAsync` already detects an interrupted prior run (`activeRun` journal) and only logs it (`// TODO: surface in Diagnostics panel`). Since Diagnostics is being removed, give it a real home: a **dismissible warning banner** at the top of the main pane when an interrupted run is detected — message naming the file/phase/progress, plus an **Open run data** action and **Dismiss**. Bind to a new `MainViewModel` property (e.g. `InterruptedRunNotice`). The actual recovery still happens in the pipeline on the next run (`ReplacementRecovery`); the banner is the missing *notification*.

### 4c. "Why is Start disabled?" near Start
Add a computed `StartBlockedReason` on `MainViewModel` (e.g. "Choose a folder", "No candidates found", "Confirm permanent deletion in Settings", "Fix invalid settings"). Surface it as a **tooltip on the Start button** and/or a small inline caption beneath it, so the cause is co-located with the disabled control instead of only in the sidebar.

### 4d. (folded into §3e) per-field validation.

### 4e. "Overall reduction" relabel
`SummaryViewModel.AverageReduction*` is byte-weighted total reduction, not the mean of per-file percentages — relabel the tile **"Overall reduction"** to stop inviting a mental average of the visible column. Logic unchanged.

---

## 5. Data / model changes (Core)
- `TriageSummary`: add `StartedAtUtc`, `CompletedAtUtc` (§3b).
- `FileProgress`: add optional `EtaSeconds` (§3c).
- `HandBrakeProgressParser`: stateful accumulator + complete-object parser with `AllowTrailingCommas`; surface progress + ETA (§1a).
- Update all construction sites, fakes (`FakeTriagePipeline`, pipeline test fakes), and tests for the new `TriageSummary`/`FileProgress` fields.

## 6. App-layer surface
- `MainWindow.xaml`: toolbar consolidation, queue summary header, status-bar progress/ETA + color, remove Diagnostics expander, slim Preset, recovery banner, Start tooltip.
- `SummaryView.xaml` + `SummaryViewModel` + `SummaryFileResult`: table B, donut legend, tiles rework, run timing, filtering.
- `FileItemViewModel`: full-frame thumbnail binding context; complete `DoneText` via shared helper.
- `SettingsView.xaml` + `SettingsViewModel`: per-field validation, auto-apply, remove Save button.
- `MainViewModel`: `StartBlockedReason`, `InterruptedRunNotice`, back-to-queue re-scan, overall progress/ETA state, open-log command, status-bar state.
- `MainWindow.xaml.cs`: deferred close.
- New shared `TriageOutcomeDisplay` helper (App layer).

## 7. Testing
- Unit: progress accumulator (multi-line, trailing comma, ETA, negatives); `TriageOutcomeDisplay` coverage for every enum value; `SummaryViewModel` filtering + reconciliation (donut count == tiles == table rows); `FileItemViewModel.DoneText` for every outcome; `StartBlockedReason` matrix; settings auto-apply + per-field validity; back-to-queue re-scan; queue-count regression; run-timing on `TriageSummary`.
- Manual: close-once; live encode shows moving %; status bar green on done; recovery banner on a simulated interrupted journal.

## 8. Out of scope (this round)
- In-context permanent-delete confirmation at Start (item F — deferred). *(Reveal-in-Explorer §2b and status-bar severity §2g are now in scope.)*
- Broad accessibility/keyboard pass and contrast audit (declined this round).
- Queue curation / removing files before Start (declined).
- Projected-savings estimate in the queue header.
