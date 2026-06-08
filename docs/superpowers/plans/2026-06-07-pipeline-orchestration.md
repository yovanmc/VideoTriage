# Pipeline Orchestration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Orchestrate discovery, probing, classification, free-space checks, encoding, verification, size comparison, and safe replacement with progress, pause, and cancellation.

**Architecture:** `TriagePipeline` coordinates existing interfaces and reports immutable `FileProgress` events. Policy is tested entirely with fakes; physical tools and user files are never used.

**Tech Stack:** .NET 10 async/await, `IProgress<T>`, cancellation tokens, xUnit, Shouldly.

---

## Scope Check

This plan does not persist completed state or logs; those are added by the next plan through
interfaces. It does not implement WPF behavior.

## File Structure

```text
src/VideoTriage.Core/
  Models/PipelineModels.cs
  FileSystem/IVideoFileDiscovery.cs
  FileSystem/VideoFileDiscovery.cs
  Probing/IVideoClassifier.cs
  Probing/BppClassifier.cs
  Pipeline/PauseToken.cs
  Pipeline/ITriagePipeline.cs
  Pipeline/TriagePipeline.cs
tests/VideoTriage.Core.Tests/Pipeline/
  PauseTokenTests.cs
  TriagePipelineTests.cs
```

### Task 1: Pipeline Models And Pause Gate

**Files:**
- Create: `src/VideoTriage.Core/Models/PipelineModels.cs`
- Create: `src/VideoTriage.Core/Pipeline/PauseToken.cs`
- Create: `tests/VideoTriage.Core.Tests/Pipeline/PauseTokenTests.cs`

- [ ] **Step 1: Write red pause tests**

```csharp
using Shouldly;
using VideoTriage.Core.Pipeline;

namespace VideoTriage.Core.Tests.Pipeline;

public sealed class PauseTokenTests
{
    [Fact]
    public async Task WaitWhilePausedAsync_CompletesOnlyAfterResume()
    {
        var token = new PauseToken();
        token.Pause();
        var wait = token.WaitWhilePausedAsync(CancellationToken.None);
        wait.IsCompleted.ShouldBeFalse();
        token.Resume();
        await wait.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitWhilePausedAsync_CancellationThrows()
    {
        var token = new PauseToken();
        token.Pause();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(
            () => token.WaitWhilePausedAsync(cts.Token));
    }
}
```

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter PauseTokenTests`

Expected: missing `PauseToken`.

- [ ] **Step 3: Add pipeline models**

Create `src/VideoTriage.Core/Models/PipelineModels.cs`:

```csharp
namespace VideoTriage.Core.Models;

public enum TriagePhase
{
    Discovered, Probing, Classified, WaitingForSpace, Encoding,
    Verifying, EmbeddingPoster, Replacing, Done
}

public enum TriageOutcome
{
    DryRunCandidate, SkippedAlreadyAv1, SkippedLowBpp, InvalidMetadata,
    AlreadyCompleted, InsufficientSpace, EncodeFailed, OutputInvalid,
    GrewKeptOriginal, Replaced, ReplacePartial, Cancelled
}

public sealed record FileProgress
{
    public required string FilePath { get; init; }
    public required TriagePhase Phase { get; init; }
    public double? EncodeProgress { get; init; }
    public TriageOutcome? Outcome { get; init; }
    public VideoStats? Source { get; init; }
    public ClassificationResult? Classification { get; init; }
    public long? OutputBytes { get; init; }
    public double? SavedPercent { get; init; }
    public string? Message { get; init; }
    public string? FinalPath { get; init; }
}

public sealed record TriageSummary
{
    public required int Scanned { get; init; }
    public required int Candidates { get; init; }
    public required int Replaced { get; init; }
    public required int Marginal { get; init; }
    public required int Grew { get; init; }
    public required int Invalid { get; init; }
    public required int Failed { get; init; }
    public required int Skipped { get; init; }
    public required long BytesSaved { get; init; }
    public required IReadOnlyList<FileProgress> Files { get; init; }
}
```

- [ ] **Step 4: Implement pause**

```csharp
namespace VideoTriage.Core.Pipeline;

public sealed class PauseToken
{
    private TaskCompletionSource _resume =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;
        _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        IsPaused = false;
        _resume.TrySetResult();
    }

    public Task WaitWhilePausedAsync(CancellationToken cancellationToken) =>
        IsPaused ? _resume.Task.WaitAsync(cancellationToken) : Task.CompletedTask;
}
```

- [ ] **Step 5: Run green and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter PauseTokenTests
git add src/VideoTriage.Core/Models/PipelineModels.cs src/VideoTriage.Core/Pipeline/PauseToken.cs tests/VideoTriage.Core.Tests/Pipeline/PauseTokenTests.cs
git commit -m "feat(core): add pipeline progress models and pause gate"
```

### Task 2: Add Pipeline Input Seams

**Files:**
- Create: `src/VideoTriage.Core/FileSystem/IVideoFileDiscovery.cs`
- Modify: `src/VideoTriage.Core/FileSystem/VideoFileDiscovery.cs`
- Create: `src/VideoTriage.Core/Probing/IVideoClassifier.cs`
- Modify: `src/VideoTriage.Core/Probing/BppClassifier.cs`

- [ ] **Step 1: Add the exact interfaces**

```csharp
using VideoTriage.Core.Models;

namespace VideoTriage.Core.FileSystem;

public interface IVideoFileDiscovery
{
    IReadOnlyList<string> FindVideos(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false);
}
```

```csharp
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public interface IVideoClassifier
{
    ClassificationResult Classify(VideoStats stats, TriageOptions? options = null);
}
```

- [ ] **Step 2: Implement without behavior changes**

Change declarations to:

```csharp
public sealed class VideoFileDiscovery : IVideoFileDiscovery
```

and:

```csharp
public sealed class BppClassifier : IVideoClassifier
```

- [ ] **Step 3: Verify and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter "VideoFileDiscoveryTests|BppClassifierTests"
git add src/VideoTriage.Core/FileSystem src/VideoTriage.Core/Probing
git commit -m "refactor(core): add pipeline discovery and classifier seams"
```

### Task 3: Define And Drive The Pipeline

**Files:**
- Create: `src/VideoTriage.Core/Pipeline/ITriagePipeline.cs`
- Create: `src/VideoTriage.Core/Pipeline/TriagePipeline.cs`
- Create: `tests/VideoTriage.Core.Tests/Pipeline/TriagePipelineTests.cs`

- [ ] **Step 1: Write failing policy tests**

Create `tests/VideoTriage.Core.Tests/Pipeline/TriagePipelineTests.cs`:

```csharp
using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.Pipeline;

namespace VideoTriage.Core.Tests.Pipeline;

public sealed class TriagePipelineTests
{
    [Fact]
    public async Task RunAsync_LowBpp_SkipsWithoutEncoding()
    {
        var fakes = PipelineFakes.LowBpp();
        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Skipped.ShouldBe(1);
        fakes.Calls.ShouldBe(["discover", "probe", "classify"]);
    }

    [Fact]
    public async Task RunAsync_InsufficientSpace_KeepsOriginal()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.AvailableBytes = 10;

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Failed.ShouldBe(1);
        fakes.Calls.ShouldNotContain("replace");
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_VerificationFailure_KeepsOriginal()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.Verification = new VerificationResult
        {
            Outcome = VerificationOutcome.DecodeError,
            Reason = "corrupt"
        };

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Invalid.ShouldBe(1);
        fakes.Calls.ShouldBe(["discover", "probe", "classify", "space", "encode", "verify", "delete-temp"]);
        fakes.OriginalRemoved.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_SmallerVerifiedOutput_CallsSafeReplacer()
    {
        var fakes = PipelineFakes.Candidate();

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Replaced.ShouldBe(1);
        fakes.Calls.ShouldBe(["discover", "probe", "classify", "space", "encode", "verify", "replace"]);
        fakes.OriginalRemoved.ShouldBeTrue();
        // C3: savings must be computed, never left at defaults. Source 1000, output 500 => 500 bytes, 50%.
        result.BytesSaved.ShouldBe(500);
        var file = result.Files.Single();
        file.OutputBytes.ShouldBe(500);
        file.SavedPercent!.Value.ShouldBe(50, 0.01);
    }

    [Fact]
    public async Task RunAsync_SmallSavingUnderThreshold_CountsMarginal()
    {
        var fakes = PipelineFakes.Candidate();
        fakes.OutputBytes = 950; // 5% saving, below the default 10% MarginalThresholdPercent

        var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

        result.Replaced.ShouldBe(1);
        result.Marginal.ShouldBe(1);
    }
}
```

The file also contains `PipelineFakes`, a fake discovery service returning one file, fake probe,
fake classifier, fake encoder, fake verifier, fake safe replacer, and fake filesystem. Each fake
appends the exact strings shown above to the shared `Calls` list. `PipelineFakes` exposes settable
`AvailableBytes`, `SourceBytes = 1000` (the probed source size), and `OutputBytes = 500` (the size
the fake filesystem reports for the encode temp via `GetFileLength`). Tests mutate `fakes.OutputBytes`
to exercise the grew/marginal/savings paths.

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter TriagePipelineTests`

Expected: missing pipeline types.

- [ ] **Step 3: Add the interface**

```csharp
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Pipeline;

public interface ITriagePipeline
{
    Task<TriageSummary> RunAsync(string folder, TriageOptions options, bool recursive = false,
        IProgress<FileProgress>? progress = null, PauseToken? pauseToken = null,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement the orchestration**

Create `TriagePipeline` with injected `IVideoFileDiscovery`, `IFfprobeService`, `IVideoClassifier`,
`IVideoEncoder`, `IOutputVerifier`, `ISafeReplacer`, and `IFileSystem`.

For each discovered file, execute this exact sequence:

```csharp
foreach (var path in discovery.FindVideos(folder, options, recursive))
{
    Report(path, TriagePhase.Discovered);
    await WaitAsync(pauseToken, cancellationToken);

    Report(path, TriagePhase.Probing);
    var probe = await ffprobe.ProbeAsync(path, cancellationToken);
    if (!probe.Succeeded || probe.Stats is null)
    {
        Complete(path, TriageOutcome.InvalidMetadata, "Probe failed.");
        continue;
    }

    Report(path, TriagePhase.Classified);
    var classification = classifier.Classify(probe.Stats, options);
    if (!classification.IsCandidate)
    {
        Complete(path, MapSkip(classification.Outcome), classification.Reason);
        continue;
    }

    if (options.DryRun)
    {
        Complete(path, TriageOutcome.DryRunCandidate, "Dry-run candidate.");
        continue;
    }

    Report(path, TriagePhase.WaitingForSpace);
    var needed = Math.Max((long)(options.MinimumFreeGigabytes * 1024 * 1024 * 1024), probe.Stats.FileSizeBytes);
    if (fileSystem.GetAvailableFreeSpace(path) < needed)
    {
        Complete(path, TriageOutcome.InsufficientSpace, "Insufficient free space.");
        continue;
    }

    var encodePath = TempFileNaming.EncodePath(path, Environment.ProcessId);
    try
    {
        Report(path, TriagePhase.Encoding);
        var encode = await encoder.EncodeAsync(path, encodePath, new Progress<double>(
            value => Report(path, TriagePhase.Encoding, value)), cancellationToken);
        if (!encode.Succeeded)
        {
            Complete(path, TriageOutcome.EncodeFailed, encode.Reason);
            continue;
        }

        Report(path, TriagePhase.Verifying);
        var verification = await verifier.VerifyAsync(probe.Stats, encodePath, options, cancellationToken);
        if (!verification.IsValid)
        {
            fileSystem.DeleteFile(encodePath);
            Complete(path, TriageOutcome.OutputInvalid, verification.Reason);
            continue;
        }

        var outputBytes = fileSystem.GetFileLength(encodePath);
        if (outputBytes >= probe.Stats.FileSizeBytes)
        {
            fileSystem.DeleteFile(encodePath);
            Complete(path, TriageOutcome.GrewKeptOriginal, "Output was not smaller.");
            continue;
        }

        Report(path, TriagePhase.Replacing);
        var replace = replacer.Replace(path, encodePath, options.DeleteMode);
        // SafeReplacer MOVED the encode temp into staging/final, so do NOT delete encodePath here.
        // Compute savings from the source size and the verified output size for the summary.
        var savedPercent = (probe.Stats.FileSizeBytes - outputBytes) / (double)probe.Stats.FileSizeBytes * 100;
        Complete(path,
            replace.Outcome == ReplaceOutcome.ReplacePartial ? TriageOutcome.ReplacePartial : TriageOutcome.Replaced,
            replace.Reason,
            replace.FinalPath,
            outputBytes: outputBytes,
            savedPercent: savedPercent);
    }
    catch (OperationCanceledException)
    {
        // Only the encode temp is ever deleted here, and only if it still exists (a successful
        // replace has already consumed it). The original is never touched on cancellation.
        if (fileSystem.FileExists(encodePath)) fileSystem.DeleteFile(encodePath);
        Complete(path, TriageOutcome.Cancelled, "Cancelled.");
        throw;
    }
}
```

Implement `Report`, `Complete`, `MapSkip`, and summary aggregation in private methods.

- `Complete` has the signature
  `void Complete(string path, TriageOutcome outcome, string reason, string? finalPath = null, long? outputBytes = null, double? savedPercent = null)`.
  It records a terminal `FileProgress` carrying `OutputBytes`/`SavedPercent` (non-null only for
  replaced/partial files) and appends it to the per-run result list used for the summary.
- Summary aggregation (mandatory — these are `required` on `TriageSummary` and must be computed,
  not defaulted):
  - `Replaced` = count of `Replaced` **and** `ReplacePartial` terminal outcomes.
  - `BytesSaved` = `Σ (sourceBytes - OutputBytes)` over replaced/partial files (sourceBytes from the
    file's `Source.FileSizeBytes`); never negative.
  - `Marginal` = count of replaced/partial files whose `SavedPercent < options.MarginalThresholdPercent`.
  - `Grew`, `Invalid`, `Failed`, `Skipped` map from their terminal outcomes.

The code above is the required ordering; do not move replacement before verification or size comparison.

- [ ] **Step 5: Run green**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter TriagePipelineTests`

Expected: all policy and safety tests pass.

- [ ] **Step 6: Full verification and commit**

```powershell
dotnet build VideoTriage.sln -c Release
dotnet test tests/VideoTriage.Core.Tests -c Release --no-build
git add src/VideoTriage.Core/Pipeline src/VideoTriage.Core/Models/PipelineModels.cs tests/VideoTriage.Core.Tests/Pipeline
git commit -m "feat(core): orchestrate safe video triage pipeline"
```

## Self-Review

- Every destructive path passes through verification, size comparison, and `ISafeReplacer`.
- Every discovered file receives a terminal event.
- Pause occurs only at phase boundaries; cancellation remains immediate through process tokens.
- Candidate cleanup is covered for failure and cancellation.

## Execution Handoff

Execute on `feature/pipeline-orchestration` after safe replacement is integrated. The specification
review must trace every terminal outcome and confirm the original remains untouched on all failures.
