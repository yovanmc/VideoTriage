# VideoTriage Architecture Contract

> **Status:** Authoritative cross-plan reference. This is not an executable implementation plan.
> Every plan under `docs/superpowers/plans/` must use the names, signatures, ownership boundaries,
> and safety rules defined here.

**Goal:** Keep independently executed VideoTriage plans composable while preserving the
verify-before-destroy invariant.

**Baseline:** `main` already contains the WPF shell, Core probing/classification, the
non-destructive CLI scanner, and 59 passing Core tests.

---

## 1. Non-Negotiable Safety Rules

1. An original video may be removed only after a smaller replacement has passed all enabled
   verification checks and the replacement has been confirmed on disk.
2. Cancellation, pause, tool failure, verification failure, poster failure, low disk space, or
   an exception must leave the original untouched.
3. Only `SafeReplacer` may request removal of an original.
4. Only `FileRemover` may call permanent-delete or Recycle Bin APIs.
5. Core and App tests use fakes and isolated temp directories. They never encode, replace,
   recycle, or delete user videos.
6. Poster embedding produces another candidate file. The poster-bearing candidate must be
   re-verified before replacement.
7. Dry-run stops after discovery, probe, and classification.

## 2. Existing Main Surface

The following types already exist and must be extended rather than duplicated. Their full
member sets are authoritative as implemented on `main` — this listing abbreviates them. In
particular, `ProcessResult` already exposes `ExitCode`, `StandardOutput`, `StandardErrorPath`,
`Elapsed`, `TimedOut`, and a computed `Succeeded => ExitCode == 0 && !TimedOut`. Do **not**
redefine these or assume they are missing.

```text
VideoTriage.Core.Models
  VideoStats
  TriageOptions
  ClassificationOutcome
  ClassificationResult
  ProbeFailure
  ProbeResult

VideoTriage.Core.Tools
  ProcessRequest
  ProcessResult
  IProcessRunner
  ProcessRunner
  ToolLocation
  ToolLocator

VideoTriage.Core.Probing
  IFfprobeService
  FfprobeService
  FfprobeJsonParser
  BppClassifier
  FolderProbeScanner

VideoTriage.Core.FileSystem
  VideoFileDiscovery

VideoTriage.Core.Formatting
  HumanSize
```

All projects target `net10.0-windows`. Core has no WPF reference.

`ToolLocator` gains this interface before App composition:

```csharp
namespace VideoTriage.Core.Tools;

public interface IToolLocator
{
    string? FindOnPath(string executableName);
    ToolLocation RequireOnPath(string executableName);
}
```

`ToolLocator` implements `IToolLocator` without changing existing behavior.

Before pipeline orchestration, add these narrow interfaces and make the existing classes implement
them without changing behavior:

```csharp
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
namespace VideoTriage.Core.Probing;

public interface IVideoClassifier
{
    ClassificationResult Classify(VideoStats stats, TriageOptions? options = null);
}
```

## 3. Options

`TriageOptions` remains the only Core runtime-options record:

```csharp
namespace VideoTriage.Core.Models;

public sealed record TriageOptions
{
    public double CandidateBppThreshold { get; init; } = 0.13;
    public bool SkipAv1 { get; init; } = true;
    public string[] VideoExtensions { get; init; } =
        [".mp4", ".m4v", ".mov", ".mkv", ".avi", ".wmv", ".webm"];

    public bool DeepVerify { get; init; } = true;
    public double DurationTolerancePercent { get; init; } = 5;
    public bool RequireResolutionMatch { get; init; } = true;
    public double ResolutionTolerancePercent { get; init; } = 2;
    public bool RequireAudioParity { get; init; } = true;

    public DeleteMode DeleteMode { get; init; } = DeleteMode.RecycleBin;
    public double MinimumFreeGigabytes { get; init; } = 5;
    public double MarginalThresholdPercent { get; init; } = 10;
    public string DataDirectoryName { get; init; } = "_videotriage_data";
    public bool DryRun { get; init; }

    public bool EmbedPoster { get; init; } = true;
    public double PosterTimestampPercent { get; init; } = 10;
}
```

Recycle Bin is the safe default. The UI may allow permanent deletion only after explicit user
selection and visible warning.

## 4. Process Execution

HandBrake progress requires streaming standard output. Extend the existing request without
changing `IProcessRunner`:

```csharp
namespace VideoTriage.Core.Tools;

public sealed record ProcessRequest
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    public string? StderrDirectory { get; init; }
    public IProgress<string>? StandardOutputLines { get; init; }
}
```

`ProcessRunner` reports each stdout line and still returns complete stdout. On timeout or
cancellation it kills the entire process tree and awaits stdout/stderr pumps before returning or
throwing.

## 5. Verification

```csharp
namespace VideoTriage.Core.Models;

public enum VerificationOutcome
{
    Valid,
    MissingOrEmpty,
    ProbeFailed,
    DurationMismatch,
    ResolutionMismatch,
    AudioMissing,
    DecodeError
}

public sealed record VerificationResult
{
    public required VerificationOutcome Outcome { get; init; }
    public required string Reason { get; init; }
    public VideoStats? OutputStats { get; init; }
    public bool IsValid => Outcome == VerificationOutcome.Valid;
}
```

```csharp
namespace VideoTriage.Core.Verify;

public static class FfmpegStderrFilter
{
    public static IReadOnlyList<string> RealErrorLines(string stderrText);
}

public static class ResolutionParity
{
    public static bool Matches(
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight,
        double tolerancePercent);
}

public static class DurationParity
{
    public static bool WithinTolerance(
        TimeSpan source,
        TimeSpan output,
        double tolerancePercent);
}

public interface IOutputVerifier
{
    Task<VerificationResult> VerifyAsync(
        VideoStats source,
        string outputPath,
        TriageOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class OutputVerifier : IOutputVerifier
{
    public OutputVerifier(
        string ffmpegPath,
        IProcessRunner processRunner,
        IFfprobeService ffprobeService);
}
```

Deep decode uses:

```text
ffmpeg -nostdin -v error -i <output> -f null -
```

Stderr is read only from `ProcessResult.StandardErrorPath`. Benign
`non.?monotonically increasing dts` and `Last message repeated N times` lines are ignored.

## 6. Encoding

```csharp
namespace VideoTriage.Core.Models;

public enum EncodeOutcome
{
    Succeeded,
    Failed,
    Cancelled
}

public sealed record EncodeResult
{
    public required EncodeOutcome Outcome { get; init; }
    public required string OutputPath { get; init; }
    public required string Reason { get; init; }
    public int? ExitCode { get; init; }
    public string? StderrPath { get; init; }
    public TimeSpan Elapsed { get; init; }
    public bool Succeeded => Outcome == EncodeOutcome.Succeeded;
}
```

```csharp
namespace VideoTriage.Core.Encoding;

public static class HandBrakeProgressParser
{
    public static double? TryParseProgress(string line);
}

public interface IVideoEncoder
{
    Task<EncodeResult> EncodeAsync(
        string inputPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class HandBrakeEncoder : IVideoEncoder
{
    public HandBrakeEncoder(
        string handBrakePath,
        IProcessRunner processRunner,
        string presetFilePath,
        string presetName);
}
```

The preset is `src/VideoTriage.Core/Encoding/Assets/videotriage-av1.json`, preset name
`VideoTriage AV1`. Encoder arguments are:

```text
--preset-import-file <preset> -Z "VideoTriage AV1" -i <input> -o <output> --json
```

## 7. Filesystem And Replacement

All destructive and mutation-sensitive filesystem calls use a test seam:

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

public sealed class PhysicalFileSystem : IFileSystem;

public static class TempFileNaming
{
    public const string EncodeInfix = ".videotriage.tmp.";
    public const string StagingInfix = ".videotriage.staging.";
    public const string PartialInfix = ".videotriage.partial.";
    public const string PosterInfix = ".videotriage.poster.";

    public static string EncodePath(string sourcePath, int processId);
    public static string StagingPath(string sourcePath, int processId);
    public static string PartialPath(string sourcePath, int processId);
    public static string PosterImagePath(string encodePath, int processId);
    public static string PosterMuxPath(string encodePath, int processId);
    public static bool IsTempArtifact(string path);
}
```

```csharp
namespace VideoTriage.Core.Models;

public enum DeleteMode
{
    RecycleBin,
    Permanent
}

public enum ReplaceOutcome
{
    Replaced,
    ReplacePartial,
    Failed
}

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
namespace VideoTriage.Core.Replace;

public interface IFileRemover
{
    void Remove(string path, DeleteMode mode);
}

public sealed class FileRemover : IFileRemover;

public interface ISafeReplacer
{
    ReplaceResult Replace(
        string originalPath,
        string verifiedReplacementPath,
        DeleteMode deleteMode);
}

public sealed class SafeReplacer : ISafeReplacer
{
    public SafeReplacer(
        IFileSystem fileSystem,
        IFileRemover fileRemover,
        Func<int>? processId = null);
}
```

Replacement ordering is fixed:

1. Confirm original and candidate exist.
2. Confirm candidate is non-empty and smaller.
3. Compute the canonical `.mp4` path and fail if it differs from the original and already exists.
4. **Move** the candidate to a distinct same-directory staging path (`TempFileNaming.StagingPath`). The staging path uses `StagingInfix` and is therefore **never equal to the encoder's output path** (`EncodePath`), even when the candidate *is* the encoder output and `processId` matches. Using `MoveFile` (not `CopyFile`) consumes the encode temp so it cannot leak after a successful replace.
5. Confirm staging exists and has the expected length.
6. Remove the original through `IFileRemover`.
7. Rename staging to the canonical final path.
8. If step 7 fails, preserve staging under a partial name and return `ReplacePartial`.

Notes:
- `TempFileNaming.IsTempArtifact` must recognize the staging infix in addition to encode/partial/poster infixes.
- Because step 4 moves the candidate, callers (the pipeline) must **not** separately delete the candidate after a `Replaced`/`ReplacePartial` outcome — it no longer exists at the encode path. Callers still delete the encode temp on non-success outcomes (`Failed`).
- A regression test MUST pass a candidate located exactly at `EncodePath(original, pid)` (same source, same pid) to prove staging never self-collides and the encode temp is consumed.

No manifest write occurs inside `SafeReplacer`; bookkeeping cannot weaken replacement safety.

## 8. State, Manifests, And Logs

```csharp
namespace VideoTriage.Core.Models;

public sealed record CompletedFileEntry
{
    public required string SourcePath { get; init; }
    public required long SourceLength { get; init; }
    public required DateTimeOffset SourceLastWriteUtc { get; init; }
    public required TriageOutcome Outcome { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public sealed record DeleteManifestEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required DeleteMode DeleteMode { get; init; }
    public required string OriginalPath { get; init; }
    public required long OriginalBytes { get; init; }
    public required string ReplacementPath { get; init; }
    public required long ReplacementBytes { get; init; }
    public required double SavedPercent { get; init; }
}

public sealed record ResultLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string SourcePath { get; init; }
    public required TriageOutcome Outcome { get; init; }
    public required string Message { get; init; }
    public long? SourceBytes { get; init; }
    public long? OutputBytes { get; init; }
    public double? SavedPercent { get; init; }
    public string? FinalPath { get; init; }
}
```

```csharp
namespace VideoTriage.Core.State;

public interface ICompletedFileStore
{
    IReadOnlyList<CompletedFileEntry> Load();
    void Append(CompletedFileEntry entry);
}

public interface IDeleteManifest
{
    void Append(DeleteManifestEntry entry);
}

public interface IResultLog
{
    void Append(ResultLogEntry entry);
}

public sealed class JsonLinesCompletedFileStore : ICompletedFileStore;
public sealed class CsvDeleteManifest : IDeleteManifest;
public sealed class JsonLinesResultLog : IResultLog;
```

All stores serialize with structured APIs and atomically append one complete record. Completed-file
matching uses normalized full path, length, and last-write timestamp; a changed file is processed
again.

## 9. Pipeline

```csharp
namespace VideoTriage.Core.Models;

public enum TriagePhase
{
    Discovered,
    Probing,
    Classified,
    WaitingForSpace,
    Encoding,
    Verifying,
    EmbeddingPoster,
    Replacing,
    Done
}

public enum TriageOutcome
{
    DryRunCandidate,
    SkippedAlreadyAv1,
    SkippedLowBpp,
    InvalidMetadata,
    AlreadyCompleted,
    InsufficientSpace,
    EncodeFailed,
    OutputInvalid,
    GrewKeptOriginal,
    Replaced,
    ReplacePartial,
    Cancelled
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

**Savings computation ownership (mandatory).** `TriagePipeline` is the sole owner of these
fields; they are never left at defaults:

- On a `Replaced`/`ReplacePartial` outcome the pipeline reads the final on-disk size via
  `IFileSystem.GetFileLength(finalPath)` and emits the terminal `FileProgress` with
  `OutputBytes = finalBytes` and `SavedPercent = (sourceBytes - finalBytes) / (double)sourceBytes * 100`.
  For non-replacement outcomes both stay `null`.
- The terminal `TriageSummary` aggregates over the per-file results:
  - `BytesSaved = Σ (sourceBytes - OutputBytes)` for every replaced/partial file (never negative;
    files that grew are excluded because they are never replaced).
  - `Marginal = count of replaced/partial files whose `SavedPercent < options.MarginalThresholdPercent`.
  - `Replaced` counts both `Replaced` and `ReplacePartial` outcomes.
- The resumability amendment (which moves the per-file loop body) MUST preserve this computation;
  a replayed/skipped completed file contributes its persisted `OutputBytes`/`SavedPercent` if
  available, otherwise it is counted under `Skipped` and contributes 0 to `BytesSaved`.

```csharp
namespace VideoTriage.Core.Pipeline;

public sealed class PauseToken
{
    public bool IsPaused { get; }
    public void Pause();
    public void Resume();
    public Task WaitWhilePausedAsync(CancellationToken cancellationToken);
}

public interface ITriagePipeline
{
    Task<TriageSummary> RunAsync(
        string folder,
        TriageOptions options,
        bool recursive = false,
        IProgress<FileProgress>? progress = null,
        PauseToken? pauseToken = null,
        CancellationToken cancellationToken = default);
}
```

`TriagePipeline` receives abstractions for discovery, probe, classification, encoding, verification,
replacement, free-space lookup, completed-state, delete manifest, result log, and optional poster
embedding. It emits a terminal `Done` event for every discovered source.

Pause is observed between phases and while receiving encode-progress callbacks. Stop is
cancellation: the active external process tree is killed, temp artifacts are removed, and the
original remains untouched.

## 10. Poster

```csharp
namespace VideoTriage.Core.Poster;

public sealed record PosterEmbedResult
{
    public required string OutputPath { get; init; }
    public required bool Embedded { get; init; }
    public required string Reason { get; init; }
}

public interface IPosterEmbedder
{
    Task<PosterEmbedResult> EmbedAsync(
        string verifiedEncodePath,
        VideoStats source,
        TriageOptions options,
        CancellationToken cancellationToken = default);
}
```

`PosterEmbedder` owns its `IOutputVerifier`; callers cannot accidentally omit re-verification.
Failure returns the original verified encode path with `Embedded = false`. Temporary poster files
are cleaned in `finally`.

## 11. Application Composition And Prerequisites

The App uses `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.DependencyInjection`.
No `NullTriagePipeline` is permitted.

```csharp
namespace VideoTriage.App.Services;

public sealed record ToolPrerequisiteStatus(
    string Name,
    bool IsAvailable,
    string? FullPath,
    string InstallHint);

public interface IPrerequisiteService
{
    IReadOnlyList<ToolPrerequisiteStatus> Check();
}

public interface ITriagePipelineProvider
{
    ITriagePipeline? Pipeline { get; }
}
```

Startup always constructs the shell and a real `ITriagePipelineProvider`. When prerequisites are
available, `Pipeline` contains the fully constructed real pipeline. Missing tools leave
`Pipeline = null`, disable Start, and present actionable install guidance. No no-op pipeline is
created.

## 12. UI Ownership

`VideoTriage.App` owns:

- `MainViewModel`: folder, live probe queue, start/pause/resume/stop, aggregate totals.
- `FileItemViewModel`: one queue row driven by `FileProgress`.
- `SettingsViewModel`: editable persisted settings.
- `SummaryViewModel`: immutable post-run summary projection.
- `DiagnosticsViewModel`: user-facing errors and log location.
- `IDialogService`, `IUiDispatcher`, `ISettingsStore`, and `IAppLog`.

ViewModels do not directly access WPF controls, the filesystem, environment variables, or external
tools. XAML uses the approved mockup as the visual reference.

## 13. Settings And Diagnostics

Settings are stored at `%AppData%\VideoTriage\settings.json` with `System.Text.Json`. Invalid or
missing JSON returns defaults and preserves the invalid file as
`settings.invalid.<timestamp>.json`.

Application logs use `Microsoft.Extensions.Logging` with a rolling text-file provider under
`%LocalAppData%\VideoTriage\Logs`. User messages are concise and include the log path for detailed
diagnosis. Logs never contain full command-line secrets; VideoTriage currently passes no secrets.

## 14. Packaging And Release

- Packaging target: self-contained `win-x64` MSIX.
- External tools remain prerequisites and are not bundled.
- CI runs restore, Release build, and all tests on `windows-latest`.
- Release documentation includes installation, prerequisites, safety behavior, dry-run guidance,
  screenshots, architecture diagram, and recovery instructions for partial replacements.
- Tagging, pushing, GitHub releases, and publishing remain explicit user-approved actions.

## 15. Dependency Order

1. Output verification
2. HandBrake encoding
3. Safe replacement and deletion manifest
4. Pipeline orchestration and free-space/cancellation
5. Resumability, result logs, and dry-run
6. Prerequisite detection and application composition
7. Folder selection, live scanning, and queue UI
8. Start, pause, resume, stop, and cancellation UI
9. Poster extraction, embedding, and re-verification
10. Settings persistence
11. Post-run summary and statistics
12. Logging, diagnostics, and user-facing errors
13. Packaging and installation
14. README, screenshots, architecture diagram, CI, and release polish

Every dependent feature branch starts from an updated `main` after its prerequisite has been
reviewed and integrated.
