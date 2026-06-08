# Resumability Logs And Dry Run Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist completed-file identity, deletion manifests, and per-file results while making dry-run and retries deterministic.

**Architecture:** Three append-only stores sit behind interfaces and use JSON Lines or CSV structured serialization. The pipeline consults completed state before encoding and records terminal results after policy completes.

**Tech Stack:** .NET 10, `System.Text.Json`, `Microsoft.VisualBasic.FileIO.TextFieldParser` for CSV verification tests, xUnit, Shouldly.

---

## Scope Check

This plan adds durable bookkeeping without changing replacement ordering. Store failures are
reported as run failures but never trigger deletion or roll back a safely completed replacement.

## File Structure

```text
src/VideoTriage.Core/
  Models/StateModels.cs
  State/ICompletedFileStore.cs
  State/JsonLinesCompletedFileStore.cs
  State/IDeleteManifest.cs
  State/CsvDeleteManifest.cs
  State/IResultLog.cs
  State/JsonLinesResultLog.cs
  Pipeline/TriagePipeline.cs
tests/VideoTriage.Core.Tests/State/
  JsonLinesCompletedFileStoreTests.cs
  CsvDeleteManifestTests.cs
  JsonLinesResultLogTests.cs
tests/VideoTriage.Core.Tests/Pipeline/TriagePipelineStateTests.cs
```

### Task 1: Completed File Store

**Files:**
- Create: `src/VideoTriage.Core/Models/StateModels.cs`
- Create: `src/VideoTriage.Core/State/ICompletedFileStore.cs`
- Create: `src/VideoTriage.Core/State/JsonLinesCompletedFileStore.cs`
- Create: `tests/VideoTriage.Core.Tests/State/JsonLinesCompletedFileStoreTests.cs`

- [ ] **Step 1: Write red round-trip and changed-file tests**

```csharp
using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.State;

namespace VideoTriage.Core.Tests.State;

public sealed class JsonLinesCompletedFileStoreTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "VideoTriage.StateTests", Guid.NewGuid().ToString("N"), "done.jsonl");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);

    [Fact]
    public void AppendThenLoad_RoundTripsCompleteEntry()
    {
        var store = new JsonLinesCompletedFileStore(path);
        var entry = Entry(@"C:\Videos\a.mp4", 10, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        store.Append(entry);

        store.Load().ShouldBe([entry]);
    }

    [Fact]
    public void Load_MalformedLine_IgnoresLineAndContinues()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, ["{broken", System.Text.Json.JsonSerializer.Serialize(Entry(@"C:\a.mp4", 1, DateTimeOffset.UtcNow))]);

        new JsonLinesCompletedFileStore(path).Load().Count.ShouldBe(1);
    }

    private static CompletedFileEntry Entry(string path, long length, DateTimeOffset lastWrite) => new()
    {
        SourcePath = path,
        SourceLength = length,
        SourceLastWriteUtc = lastWrite,
        Outcome = TriageOutcome.Replaced,
        CompletedAtUtc = DateTimeOffset.Parse("2026-01-02T00:00:00Z")
    };
}
```

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter JsonLinesCompletedFileStoreTests`

Expected: missing state types.

- [ ] **Step 3: Add exact model and interface**

Create `src/VideoTriage.Core/Models/StateModels.cs` with `CompletedFileEntry`,
`DeleteManifestEntry`, and `ResultLogEntry` exactly as shown in the architecture contract. Create:

```csharp
using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

public interface ICompletedFileStore
{
    IReadOnlyList<CompletedFileEntry> Load();
    void Append(CompletedFileEntry entry);
}
```

- [ ] **Step 4: Implement JSON Lines storage**

```csharp
using System.Text.Json;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.State;

public sealed class JsonLinesCompletedFileStore(string path) : ICompletedFileStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<CompletedFileEntry> Load()
    {
        if (!File.Exists(path)) return [];
        var entries = new List<CompletedFileEntry>();
        foreach (var line in File.ReadLines(path))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<CompletedFileEntry>(line, Options);
                if (entry is not null) entries.Add(entry);
            }
            catch (JsonException)
            {
            }
        }
        return entries;
    }

    public void Append(CompletedFileEntry entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
    }
}
```

- [ ] **Step 5: Run green and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter JsonLinesCompletedFileStoreTests
git add src/VideoTriage.Core/Models/StateModels.cs src/VideoTriage.Core/State tests/VideoTriage.Core.Tests/State
git commit -m "feat(core): persist completed file identities"
```

### Task 2: Delete Manifest And Result Log

**Files:**
- Create: `src/VideoTriage.Core/State/IDeleteManifest.cs`
- Create: `src/VideoTriage.Core/State/CsvDeleteManifest.cs`
- Create: `src/VideoTriage.Core/State/IResultLog.cs`
- Create: `src/VideoTriage.Core/State/JsonLinesResultLog.cs`
- Create: `tests/VideoTriage.Core.Tests/State/CsvDeleteManifestTests.cs`
- Create: `tests/VideoTriage.Core.Tests/State/JsonLinesResultLogTests.cs`

- [ ] **Step 1: Write red serialization tests**

Create `CsvDeleteManifestTests` with:

```csharp
[Fact]
public void Append_WritesHeaderOnceAndQuotesPaths()
{
    var path = TempFile();
    var manifest = new CsvDeleteManifest(path);

    manifest.Append(new DeleteManifestEntry
    {
        Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        DeleteMode = DeleteMode.RecycleBin,
        OriginalPath = @"C:\Videos\a, ""quote"".mov",
        OriginalBytes = 100,
        ReplacementPath = @"C:\Videos\a.mp4",
        ReplacementBytes = 40,
        SavedPercent = 60
    });
    manifest.Append(new DeleteManifestEntry
    {
        Timestamp = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
        DeleteMode = DeleteMode.Permanent,
        OriginalPath = @"C:\Videos\b.mov",
        OriginalBytes = 100,
        ReplacementPath = @"C:\Videos\b.mp4",
        ReplacementBytes = 50,
        SavedPercent = 50
    });

    var lines = File.ReadAllLines(path);
    lines[0].ShouldBe("Timestamp,DeleteMode,OriginalPath,OriginalBytes,ReplacementPath,ReplacementBytes,SavedPercent");
    lines.Length.ShouldBe(3);
    lines[1].ShouldContain("""C:\Videos\a, """"quote"""".mov""");
}
```

Create `JsonLinesResultLogTests` with a round-trip test for `ResultLogEntry` containing nullable
`SourceBytes`, `OutputBytes`, `SavedPercent`, and `FinalPath`.

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter "CsvDeleteManifestTests|JsonLinesResultLogTests"`

Expected: missing store types.

- [ ] **Step 3: Implement structured writers**

Create `IDeleteManifest`, `CsvDeleteManifest`, `IResultLog`, and `JsonLinesResultLog`. Use
`JsonSerializer.Serialize` for result entries. For CSV, write fields through:

```csharp
private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
```

Header:

```text
Timestamp,DeleteMode,OriginalPath,OriginalBytes,ReplacementPath,ReplacementBytes,SavedPercent
```

Format timestamps with `"O"` and numeric values with `CultureInfo.InvariantCulture`.

- [ ] **Step 4: Run green and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter "CsvDeleteManifestTests|JsonLinesResultLogTests"
git add src/VideoTriage.Core/State tests/VideoTriage.Core.Tests/State
git commit -m "feat(core): add deletion manifest and result log"
```

### Task 3: Integrate State With Pipeline

**Files:**
- Modify: `src/VideoTriage.Core/Pipeline/TriagePipeline.cs`
- Create: `tests/VideoTriage.Core.Tests/Pipeline/TriagePipelineStateTests.cs`

- [ ] **Step 1: Write failing integration tests**

```csharp
[Fact]
public async Task RunAsync_MatchingCompletedEntry_DoesNotProbeOrEncode()
{
    var fakes = PipelineStateFakes.WithCompletedEntry(matchesSource: true);

    await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

    fakes.ProbeCalls.ShouldBe(0);
    fakes.CompletedAppends.ShouldBeEmpty();
}

[Fact]
public async Task RunAsync_Replaced_AppendsCompletedManifestAndResult()
{
    var fakes = PipelineStateFakes.WithSuccessfulReplacement();

    await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions());

    fakes.CompletedAppends.Single().Outcome.ShouldBe(TriageOutcome.Replaced);
    fakes.ManifestAppends.Single().OriginalPath.ShouldBe(@"C:\Videos\clip.mov");
    fakes.ResultAppends.Single().Outcome.ShouldBe(TriageOutcome.Replaced);
}

[Fact]
public async Task RunAsync_DryRun_PerformsNoPersistentWrites()
{
    var fakes = PipelineStateFakes.WithSuccessfulReplacement();

    await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { DryRun = true });

    fakes.CompletedAppends.ShouldBeEmpty();
    fakes.ManifestAppends.ShouldBeEmpty();
    fakes.ResultAppends.ShouldBeEmpty();
}
```

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter TriagePipelineStateTests`

Expected: constructor/signature mismatch because stores are not injected.

- [ ] **Step 3: Inject store factories and implement policy**

Because the three stores live under the *scanned folder* (`folder + options.DataDirectoryName`)
and the folder is only known at `RunAsync` time, the pipeline takes **factory functions**, not
constructed stores. Composition supplies real factories (JSON-lines / CSV under the data
directory); `PipelineStateFakes` supplies factories that return the in-memory fakes asserted in the
tests. Extend the constructor (keep all existing seams from pipeline-orchestration):

```csharp
public sealed class TriagePipeline(
    IVideoFileDiscovery discovery,
    IFfprobeService probe,
    IVideoClassifier classifier,
    IVideoEncoder encoder,
    IOutputVerifier verifier,
    ISafeReplacer replacer,
    IFileSystem fileSystem,
    Func<string, ICompletedFileStore> completedStoreFactory,
    Func<string, IDeleteManifest> deleteManifestFactory,
    Func<string, IResultLog> resultLogFactory) : ITriagePipeline
```

At the top of `RunAsync`, before the discovery loop:

```csharp
var dataDirectory = Path.Combine(folder, options.DataDirectoryName);

// Dry-run never creates the data directory or any store (Self-Review invariant).
ICompletedFileStore? completedStore = null;
IDeleteManifest? deleteManifest = null;
IResultLog? resultLog = null;
IReadOnlyList<CompletedFileEntry> completed = [];

if (!options.DryRun)
{
    fileSystem.CreateDirectory(dataDirectory);
    completedStore = completedStoreFactory(dataDirectory);
    deleteManifest = deleteManifestFactory(dataDirectory);
    resultLog = resultLogFactory(dataDirectory);
    completed = completedStore.Load();
}

// Index completed entries by normalized full path for O(1) skip checks.
var completedByPath = completed.ToDictionary(
    e => Path.GetFullPath(e.SourcePath),
    StringComparer.OrdinalIgnoreCase);
```

Inside the per-file loop, **before** the probe call, add the completed-file skip. A stored entry
only suppresses work when the on-disk identity still matches (length + last-write); a changed
source invalidates the entry and is re-triaged:

```csharp
if (completedByPath.TryGetValue(Path.GetFullPath(path), out var prior) &&
    fileSystem.FileExists(path) &&
    fileSystem.GetFileLength(path) == prior.SourceLength &&
    fileSystem.GetLastWriteTimeUtc(path) == prior.SourceLastWriteUtc)
{
    Complete(path, TriageOutcome.AlreadyCompleted, "Already completed in a prior run.");
    continue; // no probe, no encode, no re-append
}
```

Persist state inside `Complete` (or immediately after it) for every terminal outcome, gated by
dry-run. Wire the three writes exactly:

```csharp
// 1. Result log: every non-dry-run terminal outcome.
resultLog?.Append(ToResultEntry(path, outcome, reason, finalPath, outputBytes, savedPercent));

// 2. Completed store: only outcomes that should be skipped on the next run.
if (outcome is TriageOutcome.Replaced or TriageOutcome.ReplacePartial
            or TriageOutcome.GrewKeptOriginal
            or TriageOutcome.SkippedAlreadyAv1 or TriageOutcome.SkippedLowBpp)
{
    completedStore?.Append(new CompletedFileEntry
    {
        SourcePath = path,
        SourceLength = sourceLength,        // captured from probe.Stats.FileSizeBytes
        SourceLastWriteUtc = sourceLastWrite, // fileSystem.GetLastWriteTimeUtc(path) at probe time
        Outcome = outcome,
        CompletedAtUtc = DateTimeOffset.UtcNow
    });
}

// 3. Delete manifest: ONLY after an original was actually removed.
if (replace is { OriginalRemoved: true })
{
    deleteManifest?.Append(new DeleteManifestEntry { /* timestamp, mode, original/replacement paths + bytes */ });
}
```

Rules enforced by the tests:

- Load completed entries once at run start; never inside the loop.
- A matching completed entry skips probe **and** encode and appends nothing.
- Write a result entry for every non-dry-run terminal outcome.
- Write a delete manifest only after `ReplaceResult.OriginalRemoved` is true.
- Dry-run performs **no** persistent writes and does not create the data directory.

> Note: `TriageOutcome.AlreadyCompleted` is the resume-skip outcome. It already exists on the
> `TriageOutcome` enum (added in pipeline-orchestration) and the pipeline already maps it to the
> summary `Skipped` bucket and excludes it from `Candidates`, so no enum change is needed here.

- [ ] **Step 4: Run green and final verification**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter "TriagePipelineStateTests|TriagePipelineTests"
dotnet build VideoTriage.sln -c Release
dotnet test tests/VideoTriage.Core.Tests -c Release --no-build
```

Expected: all tests pass and build has zero errors.

- [ ] **Step 5: Commit**

```powershell
git add src/VideoTriage.Core/Pipeline/TriagePipeline.cs tests/VideoTriage.Core.Tests/Pipeline/TriagePipelineStateTests.cs
git commit -m "feat(core): add resumability logs and dry-run persistence policy"
```

## Self-Review

- Changed source identity invalidates old completion state.
- Failed and invalid outputs retry later.
- Manifest writes occur only after original removal.
- Dry-run never encodes, verifies, replaces, or writes state.

## Execution Handoff

Execute on `feature/resumability-logs-dry-run` after pipeline orchestration is integrated.
