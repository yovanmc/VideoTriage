# Poster Extraction Embedding And Reverification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Best-effort embed a representative poster image into a verified encode and re-verify the muxed file before replacement.

**Architecture:** Poster argument builders are pure and tested. `PosterEmbedder` owns ffmpeg calls and an `IOutputVerifier`; it returns the original verified encode when poster work fails. The pipeline calls poster embedding only between successful output verification and safe replacement.

**Tech Stack:** .NET 10, ffmpeg through `IProcessRunner`, xUnit, Shouldly.

---

## Scope Check

Poster failure never blocks a valid encode from being saved. Poster embedding does not touch the
original source and does not replace files by itself.

## Execution Corrections

These corrections are authoritative where Task 4 snippets differ from current `main`:

- Task 4 also modifies `src/VideoTriage.App/Services/ServiceCollectionExtensions.cs` and its
  composition tests. Construct `PosterEmbedder` from the available ffmpeg path, process runner, and
  verifier, then pass it to `TriagePipeline`. Without this wiring, the default-enabled poster option
  would be silently ignored in production.
- Keep a `replacementPath` variable for the verified candidate chosen after poster embedding. Every
  size check, saved-percent calculation, replacement call, and replacement-failure cleanup must use
  this path.
- If `ISafeReplacer.Replace` returns failure, delete `replacementPath` when it still exists. If a
  poster mux was produced, also delete the now-redundant original encode temp. Do not clean up only
  `encodePath`, which would leak the muxed candidate.
- Existing pipeline tests may omit the optional `IPosterEmbedder`; production composition must not.

## File Structure

```text
src/VideoTriage.Core/
  Models/TriageOptions.cs
  Poster/PosterEmbedResult.cs
  Poster/PosterArguments.cs
  Poster/IPosterEmbedder.cs
  Poster/PosterEmbedder.cs
  Pipeline/TriagePipeline.cs
src/VideoTriage.App/Services/ServiceCollectionExtensions.cs
tests/VideoTriage.Core.Tests/
  Models/TriageOptionsPosterTests.cs
  Poster/PosterArgumentsTests.cs
  Poster/PosterEmbedderTests.cs
  Pipeline/TriagePipelinePosterTests.cs
tests/VideoTriage.App.Tests/Services/ServiceCollectionExtensionsTests.cs
```

### Task 1: Poster Options

**Files:**
- Modify: `src/VideoTriage.Core/Models/TriageOptions.cs`
- Create: `tests/VideoTriage.Core.Tests/Models/TriageOptionsPosterTests.cs`

- [ ] **Step 1: Write red defaults tests**

```csharp
using Shouldly;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Tests.Models;

public sealed class TriageOptionsPosterTests
{
    [Fact] public void Defaults_EnablePosterAtTenPercent()
    {
        var options = new TriageOptions();
        options.EmbedPoster.ShouldBeTrue();
        options.PosterTimestampPercent.ShouldBe(10);
    }
}
```

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter TriageOptionsPosterTests`

Expected: missing option properties.

- [ ] **Step 3: Add properties**

```csharp
public bool EmbedPoster { get; init; } = true;
public double PosterTimestampPercent { get; init; } = 10;
```

- [ ] **Step 4: Run and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter TriageOptionsPosterTests
git add src/VideoTriage.Core/Models/TriageOptions.cs tests/VideoTriage.Core.Tests/Models/TriageOptionsPosterTests.cs
git commit -m "feat(core): add poster embedding options"
```

### Task 2: Build ffmpeg Arguments

**Files:**
- Create: `src/VideoTriage.Core/Poster/PosterArguments.cs`
- Create: `tests/VideoTriage.Core.Tests/Poster/PosterArgumentsTests.cs`

- [ ] **Step 1: Write red tests**

Create `tests/VideoTriage.Core.Tests/Poster/PosterArgumentsTests.cs`:

```csharp
using Shouldly;
using VideoTriage.Core.Poster;

namespace VideoTriage.Core.Tests.Poster;

public sealed class PosterArgumentsTests
{
    [Fact]
    public void BuildFrameGrab_UsesThumbnailFilterAndTimestamp()
    {
        PosterArguments.BuildFrameGrab("encode.mp4", "poster.jpg", TimeSpan.FromSeconds(12.5))
            .ShouldBe(["-nostdin", "-ss", "12.5", "-i", "encode.mp4",
                "-frames:v", "1", "-vf", "thumbnail", "-y", "poster.jpg"]);
    }

    [Fact]
    public void BuildCoverMux_AttachesJpegAsCoverArt()
    {
        PosterArguments.BuildCoverMux("encode.mp4", "poster.jpg", "muxed.mp4")
            .ShouldBe(["-nostdin", "-i", "encode.mp4", "-i", "poster.jpg",
                "-map", "0", "-map", "1", "-c", "copy", "-c:v:1", "mjpeg",
                "-disposition:v:1", "attached_pic", "-y", "muxed.mp4"]);
    }
}
```

- [ ] **Step 2: Implement**

```csharp
namespace VideoTriage.Core.Poster;

public static class PosterArguments
{
    public static IReadOnlyList<string> BuildFrameGrab(
        string encodePath, string posterPath, TimeSpan timestamp) =>
        ["-nostdin", "-ss", timestamp.TotalSeconds.ToString("0.###",
            System.Globalization.CultureInfo.InvariantCulture),
         "-i", encodePath, "-frames:v", "1", "-vf", "thumbnail", "-y", posterPath];

    public static IReadOnlyList<string> BuildCoverMux(
        string encodePath, string posterPath, string muxedPath) =>
        ["-nostdin", "-i", encodePath, "-i", posterPath, "-map", "0", "-map", "1",
         "-c", "copy", "-c:v:1", "mjpeg", "-disposition:v:1", "attached_pic", "-y", muxedPath];
}
```

- [ ] **Step 3: Run and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter PosterArgumentsTests
git add src/VideoTriage.Core/Poster/PosterArguments.cs tests/VideoTriage.Core.Tests/Poster
git commit -m "feat(core): build poster ffmpeg arguments"
```

### Task 3: Embed And Reverify

**Files:**
- Create: `src/VideoTriage.Core/Poster/PosterEmbedResult.cs`
- Create: `src/VideoTriage.Core/Poster/IPosterEmbedder.cs`
- Create: `src/VideoTriage.Core/Poster/PosterEmbedder.cs`
- Create: `tests/VideoTriage.Core.Tests/Poster/PosterEmbedderTests.cs`

- [ ] **Step 1: Write red tests**

Create `tests/VideoTriage.Core.Tests/Poster/PosterEmbedderTests.cs` with these concrete tests:

```csharp
[Fact]
public async Task EmbedAsync_Success_ReturnsMuxedPath()
{
    var runner = new FakeRunner(exitCodes: [0, 0]);
    var verifier = new FakeVerifier(valid: true);
    var embedder = new PosterEmbedder("ffmpeg.exe", runner, verifier);

    var result = await embedder.EmbedAsync("encode.mp4", Source(), new TriageOptions());

    result.Embedded.ShouldBeTrue();
    result.OutputPath.ShouldEndWith(".mp4");
    verifier.Paths.Single().ShouldBe(result.OutputPath);
}

[Theory]
[InlineData(1, 0)]
[InlineData(0, 1)]
public async Task EmbedAsync_FfmpegFailure_ReturnsOriginalPath(int grabExit, int muxExit)
{
    var runner = new FakeRunner(exitCodes: [grabExit, muxExit]);
    var embedder = new PosterEmbedder("ffmpeg.exe", runner, new FakeVerifier(valid: true));

    var result = await embedder.EmbedAsync("encode.mp4", Source(), new TriageOptions());

    result.Embedded.ShouldBeFalse();
    result.OutputPath.ShouldBe("encode.mp4");
}

[Fact]
public async Task EmbedAsync_ReverifyFailure_ReturnsOriginalPath()
{
    var embedder = new PosterEmbedder("ffmpeg.exe", new FakeRunner(exitCodes: [0, 0]), new FakeVerifier(valid: false));

    var result = await embedder.EmbedAsync("encode.mp4", Source(), new TriageOptions());

    result.Embedded.ShouldBeFalse();
    result.OutputPath.ShouldBe("encode.mp4");
}
```

The same test file defines `FakeRunner`, `FakeVerifier`, and `Source()` helpers. `FakeRunner` returns
the provided exit codes in order and records every `ProcessRequest`. `FakeVerifier` returns
`VerificationOutcome.Valid` when `valid` is true and `VerificationOutcome.DecodeError` otherwise.

- [ ] **Step 2: Implement contracts**

```csharp
namespace VideoTriage.Core.Poster;

public sealed record PosterEmbedResult
{
    public required string OutputPath { get; init; }
    public required bool Embedded { get; init; }
    public required string Reason { get; init; }
}
```

```csharp
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Poster;

public interface IPosterEmbedder
{
    Task<PosterEmbedResult> EmbedAsync(string verifiedEncodePath, VideoStats source,
        TriageOptions options, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Implement service**

Create `PosterEmbedder.cs`:

```csharp
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Tools;
using VideoTriage.Core.Verify;

namespace VideoTriage.Core.Poster;

public sealed class PosterEmbedder(
    string ffmpegPath,
    IProcessRunner runner,
    IOutputVerifier verifier) : IPosterEmbedder
{
    public async Task<PosterEmbedResult> EmbedAsync(
        string verifiedEncodePath,
        VideoStats source,
        TriageOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.EmbedPoster)
            return Original(verifiedEncodePath, "Poster embedding disabled.");

        var posterPath = TempFileNaming.PosterImagePath(verifiedEncodePath, Environment.ProcessId);
        var muxedPath = TempFileNaming.PosterMuxPath(verifiedEncodePath, Environment.ProcessId);
        var keepMuxed = false;
        try
        {
            var timestamp = TimeSpan.FromSeconds(source.Duration.TotalSeconds * options.PosterTimestampPercent / 100);
            var grab = await RunAsync(PosterArguments.BuildFrameGrab(verifiedEncodePath, posterPath, timestamp), cancellationToken);
            if (!grab.Succeeded) return Original(verifiedEncodePath, "Poster frame extraction failed.");

            var mux = await RunAsync(PosterArguments.BuildCoverMux(verifiedEncodePath, posterPath, muxedPath), cancellationToken);
            if (!mux.Succeeded) return Original(verifiedEncodePath, "Poster mux failed.");

            var verified = await verifier.VerifyAsync(source, muxedPath, options, cancellationToken);
            if (!verified.IsValid)
                return Original(verifiedEncodePath, $"Poster re-verification failed: {verified.Reason}");

            keepMuxed = true;
            return new PosterEmbedResult { OutputPath = muxedPath, Embedded = true, Reason = "Poster embedded." };
        }
        finally
        {
            if (File.Exists(posterPath)) File.Delete(posterPath);
            if (!keepMuxed && File.Exists(muxedPath)) File.Delete(muxedPath);
        }
    }

    private Task<ProcessResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct) =>
        runner.RunAsync(new ProcessRequest
        {
            FileName = ffmpegPath,
            Arguments = args,
            Timeout = TimeSpan.FromMinutes(5)
        }, ct);

    private static PosterEmbedResult Original(string path, string reason) =>
        new() { OutputPath = path, Embedded = false, Reason = reason };
}
```

- [ ] **Step 4: Run green and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter PosterEmbedderTests
git add src/VideoTriage.Core/Poster tests/VideoTriage.Core.Tests/Poster
git commit -m "feat(core): embed poster art with re-verification"
```

### Task 4: Pipeline Integration

**Files:**
- Modify: `src/VideoTriage.Core/Pipeline/TriagePipeline.cs`
- Create: `tests/VideoTriage.Core.Tests/Pipeline/TriagePipelinePosterTests.cs`

- [ ] **Step 1: Write red tests**

Create `tests/VideoTriage.Core.Tests/Pipeline/TriagePipelinePosterTests.cs` with:

```csharp
[Fact]
public async Task RunAsync_PosterEnabled_CallsEmbedderBetweenVerifyAndReplace()
{
    var fakes = PipelinePosterFakes.Successful(embedderReturns: "with-poster.mp4");

    await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { EmbedPoster = true });

    fakes.Calls.ShouldBe(["encode", "verify", "embed-poster", "replace:with-poster.mp4"]);
}

[Fact]
public async Task RunAsync_PosterDisabled_DoesNotCallEmbedder()
{
    var fakes = PipelinePosterFakes.Successful(embedderReturns: "with-poster.mp4");

    await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { EmbedPoster = false });

    fakes.Calls.ShouldBe(["encode", "verify", "replace:encode.mp4"]);
}

[Fact]
public async Task RunAsync_PosterFailure_ReplacesOriginalVerifiedEncode()
{
    var fakes = PipelinePosterFakes.Successful(embedderReturns: "encode.mp4");

    await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { EmbedPoster = true });

    fakes.Calls.ShouldContain("replace:encode.mp4");
}

// CRITICAL: cover art adds bytes. If the poster-muxed file is no longer smaller than the original,
// the file must be KEPT (GrewKeptOriginal) and never replaced. The size/grew check therefore runs
// on the muxed replacementPath, not on the pre-poster encode.
[Fact]
public async Task RunAsync_PosterPushesOutputOverOriginal_KeepsOriginal()
{
    var fakes = PipelinePosterFakes.Successful(embedderReturns: "with-poster.mp4");
    fakes.SetFileLength("with-poster.mp4", fakes.SourceBytes + 1); // muxed is now larger than source

    var result = await fakes.Pipeline.RunAsync(@"C:\Videos", new TriageOptions { EmbedPoster = true });

    result.Grew.ShouldBe(1);
    fakes.OriginalRemoved.ShouldBeFalse();
    fakes.Calls.ShouldNotContain("replace:with-poster.mp4");
}
```

- [ ] **Step 2: Implement**

Add optional `IPosterEmbedder? posterEmbedder = null` to the `TriagePipeline` constructor. Insert
poster embedding **after successful verification and before the size/grew check**, then run the
size check, savings computation, and replacement against `replacementPath` (the muxed file when a
poster was embedded, otherwise the encode):

```csharp
var replacementPath = encodePath;
if (options.EmbedPoster && posterEmbedder is not null)
{
    progress?.Report(progressEvent with { Phase = TriagePhase.EmbeddingPoster });
    var poster = await posterEmbedder.EmbedAsync(encodePath, sourceStats, options, cancellationToken);
    replacementPath = poster.OutputPath;
}

// The grew-check and the C3 savings computation MUST use replacementPath, because the muxed file is
// larger than the pre-poster encode. A poster that pushes the file to >= the original size is kept.
var outputBytes = fileSystem.GetFileLength(replacementPath);
if (outputBytes >= probe.Stats.FileSizeBytes)
{
    // Clean up whichever temp(s) exist. If poster muxed a new file, the encode temp may still exist.
    if (fileSystem.FileExists(replacementPath)) fileSystem.DeleteFile(replacementPath);
    if (replacementPath != encodePath && fileSystem.FileExists(encodePath)) fileSystem.DeleteFile(encodePath);
    Complete(path, TriageOutcome.GrewKeptOriginal, "Output was not smaller.");
    continue;
}

// If a poster produced a NEW muxed file (replacementPath != encodePath), the original encode temp is
// now redundant and must be deleted so it cannot leak (SafeReplacer will consume only replacementPath).
if (replacementPath != encodePath && fileSystem.FileExists(encodePath))
    fileSystem.DeleteFile(encodePath);

var savedPercent = (probe.Stats.FileSizeBytes - outputBytes) / (double)probe.Stats.FileSizeBytes * 100;
var replace = replacer.Replace(path, replacementPath, options.DeleteMode);
```

This replaces the pre-poster `outputBytes`/grew/replace block from the pipeline-orchestration plan;
the ordering (verify → poster → grew-check → replace) and the `replacementPath` target are mandatory.

- [ ] **Step 3: Verify and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter "TriagePipelinePosterTests|PosterEmbedderTests"
dotnet build VideoTriage.sln -c Release
dotnet test tests/VideoTriage.Core.Tests -c Release --no-build
git add src/VideoTriage.Core/Pipeline/TriagePipeline.cs tests/VideoTriage.Core.Tests/Pipeline/TriagePipelinePosterTests.cs
git commit -m "feat(core): run poster embedding before safe replacement"
```

## Self-Review

- Poster embedding is best-effort and never touches originals.
- Muxed outputs are re-verified before replacement.
- Pipeline integration is additive and disabled when no embedder is registered.

## Execution Handoff

Execute on `feature/poster-thumbnails` after run controls are integrated. Settings persistence
follows this plan and exposes the already-implemented poster option.
