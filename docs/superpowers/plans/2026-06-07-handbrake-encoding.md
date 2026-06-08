# HandBrake Encoding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Encode one source video through HandBrakeCLI with the approved AV1 preset while reporting deterministic progress and preserving cancellation.

**Architecture:** Extend `ProcessRunner` with line-level stdout reporting, then build a pure HandBrake JSON progress parser and an `IVideoEncoder` adapter. The encoder only creates a candidate file; it never verifies, replaces, or deletes sources.

**Tech Stack:** .NET 10, `System.Diagnostics.Process`, `System.Text.Json`, xUnit, Shouldly, HandBrakeCLI for the explicit local smoke check.

---

## Scope Check

This plan owns process stdout streaming, HandBrake progress parsing, the preset asset, and candidate
encoding. Verification, replacement, free-space checks, and pipeline policy are separate plans.

## File Structure

```text
src/VideoTriage.Core/
  Models/EncodeResult.cs
  Tools/ProcessRequest.cs
  Tools/ProcessRunner.cs
  Encoding/HandBrakeProgressParser.cs
  Encoding/IVideoEncoder.cs
  Encoding/HandBrakeEncoder.cs
  Encoding/Assets/videotriage-av1.json
tests/VideoTriage.Core.Tests/
  Tools/ProcessRunnerStreamingTests.cs
  Encoding/HandBrakeProgressParserTests.cs
  Encoding/HandBrakeEncoderTests.cs
```

### Task 1: Stream Process Stdout

**Files:**
- Modify: `src/VideoTriage.Core/Tools/ProcessRequest.cs`
- Modify: `src/VideoTriage.Core/Tools/ProcessRunner.cs`
- Create: `tests/VideoTriage.Core.Tests/Tools/ProcessRunnerStreamingTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Shouldly;
using VideoTriage.Core.Tools;

namespace VideoTriage.Core.Tests.Tools;

public sealed class ProcessRunnerStreamingTests
{
    [Fact]
    public async Task RunAsync_ReportsEveryStdoutLineAndReturnsFullText()
    {
        var lines = new List<string>();
        var progress = new InlineProgress<string>(lines.Add);
        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "cmd.exe",
            Arguments = ["/c", "echo first&echo second"],
            StandardOutputLines = progress
        });

        lines.ShouldBe(["first", "second"]);
        result.StandardOutput.ShouldContain("first");
        result.StandardOutput.ShouldContain("second");
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
```

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter ProcessRunnerStreamingTests`

Expected: `CS0117` because `ProcessRequest.StandardOutputLines` does not exist.

- [ ] **Step 3: Add the request property**

Add to `ProcessRequest`:

```csharp
public IProgress<string>? StandardOutputLines { get; init; }
```

- [ ] **Step 4: Replace stdout pumping in `ProcessRunner`**

```csharp
private static async Task<string> ReadStandardOutputAsync(
    Process process,
    IProgress<string>? progress)
{
    var output = new System.Text.StringBuilder();
    while (await process.StandardOutput.ReadLineAsync() is { } line)
    {
        progress?.Report(line);
        output.AppendLine(line);
    }

    return output.ToString();
}
```

Replace `ReadToEndAsync()` with:

```csharp
var stdoutTask = ReadStandardOutputAsync(process, request.StandardOutputLines);
```

- [ ] **Step 5: Run green**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter "ProcessRunnerStreamingTests|ProcessRunnerTests"`

Expected: all selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src/VideoTriage.Core/Tools tests/VideoTriage.Core.Tests/Tools/ProcessRunnerStreamingTests.cs
git commit -m "feat(core): stream process stdout lines"
```

### Task 2: Parse HandBrake Progress

**Files:**
- Create: `src/VideoTriage.Core/Encoding/HandBrakeProgressParser.cs`
- Create: `tests/VideoTriage.Core.Tests/Encoding/HandBrakeProgressParserTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Shouldly;
using VideoTriage.Core.Encoding;

namespace VideoTriage.Core.Tests.Encoding;

public sealed class HandBrakeProgressParserTests
{
    [Theory]
    [InlineData("""{"State":"WORKING","Working":{"Progress":0.43}}""", 0.43)]
    [InlineData("""{"Working":{"Progress":1.2}}""", 1.0)]
    [InlineData("""{"Working":{"Progress":-1}}""", 0.0)]
    public void TryParseProgress_ValidJson_ReturnsClampedValue(string line, double expected) =>
        HandBrakeProgressParser.TryParseProgress(line).ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("Encoding: task 1")]
    [InlineData("""{"State":"WORKING"}""")]
    public void TryParseProgress_NonProgressLine_ReturnsNull(string line) =>
        HandBrakeProgressParser.TryParseProgress(line).ShouldBeNull();
}
```

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter HandBrakeProgressParserTests`

Expected: `CS0234` because the parser does not exist.

- [ ] **Step 3: Implement the parser**

```csharp
using System.Text.Json;

namespace VideoTriage.Core.Encoding;

public static class HandBrakeProgressParser
{
    public static double? TryParseProgress(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{')
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("Working", out var working) ||
                !working.TryGetProperty("Progress", out var progress) ||
                !progress.TryGetDouble(out var value))
            {
                return null;
            }

            return Math.Clamp(value, 0, 1);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Run green and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter HandBrakeProgressParserTests
git add src/VideoTriage.Core/Encoding/HandBrakeProgressParser.cs tests/VideoTriage.Core.Tests/Encoding/HandBrakeProgressParserTests.cs
git commit -m "feat(core): parse HandBrake JSON progress"
```

Expected: tests pass; commit succeeds.

### Task 3: Encode A Candidate

**Files:**
- Create: `src/VideoTriage.Core/Models/EncodeResult.cs`
- Create: `src/VideoTriage.Core/Encoding/IVideoEncoder.cs`
- Create: `src/VideoTriage.Core/Encoding/HandBrakeEncoder.cs`
- Create: `tests/VideoTriage.Core.Tests/Encoding/HandBrakeEncoderTests.cs`

- [ ] **Step 1: Write the failing happy-path test**

```csharp
using Shouldly;
using VideoTriage.Core.Encoding;
using VideoTriage.Core.Tools;

namespace VideoTriage.Core.Tests.Encoding;

public sealed class HandBrakeEncoderTests
{
    [Fact]
    public async Task EncodeAsync_BuildsPresetCommandAndReportsProgress()
    {
        var runner = new FakeRunner();
        var values = new List<double>();
        var encoder = new HandBrakeEncoder("HandBrakeCLI.exe", runner, "preset.json", "VideoTriage AV1");

        var result = await encoder.EncodeAsync("input.mov", "output.mp4",
            new InlineProgress<double>(values.Add));

        result.Succeeded.ShouldBeTrue();
        runner.Request!.Arguments.ShouldBe([
            "--preset-import-file", "preset.json", "-Z", "VideoTriage AV1",
            "-i", "input.mov", "-o", "output.mp4", "--json"]);
        values.ShouldBe([0.5]);
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public ProcessRequest? Request { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            request.StandardOutputLines!.Report("""{"Working":{"Progress":0.5}}""");
            return Task.FromResult(new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "",
                StandardErrorPath = "stderr.log",
                Elapsed = TimeSpan.FromSeconds(1)
            });
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
```

- [ ] **Step 2: Run red**

Run: `dotnet test tests/VideoTriage.Core.Tests -c Debug --filter HandBrakeEncoderTests`

Expected: missing encoder/result types.

- [ ] **Step 3: Add model and interface**

```csharp
namespace VideoTriage.Core.Models;

public enum EncodeOutcome { Succeeded, Failed, Cancelled }

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
using VideoTriage.Core.Models;

namespace VideoTriage.Core.Encoding;

public interface IVideoEncoder
{
    Task<EncodeResult> EncodeAsync(string inputPath, string outputPath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement `HandBrakeEncoder`**

```csharp
using VideoTriage.Core.Models;
using VideoTriage.Core.Tools;

namespace VideoTriage.Core.Encoding;

public sealed class HandBrakeEncoder(
    string handBrakePath,
    IProcessRunner processRunner,
    string presetFilePath,
    string presetName) : IVideoEncoder
{
    public async Task<EncodeResult> EncodeAsync(
        string inputPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var lines = new InlineProgress<string>(line =>
        {
            var value = HandBrakeProgressParser.TryParseProgress(line);
            if (value.HasValue) progress?.Report(value.Value);
        });

        try
        {
            var result = await processRunner.RunAsync(new ProcessRequest
            {
                FileName = handBrakePath,
                Arguments = ["--preset-import-file", presetFilePath, "-Z", presetName,
                    "-i", inputPath, "-o", outputPath, "--json"],
                Timeout = Timeout.InfiniteTimeSpan,
                StandardOutputLines = lines
            }, cancellationToken);

            return new EncodeResult
            {
                Outcome = result.Succeeded ? EncodeOutcome.Succeeded : EncodeOutcome.Failed,
                OutputPath = outputPath,
                Reason = result.Succeeded ? "Encode completed." : $"HandBrake exited {result.ExitCode}.",
                ExitCode = result.ExitCode,
                StderrPath = result.StandardErrorPath,
                Elapsed = result.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            return new EncodeResult
            {
                Outcome = EncodeOutcome.Cancelled,
                OutputPath = outputPath,
                Reason = "Encode cancelled."
            };
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
```

- [ ] **Step 5: Add failure and cancellation tests**

Add these methods to `HandBrakeEncoderTests`:

```csharp
[Fact]
public async Task EncodeAsync_NonzeroExit_ReturnsFailed()
{
    var runner = new FakeRunner
    {
        Result = new ProcessResult
        {
            ExitCode = 7,
            StandardOutput = "",
            StandardErrorPath = "stderr.log",
            Elapsed = TimeSpan.FromSeconds(1)
        }
    };
    var encoder = new HandBrakeEncoder("HandBrakeCLI.exe", runner, "preset.json", "VideoTriage AV1");

    var result = await encoder.EncodeAsync("input.mov", "output.mp4");

    result.Outcome.ShouldBe(EncodeOutcome.Failed);
    result.Succeeded.ShouldBeFalse();
    result.ExitCode.ShouldBe(7);
}

[Fact]
public async Task EncodeAsync_CancelledRunner_ReturnsCancelled()
{
    var runner = new CancellingRunner();
    var encoder = new HandBrakeEncoder("HandBrakeCLI.exe", runner, "preset.json", "VideoTriage AV1");

    var result = await encoder.EncodeAsync("input.mov", "output.mp4");

    result.Outcome.ShouldBe(EncodeOutcome.Cancelled);
    result.Succeeded.ShouldBeFalse();
}

private sealed class CancellingRunner : IProcessRunner
{
    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default) =>
        Task.FromCanceled<ProcessResult>(new CancellationToken(canceled: true));
}
```

Add a settable `Result` property to `FakeRunner` and return it from `RunAsync`.

- [ ] **Step 6: Run green and commit**

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter HandBrakeEncoderTests
git add src/VideoTriage.Core/Models/EncodeResult.cs src/VideoTriage.Core/Encoding tests/VideoTriage.Core.Tests/Encoding
git commit -m "feat(core): encode AV1 candidates with HandBrake"
```

Expected: all encoding tests pass.

### Task 4: Add The Preset And Final Gate

**Files:**
- Create: `src/VideoTriage.Core/Encoding/Assets/videotriage-av1.json`
- Modify: `src/VideoTriage.Core/VideoTriage.Core.csproj`
- Modify: `README.md`

- [ ] **Step 1: Add the validated HandBrake preset JSON**

Export the `VideoTriage AV1` preset from HandBrakeCLI, confirm it selects NVEnc AV1 10-bit, CQ 26,
the slower encoder preset, first audio track, and no subtitles, then save the complete exported JSON
at the exact path above. Do not hand-edit undocumented HandBrake keys.

- [ ] **Step 2: Copy the asset to output**

```xml
<ItemGroup>
  <Content Include="Encoding\Assets\videotriage-av1.json"
           CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 3: Document the prerequisite**

Add HandBrakeCLI to README prerequisites and state that unit tests use fakes.

- [ ] **Step 4: Verify and commit**

```powershell
dotnet build VideoTriage.sln -c Release
dotnet test tests/VideoTriage.Core.Tests -c Release --no-build
git add src/VideoTriage.Core/VideoTriage.Core.csproj src/VideoTriage.Core/Encoding/Assets/videotriage-av1.json README.md
git commit -m "chore(core): ship validated AV1 HandBrake preset"
```

Expected: Release build succeeds and all Core tests pass.

## Self-Review

- Encoding never verifies, replaces, or deletes files.
- Process cancellation kills the process tree through the existing runner.
- All command arguments use `ArgumentList`; no shell string is constructed.
- The preset is an exported HandBrake artifact, not an invented partial schema.

## Execution Handoff

Execute on `feature/handbrake-encoding` after output verification is integrated into `main`. Use a
fresh implementer and both review gates for every task.
