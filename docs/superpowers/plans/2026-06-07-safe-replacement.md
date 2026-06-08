# Safe Replacement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace an original only with a smaller verified candidate while preserving recoverable data through every filesystem failure.

**Architecture:** Introduce a narrow filesystem seam, deterministic temp naming, deletion-mode abstraction, and `SafeReplacer`. Tests use an in-memory fake filesystem and fake remover; no test touches user files or the Windows Recycle Bin.

**Tech Stack:** .NET 10, `Microsoft.VisualBasic.FileIO` for Recycle Bin, xUnit, Shouldly.

---

## Scope Check

This plan owns replacement mechanics only. Verification happens before this API is called. Pipeline
policy, manifests, resumability, and UI warnings are later plans.

## File Structure

```text
src/VideoTriage.Core/
  Models/ReplacementModels.cs
  FileSystem/IFileSystem.cs
  FileSystem/PhysicalFileSystem.cs
  FileSystem/TempFileNaming.cs
  FileSystem/VideoFileDiscovery.cs
  Replace/IFileRemover.cs
  Replace/FileRemover.cs
  Replace/ISafeReplacer.cs
  Replace/SafeReplacer.cs
tests/VideoTriage.Core.Tests/
  FileSystem/TempFileNamingTests.cs
  FileSystem/VideoFileDiscoveryTempTests.cs
  Replace/SafeReplacerTests.cs
```

### Task 1: Centralize Temp Naming

**Files:**
- Create: `src/VideoTriage.Core/FileSystem/TempFileNaming.cs`
- Modify: `src/VideoTriage.Core/FileSystem/VideoFileDiscovery.cs`
- Create: `tests/VideoTriage.Core.Tests/FileSystem/TempFileNamingTests.cs`

- [ ] **Step 1: Write red tests**

```csharp
using Shouldly;
using VideoTriage.Core.FileSystem;

namespace VideoTriage.Core.Tests.FileSystem;

public sealed class TempFileNamingTests
{
    [Theory]
    [InlineData("clip.videotriage.tmp.42.mp4")]
    [InlineData("clip.videotriage.staging.42.mp4")]
    [InlineData("clip.videotriage.partial.42.mp4")]
    [InlineData("clip.videotriage.poster.42.jpg")]
    public void IsTempArtifact_KnownMarker_ReturnsTrue(string path) =>
        TempFileNaming.IsTempArtifact(path).ShouldBeTrue();

    [Fact]
    public void EncodePath_UsesSourceDirectoryAndMp4Extension() =>
        TempFileNaming.EncodePath(@"C:\Videos\clip.mov", 42)
            .ShouldBe(@"C:\Videos\clip.videotriage.tmp.42.mp4");

    [Fact]
    public void StagingPath_IsDistinctFromEncodePath_ForSameSourceAndPid() =>
        TempFileNaming.StagingPath(@"C:\Videos\clip.mov", 42)
            .ShouldBe(@"C:\Videos\clip.videotriage.staging.42.mp4");
}
```

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter TempFileNamingTests`

Expected: missing type failure.

- [ ] **Step 3: Implement**

```csharp
namespace VideoTriage.Core.FileSystem;

public static class TempFileNaming
{
    public const string EncodeInfix = ".videotriage.tmp.";
    public const string StagingInfix = ".videotriage.staging.";
    public const string PartialInfix = ".videotriage.partial.";
    public const string PosterInfix = ".videotriage.poster.";

    public static string EncodePath(string sourcePath, int processId) =>
        Build(sourcePath, EncodeInfix, processId, ".mp4");

    public static string StagingPath(string sourcePath, int processId) =>
        Build(sourcePath, StagingInfix, processId, ".mp4");

    public static string PartialPath(string sourcePath, int processId) =>
        Build(sourcePath, PartialInfix, processId, ".mp4");

    public static string PosterImagePath(string encodePath, int processId) =>
        Build(encodePath, PosterInfix, processId, ".jpg");

    public static string PosterMuxPath(string encodePath, int processId) =>
        Build(encodePath, PosterInfix, processId, ".mp4");

    public static bool IsTempArtifact(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains(EncodeInfix, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(StagingInfix, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(PartialInfix, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(PosterInfix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Build(string path, string infix, int processId, string extension) =>
        Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}{infix}{processId}{extension}");
}
```

- [ ] **Step 4: Replace discovery literals**

Use `.Where(path => !TempFileNaming.IsTempArtifact(path))`.

- [ ] **Step 5: Run green and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter "TempFileNamingTests|VideoFileDiscoveryTests"
git add src/VideoTriage.Core/FileSystem tests/VideoTriage.Core.Tests/FileSystem
git commit -m "refactor(core): centralize triage temp naming"
```

### Task 2: Add Filesystem And Removal Seams

**Files:**
- Create: `src/VideoTriage.Core/FileSystem/IFileSystem.cs`
- Create: `src/VideoTriage.Core/FileSystem/PhysicalFileSystem.cs`
- Create: `src/VideoTriage.Core/Replace/IFileRemover.cs`
- Create: `src/VideoTriage.Core/Replace/FileRemover.cs`

- [ ] **Step 1: Add exact interfaces**

```csharp
namespace VideoTriage.Core.FileSystem;

public interface IFileSystem
{
    bool FileExists(string path);
    long GetFileLength(string path);
    void CreateDirectory(string path);
    void CopyFile(string sourcePath, string destinationPath, bool overwrite);
    void MoveFile(string sourcePath, string destinationPath);
    void DeleteFile(string path);
    long GetAvailableFreeSpace(string path);
    DateTimeOffset GetLastWriteTimeUtc(string path);
}
```

```csharp
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Replace;

public interface IFileRemover
{
    void Remove(string path, DeleteMode mode);
}
```

- [ ] **Step 2: Implement physical adapters**

`PhysicalFileSystem` delegates to `File`, `Directory`, and `DriveInfo`. `FileRemover` uses
`File.Delete` for `Permanent` and:

```csharp
Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
    path,
    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
```

- [ ] **Step 3: Build and commit**

```powershell
dotnet build VideoTriage.sln -c Debug
git add src/VideoTriage.Core/FileSystem src/VideoTriage.Core/Replace
git commit -m "feat(core): add filesystem and removal seams"
```

Expected: build succeeds.

### Task 3: Implement Crash-Safe Replacement

**Files:**
- Create: `src/VideoTriage.Core/Models/ReplacementModels.cs`
- Create: `src/VideoTriage.Core/Replace/ISafeReplacer.cs`
- Create: `src/VideoTriage.Core/Replace/SafeReplacer.cs`
- Create: `tests/VideoTriage.Core.Tests/Replace/SafeReplacerTests.cs`

- [ ] **Step 1: Write failing safety tests**

Create tests for these exact scenarios using `FakeFileSystem` and `FakeFileRemover`:

```csharp
[Fact]
public void Replace_CandidateNotSmaller_DoesNotRemoveOriginal();

[Fact]
public void Replace_StagingLengthMismatch_DoesNotRemoveOriginal();

[Fact]
public void Replace_HappyPath_StagesBeforeRemovingOriginal();

[Fact]
public void Replace_FinalRenameFailure_PreservesPartialAndReturnsReplacePartial();

[Fact]
public void Replace_DifferentExtensionTargetAlreadyExists_DoesNotRemoveOriginal();

// REGRESSION (critical): the pipeline encodes to EncodePath(source, pid) and passes that exact
// path as the candidate. Staging must NOT reuse EncodePath, or the move becomes x -> x and throws.
// This test passes the candidate at EncodePath(source, 42) and asserts the replace succeeds and
// the encode temp is consumed (no leftover at the encode path).
[Fact]
public void Replace_CandidateIsEncodeTempForSameSourceAndPid_SucceedsWithoutSelfCollision();
```

The fake records operations; the happy-path assertion is (note: **move**, not copy — staging
consumes the candidate so the encode temp cannot leak):

```csharp
fileSystem.Operations.ShouldBe([
    "move:candidate.mp4->source.videotriage.staging.42.mp4",
    "remove:source.mp4:RecycleBin",
    "move:source.videotriage.staging.42.mp4->source.mp4"
]);
```

For the regression test the candidate is `source.videotriage.tmp.42.mp4` (i.e.
`TempFileNaming.EncodePath(source, 42)`) and the expected operations are:

```csharp
fileSystem.Operations.ShouldBe([
    "move:source.videotriage.tmp.42.mp4->source.videotriage.staging.42.mp4",
    "remove:source.mp4:RecycleBin",
    "move:source.videotriage.staging.42.mp4->source.mp4"
]);
result.Outcome.ShouldBe(ReplaceOutcome.Replaced);
fileSystem.FileExists(TempFileNaming.EncodePath("source.mp4", 42)).ShouldBeFalse();
```

`FakeFileSystem.MoveFile` must throw `IOException` when source and destination are equal (mirroring
`System.IO.File.Move`), so the regression test genuinely fails if staging ever reuses the encode path.

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter SafeReplacerTests`

Expected: missing replacement types.

- [ ] **Step 3: Add models and interface**

```csharp
namespace VideoTriage.Core.Models;

public enum DeleteMode { RecycleBin, Permanent }
public enum ReplaceOutcome { Replaced, ReplacePartial, Failed }

public sealed record ReplaceResult
{
    public required ReplaceOutcome Outcome { get; init; }
    public required string FinalPath { get; init; }
    public required string Reason { get; init; }
    public bool OriginalRemoved { get; init; }
    public bool Succeeded => Outcome is ReplaceOutcome.Replaced or ReplaceOutcome.ReplacePartial;
}
```

```csharp
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Replace;

public interface ISafeReplacer
{
    ReplaceResult Replace(string originalPath, string verifiedReplacementPath, DeleteMode deleteMode);
}
```

- [ ] **Step 4: Implement `SafeReplacer`**

```csharp
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Replace;

public sealed class SafeReplacer(
    IFileSystem fileSystem,
    IFileRemover fileRemover,
    Func<int>? processId = null) : ISafeReplacer
{
    private readonly Func<int> _processId = processId ?? (() => Environment.ProcessId);

    public ReplaceResult Replace(string originalPath, string verifiedReplacementPath, DeleteMode deleteMode)
    {
        if (!fileSystem.FileExists(originalPath) || !fileSystem.FileExists(verifiedReplacementPath))
            return Failed(originalPath, "Original or verified replacement is missing.");

        var originalLength = fileSystem.GetFileLength(originalPath);
        var replacementLength = fileSystem.GetFileLength(verifiedReplacementPath);
        if (replacementLength <= 0 || replacementLength >= originalLength)
            return Failed(originalPath, "Replacement is empty or not smaller.");

        var finalPath = Path.ChangeExtension(originalPath, ".mp4");
        if (!string.Equals(finalPath, originalPath, StringComparison.OrdinalIgnoreCase) &&
            fileSystem.FileExists(finalPath))
            return Failed(originalPath, $"Final path already exists: {finalPath}");

        var pid = _processId();
        // Staging MUST use a distinct infix from the encoder output. The pipeline encodes to
        // TempFileNaming.EncodePath(source, pid) and passes that as the candidate here. If staging
        // reused EncodePath, MoveFile(candidate -> staging) would be x -> x and throw in production.
        var stagingPath = TempFileNaming.StagingPath(originalPath, pid);
        // Move (not copy): consumes the verified encode temp so it cannot leak after a successful
        // replace, while still guaranteeing the verified bytes exist on disk before the original
        // is removed. The candidate is always a triage temp, so moving it is safe.
        fileSystem.MoveFile(verifiedReplacementPath, stagingPath);
        if (!fileSystem.FileExists(stagingPath) || fileSystem.GetFileLength(stagingPath) != replacementLength)
            return Failed(originalPath, "Staging verification failed.");

        fileRemover.Remove(originalPath, deleteMode);

        try
        {
            fileSystem.MoveFile(stagingPath, finalPath);
            return new ReplaceResult
            {
                Outcome = ReplaceOutcome.Replaced,
                FinalPath = finalPath,
                Reason = "Replacement committed.",
                OriginalRemoved = true
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var partialPath = TempFileNaming.PartialPath(originalPath, pid);
            fileSystem.MoveFile(stagingPath, partialPath);
            return new ReplaceResult
            {
                Outcome = ReplaceOutcome.ReplacePartial,
                FinalPath = partialPath,
                Reason = $"Original removed; verified replacement preserved as partial: {ex.Message}",
                OriginalRemoved = true
            };
        }
    }

    private static ReplaceResult Failed(string path, string reason) => new()
    {
        Outcome = ReplaceOutcome.Failed,
        FinalPath = path,
        Reason = reason,
        OriginalRemoved = false
    };
}
```

- [ ] **Step 5: Run green**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter SafeReplacerTests`

Expected: all safety tests pass.

- [ ] **Step 6: Full verification and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Release
git add src/VideoTriage.Core tests/VideoTriage.Core.Tests/Replace
git commit -m "feat(core): add crash-safe verified replacement"
```

## Self-Review

- Removal occurs only after candidate and staging length checks.
- Tests assert operation order, not just final values.
- No test invokes physical deletion or Recycle Bin behavior.
- A post-removal rename failure preserves the verified bytes under a discoverable partial path.

## Execution Handoff

Execute on `feature/safe-replacement` after verification and encoding are integrated. Reviewers must
treat any ordering regression as Critical.
