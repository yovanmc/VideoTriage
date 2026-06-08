# Core Probe Classify Implementation Plan

> **Status:** Implemented on `main` by merge commit `76194ec`. Retained as the reference example for
> detailed TDD plan structure; do not execute it again.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first non-destructive VideoTriage engine slice: discover videos, probe metadata with `ffprobe`, classify AV1 candidates by bits-per-pixel, and provide a console harness that lists candidates for a folder.

**Architecture:** `VideoTriage.Core` stays UI-free and owns the domain models, process execution, tool lookup, ffprobe parsing, classification, and scan orchestration. A small `VideoTriage.Cli` project is added only as a non-destructive M2 harness; the WPF app remains untouched in this plan. Tests drive each Core component with fake JSON, temp files, and fake process runners so CI does not require real media files or `ffprobe`.

**Tech Stack:** .NET 10 (`net10.0-windows`), C# records, `System.Diagnostics.Process`, `System.Text.Json`, xUnit, Shouldly, PowerShell/cmd only inside tests where deterministic process behavior is needed.

---

## Scope Check

This plan covers exactly M2 from the broad design: `FfprobeService`, `BppClassifier`, `ToolLocator`, `ProcessRunner`, and a harness that lists candidates for a folder. It deliberately excludes verification, encoding, poster embedding, safe replace, delete manifests, UI wiring, settings, and resumability because those are independent later milestones.

**Safety boundary:** no HandBrake calls, no ffmpeg encode/decode, no writes beside source videos, no sidecar files in source folders, no replacement, no recycle-bin move, and no permanent delete. The only runtime writes are stderr temp files created by `ProcessRunner` and normal test temp files.

**Working directory for every command:** `C:\Agent Projects\VideoTriage`

---

## File Structure

Create or modify these files:

```text
C:\Agent Projects\VideoTriage\
├─ README.md                                      update status + manual harness note
├─ VideoTriage.sln                                add VideoTriage.Cli project
├─ src/
│  ├─ VideoTriage.Cli/
│  │  ├─ Program.cs                               non-destructive folder scan harness
│  │  └─ VideoTriage.Cli.csproj                   console project referencing Core
│  └─ VideoTriage.Core/
│     ├─ FileSystem/
│     │  └─ VideoFileDiscovery.cs                 stable video-file discovery
│     ├─ Models/
│     │  ├─ ClassificationResult.cs               classifier outcome + reason
│     │  ├─ ProbeFailure.cs                       ordinary probe failure details
│     │  ├─ ProbeResult.cs                        stats/failure/classification aggregate
│     │  ├─ TriageOptions.cs                      M2 defaults
│     │  └─ VideoStats.cs                         typed ffprobe-derived metadata + bpp
│     ├─ Probing/
│     │  ├─ BppClassifier.cs                      pure bpp/codec decision function
│     │  ├─ FfprobeJsonParser.cs                  JSON-to-VideoStats parser
│     │  ├─ FfprobeService.cs                     process runner + parser adapter
│     │  ├─ FolderProbeScanner.cs                 discovery + probe + classify orchestrator
│     │  └─ IFfprobeService.cs                    scanner seam for tests
│     └─ Tools/
│        ├─ IProcessRunner.cs                     ffprobe service seam for tests
│        ├─ ProcessRequest.cs                     executable invocation request
│        ├─ ProcessResult.cs                      exit/stdout/stderr-file result
│        ├─ ProcessRunner.cs                      safe process execution
│        ├─ ToolLocation.cs                       located executable
│        └─ ToolLocator.cs                        PATH lookup
└─ tests/
   └─ VideoTriage.Core.Tests/
      ├─ FileSystem/
      │  └─ VideoFileDiscoveryTests.cs
      ├─ Models/
      │  └─ VideoStatsTests.cs
      ├─ Probing/
      │  ├─ BppClassifierTests.cs
      │  ├─ FfprobeJsonParserTests.cs
      │  ├─ FfprobeServiceTests.cs
      │  └─ FolderProbeScannerTests.cs
      ├─ TestData/
      │  └─ Ffprobe/
      │     ├─ av1-video.json
      │     ├─ h264-with-audio.json
      │     ├─ missing-video-bitrate.json
      │     ├─ no-video-stream.json
      │     └─ stream-duration-missing-format-duration.json
      └─ Tools/
         ├─ ProcessRunnerTests.cs
         └─ ToolLocatorTests.cs
```

---

### Task 1: Domain Models

**Files:**
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Models\TriageOptions.cs`
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Models\VideoStats.cs`
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Models\ClassificationResult.cs`
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Models\ProbeFailure.cs`
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Models\ProbeResult.cs`
- Test: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\Models\VideoStatsTests.cs`

- [ ] **Step 1: Write the failing `VideoStats` tests**

Create `tests/VideoTriage.Core.Tests/Models/VideoStatsTests.cs`:

```csharp
using Shouldly;
using VideoTriage.Core.Models;
using Xunit;

namespace VideoTriage.Core.Tests.Models;

public sealed class VideoStatsTests
{
    [Fact]
    public void BitsPerPixel_UsesVideoBitrateFirst()
    {
        var stats = CreateStats(videoBitrate: 8_294_400, containerBitrate: 99_000_000);

        stats.EffectiveBitrateBitsPerSecond.ShouldBe(8_294_400);
        stats.BitsPerPixel.ShouldBe(8_294_400d / (1920 * 1080 * 30), tolerance: 0.000001);
    }

    [Fact]
    public void BitsPerPixel_FallsBackToContainerBitrate()
    {
        var stats = CreateStats(videoBitrate: null, containerBitrate: 4_147_200);

        stats.EffectiveBitrateBitsPerSecond.ShouldBe(4_147_200);
        stats.BitsPerPixel.ShouldBe(4_147_200d / (1920 * 1080 * 30), tolerance: 0.000001);
    }

    [Fact]
    public void BitsPerPixel_FallsBackToFileSizeAndDuration()
    {
        var stats = CreateStats(videoBitrate: null, containerBitrate: null) with
        {
            FileSizeBytes = 30_000_000,
            Duration = TimeSpan.FromSeconds(60)
        };

        stats.EffectiveBitrateBitsPerSecond.ShouldBe(4_000_000);
        stats.BitsPerPixel.ShouldBe(4_000_000d / (1920 * 1080 * 30), tolerance: 0.000001);
    }

    [Theory]
    [InlineData(0, 1080, 30)]
    [InlineData(1920, 0, 30)]
    [InlineData(1920, 1080, 0)]
    public void BitsPerPixel_InvalidGeometryOrFrameRate_ReturnsZero(int width, int height, double fps)
    {
        var stats = CreateStats(videoBitrate: 5_000_000, containerBitrate: null) with
        {
            Width = width,
            Height = height,
            FramesPerSecond = fps
        };

        stats.BitsPerPixel.ShouldBe(0);
    }

    private static VideoStats CreateStats(long? videoBitrate, long? containerBitrate) =>
        new()
        {
            FilePath = @"C:\videos\sample.mp4",
            CodecName = "h264",
            Width = 1920,
            Height = 1080,
            FramesPerSecond = 30,
            Duration = TimeSpan.FromSeconds(120),
            FileSizeBytes = 120_000_000,
            VideoBitrateBitsPerSecond = videoBitrate,
            ContainerBitrateBitsPerSecond = containerBitrate,
            HasAudio = true
        };
}
```

- [ ] **Step 2: Run the tests to verify the red state**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter VideoStatsTests
```

Expected: build fails with `CS0234` or `CS0246` because `VideoTriage.Core.Models.VideoStats` does not exist.

- [ ] **Step 3: Add `TriageOptions`**

Create `src/VideoTriage.Core/Models/TriageOptions.cs`:

```csharp
namespace VideoTriage.Core.Models;

public sealed record TriageOptions
{
    public double CandidateBppThreshold { get; init; } = 0.13;
    public bool SkipAv1 { get; init; } = true;
    public string[] VideoExtensions { get; init; } =
    [
        ".mp4",
        ".m4v",
        ".mov",
        ".mkv",
        ".avi",
        ".wmv",
        ".webm"
    ];
}
```

- [ ] **Step 4: Add `VideoStats`**

Create `src/VideoTriage.Core/Models/VideoStats.cs`:

```csharp
namespace VideoTriage.Core.Models;

public sealed record VideoStats
{
    public required string FilePath { get; init; }
    public required string CodecName { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required double FramesPerSecond { get; init; }
    public required TimeSpan Duration { get; init; }
    public required long FileSizeBytes { get; init; }
    public long? VideoBitrateBitsPerSecond { get; init; }
    public long? ContainerBitrateBitsPerSecond { get; init; }
    public bool HasAudio { get; init; }

    public long EffectiveBitrateBitsPerSecond =>
        VideoBitrateBitsPerSecond
        ?? ContainerBitrateBitsPerSecond
        ?? (Duration.TotalSeconds > 0
            ? (long)Math.Round(FileSizeBytes * 8d / Duration.TotalSeconds)
            : 0);

    public double BitsPerPixel =>
        Width > 0 && Height > 0 && FramesPerSecond > 0 && EffectiveBitrateBitsPerSecond > 0
            ? EffectiveBitrateBitsPerSecond / (Width * Height * FramesPerSecond)
            : 0;
}
```

- [ ] **Step 5: Add classification and probe result models**

Create `src/VideoTriage.Core/Models/ClassificationResult.cs`:

```csharp
namespace VideoTriage.Core.Models;

public enum ClassificationOutcome
{
    Candidate,
    SkipAlreadyAv1,
    SkipLowBpp,
    InvalidMetadata
}

public sealed record ClassificationResult
{
    public required ClassificationOutcome Outcome { get; init; }
    public required string Reason { get; init; }
    public required VideoStats Stats { get; init; }

    public bool IsCandidate => Outcome == ClassificationOutcome.Candidate;
}
```

Create `src/VideoTriage.Core/Models/ProbeFailure.cs`:

```csharp
namespace VideoTriage.Core.Models;

public sealed record ProbeFailure
{
    public required string FilePath { get; init; }
    public required string Message { get; init; }
    public int? ExitCode { get; init; }
    public string? StderrPath { get; init; }
}
```

Create `src/VideoTriage.Core/Models/ProbeResult.cs`:

```csharp
namespace VideoTriage.Core.Models;

public sealed record ProbeResult
{
    public required string FilePath { get; init; }
    public VideoStats? Stats { get; init; }
    public ProbeFailure? Failure { get; init; }
    public ClassificationResult? Classification { get; init; }

    public bool Succeeded => Stats is not null && Failure is null;
}
```

- [ ] **Step 6: Run the model tests to verify green**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter VideoStatsTests
```

Expected: `Passed!` with `Failed: 0`.

- [ ] **Step 7: Commit**

Run:

```bash
git add src/VideoTriage.Core/Models tests/VideoTriage.Core.Tests/Models
git commit -m "feat(core): add probe domain models"
```

Expected: commit succeeds.

---

### Task 2: Bpp Classifier

**Files:**
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Probing\BppClassifier.cs`
- Test: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\Probing\BppClassifierTests.cs`

- [ ] **Step 1: Write the failing classifier tests**

Create `tests/VideoTriage.Core.Tests/Probing/BppClassifierTests.cs`:

```csharp
using Shouldly;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using Xunit;

namespace VideoTriage.Core.Tests.Probing;

public sealed class BppClassifierTests
{
    [Fact]
    public void Classify_SkipsAv1_WhenSkipAv1IsTrue()
    {
        var result = new BppClassifier().Classify(CreateStats(codecName: "av1", bpp: 0.25));

        result.Outcome.ShouldBe(ClassificationOutcome.SkipAlreadyAv1);
        result.IsCandidate.ShouldBeFalse();
        result.Reason.ShouldContain("already AV1");
    }

    [Fact]
    public void Classify_AllowsAv1_WhenSkipAv1IsFalse()
    {
        var result = new BppClassifier().Classify(
            CreateStats(codecName: "AV1", bpp: 0.25),
            new TriageOptions { SkipAv1 = false });

        result.Outcome.ShouldBe(ClassificationOutcome.Candidate);
    }

    [Theory]
    [InlineData(0.13)]
    [InlineData(0.20)]
    public void Classify_ReturnsCandidate_AtOrAboveThreshold(double bpp)
    {
        var result = new BppClassifier().Classify(CreateStats(codecName: "h264", bpp: bpp));

        result.Outcome.ShouldBe(ClassificationOutcome.Candidate);
        result.IsCandidate.ShouldBeTrue();
    }

    [Fact]
    public void Classify_SkipsLowBpp_BelowThreshold()
    {
        var result = new BppClassifier().Classify(CreateStats(codecName: "hevc", bpp: 0.129));

        result.Outcome.ShouldBe(ClassificationOutcome.SkipLowBpp);
        result.Reason.ShouldContain("below");
    }

    [Fact]
    public void Classify_CodecComparisonIsCaseInsensitive()
    {
        var result = new BppClassifier().Classify(CreateStats(codecName: "Av1", bpp: 0.40));

        result.Outcome.ShouldBe(ClassificationOutcome.SkipAlreadyAv1);
    }

    [Theory]
    [InlineData(0, 1080, 30, 5_000_000)]
    [InlineData(1920, 0, 30, 5_000_000)]
    [InlineData(1920, 1080, 0, 5_000_000)]
    [InlineData(1920, 1080, 30, 0)]
    public void Classify_InvalidMetadata_WhenGeometryFrameRateOrBitrateIsZero(
        int width,
        int height,
        double fps,
        long bitrate)
    {
        var stats = CreateStats(codecName: "h264", bpp: 0.20) with
        {
            Width = width,
            Height = height,
            FramesPerSecond = fps,
            VideoBitrateBitsPerSecond = bitrate
        };

        var result = new BppClassifier().Classify(stats);

        result.Outcome.ShouldBe(ClassificationOutcome.InvalidMetadata);
    }

    [Fact]
    public void Classify_InvalidMetadata_WhenDurationIsZero()
    {
        var stats = CreateStats(codecName: "h264", bpp: 0.20) with
        {
            Duration = TimeSpan.Zero
        };

        var result = new BppClassifier().Classify(stats);

        result.Outcome.ShouldBe(ClassificationOutcome.InvalidMetadata);
    }

    [Fact]
    public void Classify_UsesCustomThreshold()
    {
        var result = new BppClassifier().Classify(
            CreateStats(codecName: "h264", bpp: 0.15),
            new TriageOptions { CandidateBppThreshold = 0.16 });

        result.Outcome.ShouldBe(ClassificationOutcome.SkipLowBpp);
    }

    private static VideoStats CreateStats(string codecName, double bpp)
    {
        const int width = 1920;
        const int height = 1080;
        const double fps = 30;
        var bitrate = (long)Math.Round(bpp * width * height * fps);

        return new VideoStats
        {
            FilePath = @"C:\videos\sample.mp4",
            CodecName = codecName,
            Width = width,
            Height = height,
            FramesPerSecond = fps,
            Duration = TimeSpan.FromSeconds(60),
            FileSizeBytes = 30_000_000,
            VideoBitrateBitsPerSecond = bitrate,
            HasAudio = true
        };
    }
}
```

- [ ] **Step 2: Run the tests to verify the red state**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter BppClassifierTests
```

Expected: build fails with `CS0234` or `CS0246` because `VideoTriage.Core.Probing.BppClassifier` does not exist.

- [ ] **Step 3: Add the classifier**

Create `src/VideoTriage.Core/Probing/BppClassifier.cs`:

```csharp
using System.Globalization;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public sealed class BppClassifier
{
    public ClassificationResult Classify(VideoStats stats, TriageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stats);
        options ??= new TriageOptions();

        if (stats.Width <= 0
            || stats.Height <= 0
            || stats.FramesPerSecond <= 0
            || stats.Duration <= TimeSpan.Zero
            || stats.EffectiveBitrateBitsPerSecond <= 0)
        {
            return new ClassificationResult
            {
                Outcome = ClassificationOutcome.InvalidMetadata,
                Reason = "Invalid metadata: width, height, frame rate, duration, and bitrate must be positive.",
                Stats = stats
            };
        }

        if (options.SkipAv1 && string.Equals(stats.CodecName, "av1", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassificationResult
            {
                Outcome = ClassificationOutcome.SkipAlreadyAv1,
                Reason = "Skipped because the video is already AV1.",
                Stats = stats
            };
        }

        if (stats.BitsPerPixel < options.CandidateBppThreshold)
        {
            return new ClassificationResult
            {
                Outcome = ClassificationOutcome.SkipLowBpp,
                Reason = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Skipped because bpp {stats.BitsPerPixel:0.000} is below threshold {options.CandidateBppThreshold:0.000}."),
                Stats = stats
            };
        }

        return new ClassificationResult
        {
            Outcome = ClassificationOutcome.Candidate,
            Reason = string.Create(
                CultureInfo.InvariantCulture,
                $"Candidate because bpp {stats.BitsPerPixel:0.000} is at or above threshold {options.CandidateBppThreshold:0.000}."),
            Stats = stats
        };
    }
}
```

- [ ] **Step 4: Run the classifier tests to verify green**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter BppClassifierTests
```

Expected: `Passed!` with `Failed: 0`.

- [ ] **Step 5: Commit**

Run:

```bash
git add src/VideoTriage.Core/Probing/BppClassifier.cs tests/VideoTriage.Core.Tests/Probing/BppClassifierTests.cs
git commit -m "feat(core): classify video candidates by bpp"
```

Expected: commit succeeds.

---

### Task 3: Process Runner

**Files:**
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Tools\IProcessRunner.cs`
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Tools\ProcessRequest.cs`
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Tools\ProcessResult.cs`
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Tools\ProcessRunner.cs`
- Test: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\Tools\ProcessRunnerTests.cs`

- [ ] **Step 1: Write the failing process runner tests**

Create `tests/VideoTriage.Core.Tests/Tools/ProcessRunnerTests.cs`:

```csharp
using Shouldly;
using VideoTriage.Core.Tools;
using Xunit;

namespace VideoTriage.Core.Tests.Tools;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesStdout()
    {
        using var temp = new TempDirectory();
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "echo stdout-text"],
            StderrDirectory = temp.Path
        });

        result.Succeeded.ShouldBeTrue();
        result.StandardOutput.ShouldContain("stdout-text");
        File.Exists(result.StandardErrorPath).ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_WritesStderrToFile()
    {
        using var temp = new TempDirectory();
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "echo stderr-text 1>&2"],
            StderrDirectory = temp.Path
        });

        result.Succeeded.ShouldBeTrue();
        File.ReadAllText(result.StandardErrorPath).ShouldContain("stderr-text");
    }

    [Fact]
    public async Task RunAsync_ReturnsNonZeroExitCode()
    {
        using var temp = new TempDirectory();
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "exit /b 7"],
            StderrDirectory = temp.Path
        });

        result.ExitCode.ShouldBe(7);
        result.Succeeded.ShouldBeFalse();
        result.TimedOut.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_TimesOutAndKillsProcess()
    {
        using var temp = new TempDirectory();
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "ping -n 6 127.0.0.1 > nul"],
            Timeout = TimeSpan.FromMilliseconds(200),
            StderrDirectory = temp.Path
        });

        result.TimedOut.ShouldBeTrue();
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_HonorsCancellation()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            new ProcessRunner().RunAsync(new ProcessRequest
            {
                FileName = "cmd.exe",
                Arguments = ["/c", "ping -n 6 127.0.0.1 > nul"],
                Timeout = TimeSpan.FromSeconds(10),
                StderrDirectory = temp.Path
            }, cts.Token));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VideoTriage.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify the red state**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter ProcessRunnerTests
```

Expected: build fails with `CS0234` or `CS0246` because `VideoTriage.Core.Tools.ProcessRunner` does not exist.

- [ ] **Step 3: Add process request and result types**

Create `src/VideoTriage.Core/Tools/ProcessRequest.cs`:

```csharp
namespace VideoTriage.Core.Tools;

public sealed record ProcessRequest
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    public string? StderrDirectory { get; init; }
}
```

Create `src/VideoTriage.Core/Tools/ProcessResult.cs`:

```csharp
namespace VideoTriage.Core.Tools;

public sealed record ProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardErrorPath { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public bool TimedOut { get; init; }

    public bool Succeeded => ExitCode == 0 && !TimedOut;
}
```

Create `src/VideoTriage.Core/Tools/IProcessRunner.cs`:

```csharp
namespace VideoTriage.Core.Tools;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Add process runner**

Create `src/VideoTriage.Core/Tools/ProcessRunner.cs`:

```csharp
using System.Diagnostics;

namespace VideoTriage.Core.Tools;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("Process file name is required.", nameof(request));
        }

        var stderrDirectory = request.StderrDirectory ?? Path.GetTempPath();
        Directory.CreateDirectory(stderrDirectory);
        var stderrPath = Path.Combine(stderrDirectory, $"videotriage-stderr-{Guid.NewGuid():N}.log");

        using var process = new Process
        {
            StartInfo = CreateStartInfo(request)
        };

        var stopwatch = Stopwatch.StartNew();
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = CopyStandardErrorAsync(process, stderrPath);

        var timedOut = false;
        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await stdoutTask;
            await stderrTask;
            throw;
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var stdout = await stdoutTask;
        await stderrTask;

        stopwatch.Stop();

        return new ProcessResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            StandardOutput = stdout,
            StandardErrorPath = stderrPath,
            Elapsed = stopwatch.Elapsed,
            TimedOut = timedOut
        };
    }

    private static ProcessStartInfo CreateStartInfo(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task CopyStandardErrorAsync(Process process, string stderrPath)
    {
        await using var file = new FileStream(stderrPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(file);
        while (await process.StandardError.ReadLineAsync() is { } line)
        {
            await writer.WriteLineAsync(line);
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
```

- [ ] **Step 5: Run the process runner tests to verify green**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter ProcessRunnerTests
```

Expected: `Passed!` with `Failed: 0`.

- [ ] **Step 6: Commit**

Run:

```bash
git add src/VideoTriage.Core/Tools/IProcessRunner.cs src/VideoTriage.Core/Tools/ProcessRequest.cs src/VideoTriage.Core/Tools/ProcessResult.cs src/VideoTriage.Core/Tools/ProcessRunner.cs tests/VideoTriage.Core.Tests/Tools/ProcessRunnerTests.cs
git commit -m "feat(core): add process runner with stderr file capture"
```

Expected: commit succeeds.

---

### Task 4: Tool Locator

**Files:**
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Tools\ToolLocation.cs`
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Tools\ToolLocator.cs`
- Test: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\Tools\ToolLocatorTests.cs`

- [ ] **Step 1: Write the failing tool locator tests**

Create `tests/VideoTriage.Core.Tests/Tools/ToolLocatorTests.cs`:

```csharp
using Shouldly;
using VideoTriage.Core.Tools;
using Xunit;

namespace VideoTriage.Core.Tests.Tools;

public sealed class ToolLocatorTests
{
    [Fact]
    public void FindOnPath_FindsExecutableInInjectedPath()
    {
        using var temp = new TempDirectory();
        var toolPath = System.IO.Path.Combine(temp.Path, "ffprobe.exe");
        File.WriteAllText(toolPath, string.Empty);

        var result = new ToolLocator(pathOverride: temp.Path).FindOnPath("ffprobe");

        result.ShouldBe(toolPath);
    }

    [Fact]
    public void FindOnPath_AcceptsExecutableNameWithExeSuffix()
    {
        using var temp = new TempDirectory();
        var toolPath = System.IO.Path.Combine(temp.Path, "ffprobe.exe");
        File.WriteAllText(toolPath, string.Empty);

        var result = new ToolLocator(pathOverride: temp.Path).FindOnPath("ffprobe.exe");

        result.ShouldBe(toolPath);
    }

    [Fact]
    public void FindOnPath_ReturnsNullWhenMissing()
    {
        using var temp = new TempDirectory();

        var result = new ToolLocator(pathOverride: temp.Path).FindOnPath("ffprobe");

        result.ShouldBeNull();
    }

    [Fact]
    public void RequireOnPath_ThrowsWithToolNameAndHintWhenMissing()
    {
        using var temp = new TempDirectory();

        var exception = Should.Throw<FileNotFoundException>(() =>
            new ToolLocator(pathOverride: temp.Path).RequireOnPath("ffprobe"));

        exception.Message.ShouldContain("ffprobe");
        exception.Message.ShouldContain("PATH");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VideoTriage.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify the red state**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter ToolLocatorTests
```

Expected: build fails with `CS0246` because `ToolLocator` does not exist.

- [ ] **Step 3: Add tool location and locator**

Create `src/VideoTriage.Core/Tools/ToolLocation.cs`:

```csharp
namespace VideoTriage.Core.Tools;

public sealed record ToolLocation
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
}
```

Create `src/VideoTriage.Core/Tools/ToolLocator.cs`:

```csharp
namespace VideoTriage.Core.Tools;

public sealed class ToolLocator
{
    private readonly string? _pathOverride;

    public ToolLocator(string? pathOverride = null)
    {
        _pathOverride = pathOverride;
    }

    public string? FindOnPath(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            throw new ArgumentException("Executable name is required.", nameof(executableName));
        }

        foreach (var directory in GetPathDirectories())
        {
            foreach (var candidateName in GetCandidateNames(executableName))
            {
                var candidatePath = Path.Combine(directory, candidateName);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }
        }

        return null;
    }

    public ToolLocation RequireOnPath(string executableName)
    {
        var fullPath = FindOnPath(executableName);
        if (fullPath is null)
        {
            throw new FileNotFoundException(
                $"Required tool '{executableName}' was not found on PATH. Install ffmpeg/ffprobe and make sure the executable directory is on PATH.");
        }

        return new ToolLocation
        {
            Name = executableName,
            FullPath = fullPath
        };
    }

    private IEnumerable<string> GetPathDirectories()
    {
        var path = _pathOverride ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists);
    }

    private static IEnumerable<string> GetCandidateNames(string executableName)
    {
        yield return executableName;

        if (OperatingSystem.IsWindows()
            && !executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{executableName}.exe";
        }
    }
}
```

- [ ] **Step 4: Run the tool locator tests to verify green**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter ToolLocatorTests
```

Expected: `Passed!` with `Failed: 0`.

- [ ] **Step 5: Commit**

Run:

```bash
git add src/VideoTriage.Core/Tools/ToolLocation.cs src/VideoTriage.Core/Tools/ToolLocator.cs tests/VideoTriage.Core.Tests/Tools/ToolLocatorTests.cs
git commit -m "feat(core): locate external tools on path"
```

Expected: commit succeeds.

---

### Task 5: Ffprobe JSON Parser

**Files:**
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Probing\FfprobeJsonParser.cs`
- Create: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\Probing\FfprobeJsonParserTests.cs`
- Create: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\TestData\Ffprobe\h264-with-audio.json`
- Create: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\TestData\Ffprobe\av1-video.json`
- Create: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\TestData\Ffprobe\missing-video-bitrate.json`
- Create: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\TestData\Ffprobe\stream-duration-missing-format-duration.json`
- Create: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\TestData\Ffprobe\no-video-stream.json`
- Modify: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\VideoTriage.Core.Tests.csproj`

- [ ] **Step 1: Add ffprobe JSON fixtures**

Create `tests/VideoTriage.Core.Tests/TestData/Ffprobe/h264-with-audio.json`:

```json
{
  "streams": [
    {
      "codec_type": "video",
      "codec_name": "h264",
      "width": 1920,
      "height": 1080,
      "avg_frame_rate": "30000/1001",
      "r_frame_rate": "30000/1001",
      "duration": "120.500000",
      "bit_rate": "9000000"
    },
    {
      "codec_type": "audio",
      "codec_name": "aac"
    }
  ],
  "format": {
    "duration": "120.500000",
    "bit_rate": "9500000"
  }
}
```

Create `tests/VideoTriage.Core.Tests/TestData/Ffprobe/av1-video.json`:

```json
{
  "streams": [
    {
      "codec_type": "video",
      "codec_name": "av1",
      "width": 1280,
      "height": 720,
      "avg_frame_rate": "30/1",
      "duration": "60.000000",
      "bit_rate": "3000000"
    }
  ],
  "format": {
    "duration": "60.000000",
    "bit_rate": "3100000"
  }
}
```

Create `tests/VideoTriage.Core.Tests/TestData/Ffprobe/missing-video-bitrate.json`:

```json
{
  "streams": [
    {
      "codec_type": "video",
      "codec_name": "hevc",
      "width": 3840,
      "height": 2160,
      "avg_frame_rate": "60/1",
      "duration": "30.000000"
    }
  ],
  "format": {
    "duration": "30.000000",
    "bit_rate": "45000000"
  }
}
```

Create `tests/VideoTriage.Core.Tests/TestData/Ffprobe/stream-duration-missing-format-duration.json`:

```json
{
  "streams": [
    {
      "codec_type": "video",
      "codec_name": "h264",
      "width": 1920,
      "height": 1080,
      "avg_frame_rate": "24/1",
      "bit_rate": "5000000"
    }
  ],
  "format": {
    "duration": "42.250000",
    "bit_rate": "5500000"
  }
}
```

Create `tests/VideoTriage.Core.Tests/TestData/Ffprobe/no-video-stream.json`:

```json
{
  "streams": [
    {
      "codec_type": "audio",
      "codec_name": "aac"
    }
  ],
  "format": {
    "duration": "12.000000",
    "bit_rate": "192000"
  }
}
```

- [ ] **Step 2: Write the failing parser tests**

Create `tests/VideoTriage.Core.Tests/Probing/FfprobeJsonParserTests.cs`:

```csharp
using System.IO;
using Shouldly;
using VideoTriage.Core.Probing;
using Xunit;

namespace VideoTriage.Core.Tests.Probing;

public sealed class FfprobeJsonParserTests
{
    [Fact]
    public void Parse_ReadsVideoCodecDimensionsFpsDurationBitrateAndAudio()
    {
        var stats = new FfprobeJsonParser().Parse(@"C:\videos\a.mp4", 1_000_000, Fixture("h264-with-audio.json"));

        stats.CodecName.ShouldBe("h264");
        stats.Width.ShouldBe(1920);
        stats.Height.ShouldBe(1080);
        stats.FramesPerSecond.ShouldBe(30000d / 1001d, tolerance: 0.000001);
        stats.Duration.ShouldBe(TimeSpan.FromSeconds(120.5));
        stats.VideoBitrateBitsPerSecond.ShouldBe(9_000_000);
        stats.ContainerBitrateBitsPerSecond.ShouldBe(9_500_000);
        stats.HasAudio.ShouldBeTrue();
        stats.FileSizeBytes.ShouldBe(1_000_000);
    }

    [Fact]
    public void Parse_PreservesAv1CodecName()
    {
        var stats = new FfprobeJsonParser().Parse(@"C:\videos\av1.mp4", 1_000_000, Fixture("av1-video.json"));

        stats.CodecName.ShouldBe("av1");
        stats.HasAudio.ShouldBeFalse();
    }

    [Fact]
    public void Parse_UsesContainerBitrateFallback()
    {
        var stats = new FfprobeJsonParser().Parse(@"C:\videos\b.mov", 1_000_000, Fixture("missing-video-bitrate.json"));

        stats.VideoBitrateBitsPerSecond.ShouldBeNull();
        stats.ContainerBitrateBitsPerSecond.ShouldBe(45_000_000);
        stats.EffectiveBitrateBitsPerSecond.ShouldBe(45_000_000);
    }

    [Fact]
    public void Parse_UsesFormatDurationFallback()
    {
        var stats = new FfprobeJsonParser().Parse(@"C:\videos\c.mkv", 1_000_000, Fixture("stream-duration-missing-format-duration.json"));

        stats.Duration.ShouldBe(TimeSpan.FromSeconds(42.25));
    }

    [Fact]
    public void Parse_ThrowsWhenNoVideoStream()
    {
        var exception = Should.Throw<InvalidDataException>(() =>
            new FfprobeJsonParser().Parse(@"C:\videos\audio.m4a", 1_000_000, Fixture("no-video-stream.json")));

        exception.Message.ShouldContain("video stream");
    }

    [Fact]
    public void Parse_ThrowsWhenJsonInvalid()
    {
        Should.Throw<InvalidDataException>(() =>
            new FfprobeJsonParser().Parse(@"C:\videos\bad.mp4", 1_000_000, "{"));
    }

    [Fact]
    public void Parse_ParsesZeroSlashZeroFrameRateAsInvalid()
    {
        const string json = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "h264",
              "width": 1920,
              "height": 1080,
              "avg_frame_rate": "0/0",
              "duration": "5.0",
              "bit_rate": "1000000"
            }
          ],
          "format": { "duration": "5.0" }
        }
        """;

        var exception = Should.Throw<InvalidDataException>(() =>
            new FfprobeJsonParser().Parse(@"C:\videos\badfps.mp4", 1_000_000, json));

        exception.Message.ShouldContain("frame rate");
    }

    private static string Fixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffprobe", fileName);
        return File.ReadAllText(path);
    }
}
```

- [ ] **Step 3: Copy test data to the test output**

Replace `tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\VideoTriage.Core\VideoTriage.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="TestData\**\*.*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Run the tests to verify the red state**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter FfprobeJsonParserTests
```

Expected: build fails with `CS0246` because `FfprobeJsonParser` does not exist.

- [ ] **Step 5: Add the parser**

Create `src/VideoTriage.Core/Probing/FfprobeJsonParser.cs`:

```csharp
using System.Globalization;
using System.IO;
using System.Text.Json;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public sealed class FfprobeJsonParser
{
    public VideoStats Parse(string filePath, long fileSizeBytes, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var streams = root.GetProperty("streams").EnumerateArray().ToArray();
            var video = streams.FirstOrDefault(IsVideoStream);

            if (video.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidDataException("ffprobe JSON does not contain a video stream.");
            }

            return new VideoStats
            {
                FilePath = filePath,
                CodecName = RequiredString(video, "codec_name"),
                Width = RequiredPositiveInt(video, "width"),
                Height = RequiredPositiveInt(video, "height"),
                FramesPerSecond = RequiredFrameRate(video),
                Duration = RequiredDuration(video, root),
                FileSizeBytes = fileSizeBytes,
                VideoBitrateBitsPerSecond = OptionalLong(video, "bit_rate"),
                ContainerBitrateBitsPerSecond = TryGetFormat(root, out var format)
                    ? OptionalLong(format, "bit_rate")
                    : null,
                HasAudio = streams.Any(IsAudioStream)
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("ffprobe JSON is invalid.", exception);
        }
    }

    private static bool IsVideoStream(JsonElement stream) =>
        stream.TryGetProperty("codec_type", out var codecType)
        && string.Equals(codecType.GetString(), "video", StringComparison.OrdinalIgnoreCase);

    private static bool IsAudioStream(JsonElement stream) =>
        stream.TryGetProperty("codec_type", out var codecType)
        && string.Equals(codecType.GetString(), "audio", StringComparison.OrdinalIgnoreCase);

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"ffprobe JSON is missing required property '{propertyName}'.");
        }

        return property.GetString()!;
    }

    private static int RequiredPositiveInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || !property.TryGetInt32(out var value) || value <= 0)
        {
            throw new InvalidDataException($"ffprobe JSON has invalid positive integer property '{propertyName}'.");
        }

        return value;
    }

    private static double RequiredFrameRate(JsonElement video)
    {
        var raw = OptionalString(video, "avg_frame_rate");
        if (string.IsNullOrWhiteSpace(raw) || raw == "0/0")
        {
            raw = OptionalString(video, "r_frame_rate");
        }

        var frameRate = ParseRational(raw);
        if (frameRate <= 0)
        {
            throw new InvalidDataException("ffprobe JSON has invalid frame rate.");
        }

        return frameRate;
    }

    private static TimeSpan RequiredDuration(JsonElement video, JsonElement root)
    {
        var seconds = OptionalDouble(video, "duration");
        if (seconds is null && TryGetFormat(root, out var format))
        {
            seconds = OptionalDouble(format, "duration");
        }

        if (seconds is null or <= 0)
        {
            throw new InvalidDataException("ffprobe JSON has invalid duration.");
        }

        return TimeSpan.FromSeconds(seconds.Value);
    }

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;

    private static long? OptionalLong(JsonElement element, string propertyName)
    {
        var raw = OptionalString(element, propertyName);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double? OptionalDouble(JsonElement element, string propertyName)
    {
        var raw = OptionalString(element, propertyName);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double ParseRational(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        var parts = raw.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            && denominator != 0)
        {
            return numerator / denominator;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static bool TryGetFormat(JsonElement root, out JsonElement format) =>
        root.TryGetProperty("format", out format) && format.ValueKind == JsonValueKind.Object;
}
```

- [ ] **Step 6: Run the parser tests to verify green**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter FfprobeJsonParserTests
```

Expected: `Passed!` with `Failed: 0`.

- [ ] **Step 7: Commit**

Run:

```bash
git add src/VideoTriage.Core/Probing/FfprobeJsonParser.cs tests/VideoTriage.Core.Tests/Probing/FfprobeJsonParserTests.cs tests/VideoTriage.Core.Tests/TestData tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj
git commit -m "feat(core): parse ffprobe video metadata"
```

Expected: commit succeeds.

---

### Task 6: Ffprobe Service

**Files:**
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Probing\IFfprobeService.cs`
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Probing\FfprobeService.cs`
- Test: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\Probing\FfprobeServiceTests.cs`

- [ ] **Step 1: Write the failing service tests**

Create `tests/VideoTriage.Core.Tests/Probing/FfprobeServiceTests.cs`:

```csharp
using Shouldly;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Tools;
using Xunit;

namespace VideoTriage.Core.Tests.Probing;

public sealed class FfprobeServiceTests
{
    [Fact]
    public async Task ProbeAsync_ReturnsFailureWhenFileDoesNotExist()
    {
        var service = new FfprobeService("ffprobe.exe", new FakeProcessRunner(), new FfprobeJsonParser());

        var result = await service.ProbeAsync(@"C:\missing\video.mp4");

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldNotBeNull();
        result.Failure.Message.ShouldContain("does not exist");
    }

    [Fact]
    public async Task ProbeAsync_BuildsFfprobeCommandAndParsesStats()
    {
        using var temp = new TempVideoFile();
        var runner = new FakeProcessRunner { Result = SuccessfulResult(Fixture("h264-with-audio.json")) };
        var service = new FfprobeService(@"C:\tools\ffprobe.exe", runner, new FfprobeJsonParser());

        var result = await service.ProbeAsync(temp.Path);

        result.Succeeded.ShouldBeTrue();
        result.Stats.ShouldNotBeNull();
        result.Stats.FilePath.ShouldBe(temp.Path);
        result.Stats.FileSizeBytes.ShouldBe(new FileInfo(temp.Path).Length);
        runner.Requests.Single().FileName.ShouldBe(@"C:\tools\ffprobe.exe");
        runner.Requests.Single().Arguments.ShouldBe(
            new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", temp.Path });
    }

    [Fact]
    public async Task ProbeAsync_ReturnsFailureWhenFfprobeExitCodeIsNonZero()
    {
        using var temp = new TempVideoFile();
        var service = new FfprobeService("ffprobe.exe", new FakeProcessRunner
        {
            Result = new ProcessResult
            {
                ExitCode = 2,
                StandardOutput = string.Empty,
                StandardErrorPath = @"C:\temp\ffprobe.err",
                Elapsed = TimeSpan.FromMilliseconds(12)
            }
        }, new FfprobeJsonParser());

        var result = await service.ProbeAsync(temp.Path);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldNotBeNull();
        result.Failure.ExitCode.ShouldBe(2);
        result.Failure.StderrPath.ShouldBe(@"C:\temp\ffprobe.err");
    }

    [Fact]
    public async Task ProbeAsync_ReturnsFailureWhenJsonCannotBeParsed()
    {
        using var temp = new TempVideoFile();
        var service = new FfprobeService("ffprobe.exe", new FakeProcessRunner
        {
            Result = SuccessfulResult("{")
        }, new FfprobeJsonParser());

        var result = await service.ProbeAsync(temp.Path);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldNotBeNull();
        result.Failure.Message.ShouldContain("ffprobe JSON");
    }

    private static ProcessResult SuccessfulResult(string stdout) =>
        new()
        {
            ExitCode = 0,
            StandardOutput = stdout,
            StandardErrorPath = @"C:\temp\empty.err",
            Elapsed = TimeSpan.FromMilliseconds(10)
        };

    private static string Fixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffprobe", fileName);
        return File.ReadAllText(path);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];
        public ProcessResult Result { get; init; } = SuccessfulResult("{}");

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class TempVideoFile : IDisposable
    {
        public TempVideoFile()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"videotriage-{Guid.NewGuid():N}.mp4");
            File.WriteAllBytes(Path, [1, 2, 3, 4]);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify the red state**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter FfprobeServiceTests
```

Expected: build fails with `CS0246` because `FfprobeService` and `IFfprobeService` do not exist.

- [ ] **Step 3: Add `IFfprobeService`**

Create `src/VideoTriage.Core/Probing/IFfprobeService.cs`:

```csharp
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public interface IFfprobeService
{
    Task<ProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Add `FfprobeService`**

Create `src/VideoTriage.Core/Probing/FfprobeService.cs`:

```csharp
using System.IO;
using VideoTriage.Core.Models;
using VideoTriage.Core.Tools;

namespace VideoTriage.Core.Probing;

public sealed class FfprobeService : IFfprobeService
{
    private readonly string _ffprobePath;
    private readonly IProcessRunner _processRunner;
    private readonly FfprobeJsonParser _parser;

    public FfprobeService(string ffprobePath, IProcessRunner processRunner, FfprobeJsonParser parser)
    {
        if (string.IsNullOrWhiteSpace(ffprobePath))
        {
            throw new ArgumentException("ffprobe path is required.", nameof(ffprobePath));
        }

        _ffprobePath = ffprobePath;
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<ProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return Failure(filePath, $"File does not exist: {filePath}");
        }

        var processResult = await _processRunner.RunAsync(new ProcessRequest
        {
            FileName = _ffprobePath,
            Arguments = ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", filePath],
            Timeout = TimeSpan.FromSeconds(30)
        }, cancellationToken);

        if (!processResult.Succeeded)
        {
            return Failure(
                filePath,
                $"ffprobe failed with exit code {processResult.ExitCode}.",
                processResult.ExitCode,
                processResult.StandardErrorPath);
        }

        try
        {
            var fileSizeBytes = new FileInfo(filePath).Length;
            var stats = _parser.Parse(filePath, fileSizeBytes, processResult.StandardOutput);
            return new ProbeResult
            {
                FilePath = filePath,
                Stats = stats
            };
        }
        catch (InvalidDataException exception)
        {
            return Failure(filePath, exception.Message, stderrPath: processResult.StandardErrorPath);
        }
    }

    private static ProbeResult Failure(string filePath, string message, int? exitCode = null, string? stderrPath = null) =>
        new()
        {
            FilePath = filePath,
            Failure = new ProbeFailure
            {
                FilePath = filePath,
                Message = message,
                ExitCode = exitCode,
                StderrPath = stderrPath
            }
        };
}
```

- [ ] **Step 5: Run the service tests to verify green**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter FfprobeServiceTests
```

Expected: `Passed!` with `Failed: 0`.

- [ ] **Step 6: Commit**

Run:

```bash
git add src/VideoTriage.Core/Probing/IFfprobeService.cs src/VideoTriage.Core/Probing/FfprobeService.cs tests/VideoTriage.Core.Tests/Probing/FfprobeServiceTests.cs
git commit -m "feat(core): probe videos with ffprobe"
```

Expected: commit succeeds.

---

### Task 7: Video File Discovery

**Files:**
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\FileSystem\VideoFileDiscovery.cs`
- Test: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\FileSystem\VideoFileDiscoveryTests.cs`

- [ ] **Step 1: Write the failing file discovery tests**

Create `tests/VideoTriage.Core.Tests/FileSystem/VideoFileDiscoveryTests.cs`:

```csharp
using Shouldly;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using Xunit;

namespace VideoTriage.Core.Tests.FileSystem;

public sealed class VideoFileDiscoveryTests
{
    [Fact]
    public void FindVideos_FindsDefaultVideoExtensions()
    {
        using var temp = new TempDirectory();
        temp.File("a.mp4");
        temp.File("b.MOV");
        temp.File("notes.txt");

        var results = new VideoFileDiscovery().FindVideos(temp.Path);

        results.Select(Path.GetFileName).ShouldBe(new[] { "a.mp4", "b.MOV" });
    }

    [Fact]
    public void FindVideos_HonorsCustomExtensionList()
    {
        using var temp = new TempDirectory();
        temp.File("a.custom");
        temp.File("b.mp4");

        var results = new VideoFileDiscovery().FindVideos(
            temp.Path,
            new TriageOptions { VideoExtensions = [".custom"] });

        results.Select(Path.GetFileName).ShouldBe(new[] { "a.custom" });
    }

    [Fact]
    public void FindVideos_RecursiveFalseIgnoresNestedFiles()
    {
        using var temp = new TempDirectory();
        temp.File("root.mp4");
        temp.File(Path.Combine("nested", "child.mp4"));

        var results = new VideoFileDiscovery().FindVideos(temp.Path, recursive: false);

        results.Select(Path.GetFileName).ShouldBe(new[] { "root.mp4" });
    }

    [Fact]
    public void FindVideos_RecursiveTrueIncludesNestedFiles()
    {
        using var temp = new TempDirectory();
        temp.File("root.mp4");
        temp.File(Path.Combine("nested", "child.mp4"));

        var results = new VideoFileDiscovery().FindVideos(temp.Path, recursive: true);

        results.Select(Path.GetFileName).ShouldBe(new[] { "child.mp4", "root.mp4" }, ignoreOrder: false);
    }

    [Fact]
    public void FindVideos_IgnoresVideoTriageTempFiles()
    {
        using var temp = new TempDirectory();
        temp.File("keep.mp4");
        temp.File("skip.videotriage.tmp.mp4");
        temp.File("skip.videotriage.partial.mp4");

        var results = new VideoFileDiscovery().FindVideos(temp.Path);

        results.Select(Path.GetFileName).ShouldBe(new[] { "keep.mp4" });
    }

    [Fact]
    public void FindVideos_ThrowsWhenFolderDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Should.Throw<DirectoryNotFoundException>(() =>
            new VideoFileDiscovery().FindVideos(missing));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VideoTriage.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void File(string relativePath)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            System.IO.File.WriteAllText(fullPath, string.Empty);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify the red state**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter VideoFileDiscoveryTests
```

Expected: build fails with `CS0234` or `CS0246` because `VideoTriage.Core.FileSystem.VideoFileDiscovery` does not exist.

- [ ] **Step 3: Add file discovery**

Create `src/VideoTriage.Core/FileSystem/VideoFileDiscovery.cs`:

```csharp
using VideoTriage.Core.Models;

namespace VideoTriage.Core.FileSystem;

public sealed class VideoFileDiscovery
{
    public IReadOnlyList<string> FindVideos(string folderPath, TriageOptions? options = null, bool recursive = false)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder does not exist: {folderPath}");
        }

        options ??= new TriageOptions();
        var extensions = options.VideoExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        return Directory
            .EnumerateFiles(folderPath, "*", searchOption)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Where(path => !Path.GetFileName(path).Contains(".videotriage.tmp.", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Contains(".videotriage.partial.", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
```

- [ ] **Step 4: Run the discovery tests to verify green**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter VideoFileDiscoveryTests
```

Expected: `Passed!` with `Failed: 0`.

- [ ] **Step 5: Commit**

Run:

```bash
git add src/VideoTriage.Core/FileSystem/VideoFileDiscovery.cs tests/VideoTriage.Core.Tests/FileSystem/VideoFileDiscoveryTests.cs
git commit -m "feat(core): discover video files"
```

Expected: commit succeeds.

---

### Task 8: Folder Probe Scanner

**Files:**
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Core\Probing\FolderProbeScanner.cs`
- Test: `C:\Agent Projects\VideoTriage\tests\VideoTriage.Core.Tests\Probing\FolderProbeScannerTests.cs`

- [ ] **Step 1: Write the failing scanner tests**

Create `tests/VideoTriage.Core.Tests/Probing/FolderProbeScannerTests.cs`:

```csharp
using Shouldly;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using Xunit;

namespace VideoTriage.Core.Tests.Probing;

public sealed class FolderProbeScannerTests
{
    [Fact]
    public async Task ScanAsync_ClassifiesSuccessfulProbes()
    {
        using var temp = new TempDirectory();
        var file = temp.File("candidate.mp4");
        var service = new FakeFfprobeService
        {
            Results =
            {
                [file] = Success(file, bpp: 0.20)
            }
        };

        var results = await CreateScanner(service).ScanAsync(temp.Path);

        results.Single().Classification.ShouldNotBeNull();
        results.Single().Classification!.Outcome.ShouldBe(ClassificationOutcome.Candidate);
    }

    [Fact]
    public async Task ScanAsync_PreservesProbeFailureAndContinues()
    {
        using var temp = new TempDirectory();
        var bad = temp.File("bad.mp4");
        var good = temp.File("good.mp4");
        var service = new FakeFfprobeService
        {
            Results =
            {
                [bad] = Failure(bad, "bad metadata"),
                [good] = Success(good, bpp: 0.20)
            }
        };

        var results = await CreateScanner(service).ScanAsync(temp.Path);

        results.Count.ShouldBe(2);
        results[0].Failure.ShouldNotBeNull();
        results[1].Classification!.Outcome.ShouldBe(ClassificationOutcome.Candidate);
    }

    [Fact]
    public async Task ScanAsync_ReportsProgressOncePerCompletedFile()
    {
        using var temp = new TempDirectory();
        var first = temp.File("a.mp4");
        var second = temp.File("b.mp4");
        var service = new FakeFfprobeService
        {
            Results =
            {
                [first] = Success(first, bpp: 0.20),
                [second] = Success(second, bpp: 0.10)
            }
        };
        var progressResults = new List<ProbeResult>();

        await CreateScanner(service).ScanAsync(
            temp.Path,
            progress: new InlineProgress<ProbeResult>(progressResults.Add));

        progressResults.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ScanAsync_PreservesSortedDiscoveryOrder()
    {
        using var temp = new TempDirectory();
        var second = temp.File("z.mp4");
        var first = temp.File("a.mp4");
        var service = new FakeFfprobeService
        {
            Results =
            {
                [first] = Success(first, bpp: 0.20),
                [second] = Success(second, bpp: 0.20)
            }
        };

        var results = await CreateScanner(service).ScanAsync(temp.Path);

        results.Select(result => Path.GetFileName(result.FilePath)).ShouldBe(new[] { "a.mp4", "z.mp4" });
    }

    [Fact]
    public async Task ScanAsync_HonorsCancellationBeforeRemainingFiles()
    {
        using var temp = new TempDirectory();
        var first = temp.File("a.mp4");
        temp.File("b.mp4");
        using var cts = new CancellationTokenSource();
        var service = new FakeFfprobeService
        {
            Results =
            {
                [first] = Success(first, bpp: 0.20)
            },
            CancelAfterFirstProbe = cts
        };

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateScanner(service).ScanAsync(temp.Path, cancellationToken: cts.Token));
    }

    private static FolderProbeScanner CreateScanner(IFfprobeService service) =>
        new(new VideoFileDiscovery(), service, new BppClassifier());

    private static ProbeResult Success(string filePath, double bpp) =>
        new()
        {
            FilePath = filePath,
            Stats = new VideoStats
            {
                FilePath = filePath,
                CodecName = "h264",
                Width = 1920,
                Height = 1080,
                FramesPerSecond = 30,
                Duration = TimeSpan.FromSeconds(60),
                FileSizeBytes = 30_000_000,
                VideoBitrateBitsPerSecond = (long)Math.Round(bpp * 1920 * 1080 * 30),
                HasAudio = true
            }
        };

    private static ProbeResult Failure(string filePath, string message) =>
        new()
        {
            FilePath = filePath,
            Failure = new ProbeFailure
            {
                FilePath = filePath,
                Message = message
            }
        };

    private sealed class FakeFfprobeService : IFfprobeService
    {
        private int _probeCount;
        public Dictionary<string, ProbeResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);
        public CancellationTokenSource? CancelAfterFirstProbe { get; init; }

        public Task<ProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _probeCount++;
            if (_probeCount == 1)
            {
                CancelAfterFirstProbe?.Cancel();
            }

            return Task.FromResult(Results[filePath]);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VideoTriage.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string relativePath)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            System.IO.File.WriteAllText(fullPath, string.Empty);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify the red state**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter FolderProbeScannerTests
```

Expected: build fails with `CS0246` because `FolderProbeScanner` does not exist.

- [ ] **Step 3: Add scanner**

Create `src/VideoTriage.Core/Probing/FolderProbeScanner.cs`:

```csharp
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Probing;

public sealed class FolderProbeScanner
{
    private readonly VideoFileDiscovery _discovery;
    private readonly IFfprobeService _ffprobeService;
    private readonly BppClassifier _classifier;

    public FolderProbeScanner(
        VideoFileDiscovery discovery,
        IFfprobeService ffprobeService,
        BppClassifier classifier)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _ffprobeService = ffprobeService ?? throw new ArgumentNullException(nameof(ffprobeService));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    public async Task<IReadOnlyList<ProbeResult>> ScanAsync(
        string folderPath,
        TriageOptions? options = null,
        bool recursive = false,
        IProgress<ProbeResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TriageOptions();
        var results = new List<ProbeResult>();

        foreach (var filePath in _discovery.FindVideos(folderPath, options, recursive))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var probeResult = await _ffprobeService.ProbeAsync(filePath, cancellationToken);
            var completedResult = probeResult.Stats is null
                ? probeResult
                : probeResult with { Classification = _classifier.Classify(probeResult.Stats, options) };

            results.Add(completedResult);
            progress?.Report(completedResult);
        }

        return results;
    }
}
```

- [ ] **Step 4: Run the scanner tests to verify green**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter FolderProbeScannerTests
```

Expected: `Passed!` with `Failed: 0`.

- [ ] **Step 5: Commit**

Run:

```bash
git add src/VideoTriage.Core/Probing/FolderProbeScanner.cs tests/VideoTriage.Core.Tests/Probing/FolderProbeScannerTests.cs
git commit -m "feat(core): scan folders for probe classifications"
```

Expected: commit succeeds.

---

### Task 9: Non-Destructive Console Harness

**Files:**
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Cli\VideoTriage.Cli.csproj`
- Create: `C:\Agent Projects\VideoTriage\src\VideoTriage.Cli\Program.cs`
- Modify: `C:\Agent Projects\VideoTriage\VideoTriage.sln`

- [ ] **Step 1: Create the console project**

Run:

```bash
dotnet new console -n VideoTriage.Cli -o src/VideoTriage.Cli -f net10.0
dotnet sln add src/VideoTriage.Cli/VideoTriage.Cli.csproj
```

Expected: console project is created and added to the solution.

- [ ] **Step 2: Replace the CLI project file**

Replace `src/VideoTriage.Cli/VideoTriage.Cli.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\VideoTriage.Core\VideoTriage.Core.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: Run the CLI acceptance check to verify the red state**

Run:

```powershell
$output = dotnet run --project src/VideoTriage.Cli 2>&1
if ($output -notmatch 'Usage: VideoTriage.Cli <folder> \[--recursive\]') {
    throw "CLI usage contract is not implemented."
}
```

Expected: command throws `CLI usage contract is not implemented.` because the template still prints `Hello, World!`.

- [ ] **Step 4: Replace the CLI program**

Replace `src/VideoTriage.Cli/Program.cs` with:

```csharp
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Tools;

if (args.Length is 0 or > 2
    || (args.Length == 2 && !string.Equals(args[1], "--recursive", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("Usage: VideoTriage.Cli <folder> [--recursive]");
    return 2;
}

var folderPath = args[0];
var recursive = args.Any(arg => string.Equals(arg, "--recursive", StringComparison.OrdinalIgnoreCase));

if (!Directory.Exists(folderPath))
{
    Console.Error.WriteLine($"Folder does not exist: {folderPath}");
    return 2;
}

ToolLocation ffprobe;
try
{
    ffprobe = new ToolLocator().RequireOnPath("ffprobe");
}
catch (FileNotFoundException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}

var scanner = new FolderProbeScanner(
    new VideoFileDiscovery(),
    new FfprobeService(ffprobe.FullPath, new ProcessRunner(), new FfprobeJsonParser()),
    new BppClassifier());

Console.WriteLine("VideoTriage probe/classify scan");
Console.WriteLine($"Folder: {folderPath}");
Console.WriteLine($"Recursive: {recursive}");
Console.WriteLine();

var options = new TriageOptions();
var results = await scanner.ScanAsync(folderPath, options, recursive);

foreach (var result in results)
{
    if (result.Failure is not null)
    {
        Console.WriteLine($"INVALID\t{result.FilePath}\t{result.Failure.Message}");
        continue;
    }

    var classification = result.Classification!;
    var stats = result.Stats!;
    Console.WriteLine(
        $"{classification.Outcome}\t{stats.BitsPerPixel:0.000}\t{stats.CodecName}\t{stats.Width}x{stats.Height}\t{result.FilePath}");
}

var candidates = results.Count(result => result.Classification?.Outcome == ClassificationOutcome.Candidate);
Console.WriteLine();
Console.WriteLine($"Scanned: {results.Count}");
Console.WriteLine($"Candidates: {candidates}");
return 0;
```

- [ ] **Step 5: Build the CLI**

Run:

```bash
dotnet build src/VideoTriage.Cli/VideoTriage.Cli.csproj -c Debug
```

Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 6: Run the CLI with no arguments**

Run:

```powershell
$output = dotnet run --project src/VideoTriage.Cli --no-build 2>&1
if ($output -notmatch 'Usage: VideoTriage.Cli <folder> \[--recursive\]') {
    throw "CLI usage contract failed."
}
```

Expected: command completes without throwing and output contains `Usage: VideoTriage.Cli <folder> [--recursive]`.

- [ ] **Step 7: Commit**

Run:

```bash
git add VideoTriage.sln src/VideoTriage.Cli
git commit -m "feat(cli): add non-destructive probe scan harness"
```

Expected: commit succeeds.

---

### Task 10: README Status And Manual Verification

**Files:**
- Modify: `C:\Agent Projects\VideoTriage\README.md`

- [ ] **Step 1: Update README status and harness usage**

Replace the `## Status` section in `README.md` with:

````markdown
## Status
- [x] Scaffold + Fluent shell
- [x] Core probe/classify scan API
- [ ] Core engine (verify / safe-replace)
- [ ] UI wiring + live progress
- [ ] Embedded poster thumbnails

## Non-Destructive Probe Scan

M2 includes a console harness that reads a folder, probes videos with `ffprobe`, and prints
candidate classifications. It does not encode, replace, or delete files.

```bash
dotnet run --project src/VideoTriage.Cli -- "D:\Videos\Captures"
dotnet run --project src/VideoTriage.Cli -- "D:\Videos\Captures" --recursive
```
````

- [ ] **Step 2: Run the README-related build check**

Run:

```bash
dotnet build VideoTriage.sln -c Debug
```

Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 3: Commit**

Run:

```bash
git add README.md
git commit -m "docs: document probe classify milestone"
```

Expected: commit succeeds.

---

### Task 11: CI-Equivalent Verification

**Files:**
- No source edits.

- [ ] **Step 1: Inspect the working tree**

Run:

```bash
git status --short
```

Expected: no unexpected files. Expected uncommitted files only appear if a previous commit step failed.

- [ ] **Step 2: Restore**

Run:

```bash
dotnet restore VideoTriage.sln
```

Expected: restore succeeds with `0 Error(s)`.

- [ ] **Step 3: Build Release**

Run:

```bash
dotnet build VideoTriage.sln -c Release --no-restore
```

Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 4: Run Release tests**

Run:

```bash
dotnet test tests/VideoTriage.Core.Tests -c Release --no-build --verbosity normal
```

Expected: all tests pass.

- [ ] **Step 5: Verify the non-destructive CLI usage path**

Run:

```bash
dotnet run --project src/VideoTriage.Cli -c Release --no-build
```

Expected: exit code `2`; output contains `Usage: VideoTriage.Cli <folder> [--recursive]`.

---

## Self-Review

**1. Spec coverage:** M2 requires `FfprobeService`, `BppClassifier`, `ToolLocator`, and `ProcessRunner`; Tasks 2, 3, 4, and 6 create those. The broad design also calls for a console/test harness that lists candidates for a folder; Task 9 adds `VideoTriage.Cli` and Task 11 verifies its usage path. The non-destructive safety rule is maintained throughout the plan.

**2. Placeholder scan:** The plan contains no prohibited placeholder language, wildcard file paths, or incomplete code bodies. Every code-changing step includes concrete code or an exact command that creates the file.

**3. Type consistency:** `VideoStats`, `ClassificationResult`, `ProbeResult`, and `ProbeFailure` are defined in Task 1 and used consistently later. `BppClassifier.Classify(VideoStats, TriageOptions?)`, `IProcessRunner.RunAsync(ProcessRequest, CancellationToken)`, `IFfprobeService.ProbeAsync(string, CancellationToken)`, and `FolderProbeScanner.ScanAsync(...)` use the same names and signatures in tests, implementations, and CLI wiring.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-07-core-probe-classify.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints.

This plan is complete on `main`; no execution handoff remains.
