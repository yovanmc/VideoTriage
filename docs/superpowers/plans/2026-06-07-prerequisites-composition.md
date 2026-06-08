# Prerequisites And Application Composition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect ffprobe, ffmpeg, and HandBrakeCLI and construct the real VideoTriage dependency graph with .NET hosting and dependency injection.

**Architecture:** `PrerequisiteService` reports stable, actionable tool status through an `IToolLocator` seam. `App` always builds a real host and an `ITriagePipelineProvider`; the provider contains the real pipeline when all tools exist and `null` otherwise, so the shell resolves normally and later ViewModels disable Start without receiving a fake pipeline.

**Tech Stack:** .NET 10, WPF, Microsoft.Extensions.Hosting, Microsoft.Extensions.DependencyInjection, xUnit, Shouldly.

---

## Scope Check

This plan owns tool discovery, the App test project, dependency registration, and WPF host lifetime.
It does not implement queue UI or run commands. It assumes verification, encoding, replacement,
pipeline, state, and poster plans have been integrated into updated `main`.

**Working directory for every command:** `C:\Agent Projects\VideoTriage`

## File Structure

```text
VideoTriage.sln                                             MODIFY - add App tests
src/VideoTriage.Core/Tools/IToolLocator.cs                  CREATE - test seam for PATH lookup
src/VideoTriage.Core/Tools/ToolLocator.cs                   MODIFY - implement seam
src/VideoTriage.App/VideoTriage.App.csproj                  MODIFY - hosting packages
src/VideoTriage.App/App.xaml                                MODIFY - remove StartupUri
src/VideoTriage.App/App.xaml.cs                             MODIFY - own host startup/shutdown
src/VideoTriage.App/Services/ToolPrerequisiteStatus.cs      CREATE - immutable status
src/VideoTriage.App/Services/IPrerequisiteService.cs        CREATE - status query
src/VideoTriage.App/Services/PrerequisiteService.cs         CREATE - stable tool checks
src/VideoTriage.App/Services/ServiceCollectionExtensions.cs CREATE - real graph registration
tests/VideoTriage.Core.Tests/Tools/ToolLocatorTests.cs       MODIFY - interface compatibility test
tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj     CREATE - App test project
tests/VideoTriage.App.Tests/Services/PrerequisiteServiceTests.cs CREATE
tests/VideoTriage.App.Tests/Services/ServiceCollectionExtensionsTests.cs CREATE
```

### Task 1: Add The App Test Project

**Files:**
- Create: `tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj`
- Modify: `VideoTriage.sln`

- [ ] **Step 1: Create the test project**

Create `tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\VideoTriage.App\VideoTriage.App.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add it to the solution**

Run:

```powershell
dotnet sln VideoTriage.sln add tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj
```

Expected: `Project 'tests\VideoTriage.App.Tests\VideoTriage.App.Tests.csproj' added to the solution.`

- [ ] **Step 3: Verify the empty suite is green**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug
```

Expected: restore/build succeeds and reports no failed tests.

- [ ] **Step 4: Commit**

```powershell
git add VideoTriage.sln tests/VideoTriage.App.Tests/VideoTriage.App.Tests.csproj
git commit -m "test(app): add application test project"
```

Expected: commit succeeds.

### Task 2: Add The Tool Locator Seam

**Files:**
- Create: `src/VideoTriage.Core/Tools/IToolLocator.cs`
- Modify: `src/VideoTriage.Core/Tools/ToolLocator.cs`
- Modify: `tests/VideoTriage.Core.Tests/Tools/ToolLocatorTests.cs`

- [ ] **Step 1: Add the failing interface compatibility test**

Add to `ToolLocatorTests`:

```csharp
[Fact]
public void ToolLocator_ImplementsIToolLocator()
{
    IToolLocator locator = new ToolLocator(string.Empty);

    locator.FindOnPath("ffmpeg").ShouldBeNull();
}
```

- [ ] **Step 2: Run red**

Run:

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter ToolLocator_ImplementsIToolLocator
```

Expected: build fails with `CS0246` because `IToolLocator` does not exist.

- [ ] **Step 3: Add the interface and implement it**

Create `src/VideoTriage.Core/Tools/IToolLocator.cs`:

```csharp
namespace VideoTriage.Core.Tools;

public interface IToolLocator
{
    string? FindOnPath(string executableName);
    ToolLocation RequireOnPath(string executableName);
}
```

Change the declaration in `ToolLocator.cs` to:

```csharp
public sealed class ToolLocator : IToolLocator
```

Do not change any method body.

- [ ] **Step 4: Run green**

Run:

```powershell
dotnet test tests/VideoTriage.Core.Tests -c Debug --filter ToolLocatorTests
```

Expected: `Passed!` and `Failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add src/VideoTriage.Core/Tools/IToolLocator.cs src/VideoTriage.Core/Tools/ToolLocator.cs tests/VideoTriage.Core.Tests/Tools/ToolLocatorTests.cs
git commit -m "refactor(core): expose tool locator seam"
```

Expected: commit succeeds.

### Task 3: Detect Required Tools

**Files:**
- Create: `src/VideoTriage.App/Services/ToolPrerequisiteStatus.cs`
- Create: `src/VideoTriage.App/Services/IPrerequisiteService.cs`
- Create: `src/VideoTriage.App/Services/PrerequisiteService.cs`
- Create: `tests/VideoTriage.App.Tests/Services/PrerequisiteServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/VideoTriage.App.Tests/Services/PrerequisiteServiceTests.cs`:

```csharp
using Shouldly;
using VideoTriage.App.Services;
using VideoTriage.Core.Tools;

namespace VideoTriage.App.Tests.Services;

public sealed class PrerequisiteServiceTests
{
    [Fact]
    public void Check_ReturnsAllRequiredToolsInStableOrder()
    {
        var locator = new FakeLocator(new Dictionary<string, string?>
        {
            ["ffprobe"] = @"C:\tools\ffprobe.exe",
            ["ffmpeg"] = null,
            ["HandBrakeCLI"] = @"C:\tools\HandBrakeCLI.exe"
        });

        var result = new PrerequisiteService(locator).Check();

        result.Select(x => x.Name).ShouldBe(["ffprobe", "ffmpeg", "HandBrakeCLI"]);
        result[0].ShouldBe(new ToolPrerequisiteStatus(
            "ffprobe", true, @"C:\tools\ffprobe.exe", "winget install Gyan.FFmpeg"));
        result[1].IsAvailable.ShouldBeFalse();
        result[1].FullPath.ShouldBeNull();
        result[1].InstallHint.ShouldBe("winget install Gyan.FFmpeg");
    }

    [Fact]
    public void Check_HandBrakeMissing_ReturnsCliSpecificInstallHint()
    {
        var locator = new FakeLocator(new Dictionary<string, string?>());

        var result = new PrerequisiteService(locator).Check();

        result.Single(x => x.Name == "HandBrakeCLI").InstallHint
            .ShouldBe("winget install HandBrake.HandBrake.CLI");
    }

    private sealed class FakeLocator(IReadOnlyDictionary<string, string?> paths) : IToolLocator
    {
        public string? FindOnPath(string executableName) =>
            paths.TryGetValue(executableName, out var path) ? path : null;

        public ToolLocation RequireOnPath(string executableName) =>
            throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Run red**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter PrerequisiteServiceTests
```

Expected: build fails with `CS0234` or `CS0246` because the prerequisite types do not exist.

- [ ] **Step 3: Add the complete production types**

Create `src/VideoTriage.App/Services/ToolPrerequisiteStatus.cs`:

```csharp
namespace VideoTriage.App.Services;

public sealed record ToolPrerequisiteStatus(
    string Name,
    bool IsAvailable,
    string? FullPath,
    string InstallHint);
```

Create `src/VideoTriage.App/Services/IPrerequisiteService.cs`:

```csharp
namespace VideoTriage.App.Services;

public interface IPrerequisiteService
{
    IReadOnlyList<ToolPrerequisiteStatus> Check();
}
```

Create `src/VideoTriage.App/Services/PrerequisiteService.cs`:

```csharp
using VideoTriage.Core.Tools;

namespace VideoTriage.App.Services;

public sealed class PrerequisiteService(IToolLocator locator) : IPrerequisiteService
{
    public IReadOnlyList<ToolPrerequisiteStatus> Check() =>
    [
        Status("ffprobe", "winget install Gyan.FFmpeg"),
        Status("ffmpeg", "winget install Gyan.FFmpeg"),
        Status("HandBrakeCLI", "winget install HandBrake.HandBrake.CLI")
    ];

    private ToolPrerequisiteStatus Status(string name, string installHint)
    {
        var path = locator.FindOnPath(name);
        return new ToolPrerequisiteStatus(name, path is not null, path, installHint);
    }
}
```

- [ ] **Step 4: Run green**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter PrerequisiteServiceTests
```

Expected: `Passed!` and `Failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add src/VideoTriage.App/Services/ToolPrerequisiteStatus.cs src/VideoTriage.App/Services/IPrerequisiteService.cs src/VideoTriage.App/Services/PrerequisiteService.cs tests/VideoTriage.App.Tests/Services/PrerequisiteServiceTests.cs
git commit -m "feat(app): detect external tool prerequisites"
```

Expected: commit succeeds.

### Task 4: Register The Real Dependency Graph

**Files:**
- Modify: `src/VideoTriage.App/VideoTriage.App.csproj`
- Create: `src/VideoTriage.App/Services/ServiceCollectionExtensions.cs`
- Create: `tests/VideoTriage.App.Tests/Services/ServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Add hosting packages**

Run:

```powershell
dotnet add src/VideoTriage.App package Microsoft.Extensions.Hosting
```

Expected: package reference is added and restore succeeds.

- [ ] **Step 2: Write failing composition tests**

Create `tests/VideoTriage.App.Tests/Services/ServiceCollectionExtensionsTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VideoTriage.App.Services;
using VideoTriage.Core.Pipeline;
using VideoTriage.Core.Tools;

namespace VideoTriage.App.Tests.Services;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddVideoTriage_AllToolsAvailable_RegistersRealPipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolLocator>(new FakeLocator(allAvailable: true));

        services.AddVideoTriageForTests(Path.GetTempPath());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPrerequisiteService>().ShouldBeOfType<PrerequisiteService>();
        provider.GetRequiredService<ITriagePipelineProvider>().Pipeline
            .ShouldBeOfType<TriagePipeline>();
    }

    [Fact]
    public void AddVideoTriage_MissingTool_RegistersProviderWithoutPipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToolLocator>(new FakeLocator(allAvailable: false));

        services.AddVideoTriageForTests(Path.GetTempPath());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITriagePipelineProvider>().Pipeline.ShouldBeNull();
        provider.GetRequiredService<IPrerequisiteService>().Check()
            .Any(x => !x.IsAvailable).ShouldBeTrue();
    }

    private sealed class FakeLocator(bool allAvailable) : IToolLocator
    {
        public string? FindOnPath(string executableName) =>
            allAvailable || executableName != "ffmpeg" ? $@"C:\tools\{executableName}.exe" : null;

        public ToolLocation RequireOnPath(string executableName) =>
            new() { Name = executableName, FullPath = FindOnPath(executableName)! };
    }
}
```

- [ ] **Step 3: Run red**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter ServiceCollectionExtensionsTests
```

Expected: build fails with `CS1061` because `AddVideoTriageForTests` does not exist.

- [ ] **Step 4: Add the complete composition extension**

Create `src/VideoTriage.App/Services/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoTriage.App.Views;
using VideoTriage.Core.Encoding;
using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Pipeline;
using VideoTriage.Core.Poster;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Replace;
using VideoTriage.Core.State;
using VideoTriage.Core.Tools;
using VideoTriage.Core.Verify;

namespace VideoTriage.App.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoTriage(this IServiceCollection services)
        => services.AddVideoTriageCore();

    // Retained for tests that want to register the same graph. Triage STATE is no longer global:
    // per the architecture contract, completed/manifest/result stores live in
    // `<scannedFolder>/<TriageOptions.DataDirectoryName>` (default `_videotriage_data`), so they are
    // created per run from a directory the pipeline computes. Hence the pipeline receives store
    // FACTORIES (`Func<string, ...>`), not pre-constructed singletons bound to a global path.
    public static IServiceCollection AddVideoTriageForTests(this IServiceCollection services)
        => services.AddVideoTriageCore();

    private static IServiceCollection AddVideoTriageCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IToolLocator, ToolLocator>();
        services.AddSingleton<IPrerequisiteService, PrerequisiteService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IVideoFileDiscovery, VideoFileDiscovery>();
        services.AddSingleton<IVideoClassifier, BppClassifier>();
        services.AddSingleton<FfprobeJsonParser>();
        services.AddSingleton<IFileRemover, FileRemover>();
        services.AddSingleton<ISafeReplacer, SafeReplacer>();
        // Per-folder store factories: the pipeline calls these with `<folder>/_videotriage_data`.
        services.AddSingleton<Func<string, ICompletedFileStore>>(
            _ => dir => new JsonLinesCompletedFileStore(Path.Combine(dir, "completed.jsonl")));
        services.AddSingleton<Func<string, IDeleteManifest>>(
            _ => dir => new CsvDeleteManifest(Path.Combine(dir, "deletions.csv")));
        services.AddSingleton<Func<string, IResultLog>>(
            _ => dir => new JsonLinesResultLog(Path.Combine(dir, "results.jsonl")));
        services.AddSingleton<ITriagePipelineProvider>(sp =>
        {
            var statuses = sp.GetRequiredService<IPrerequisiteService>().Check();
            if (statuses.Any(x => !x.IsAvailable))
                return new TriagePipelineProvider(null);

            var paths = statuses.ToDictionary(x => x.Name, x => x.FullPath!);
            var runner = sp.GetRequiredService<IProcessRunner>();
            var ffprobe = new FfprobeService(
                paths["ffprobe"], runner, sp.GetRequiredService<FfprobeJsonParser>());
            var verifier = new OutputVerifier(paths["ffmpeg"], runner, ffprobe);
            var encoder = new HandBrakeEncoder(
                paths["HandBrakeCLI"],
                runner,
                Path.Combine(AppContext.BaseDirectory, "Encoding", "Assets", "videotriage-av1.json"),
                "VideoTriage AV1");
            var poster = new PosterEmbedder(paths["ffmpeg"], runner, verifier);
            ITriagePipeline pipeline = new TriagePipeline(
                sp.GetRequiredService<IVideoFileDiscovery>(),
                ffprobe,
                sp.GetRequiredService<IVideoClassifier>(),
                encoder,
                verifier,
                sp.GetRequiredService<ISafeReplacer>(),
                sp.GetRequiredService<IFileSystem>(),
                sp.GetRequiredService<Func<string, ICompletedFileStore>>(),
                sp.GetRequiredService<Func<string, IDeleteManifest>>(),
                sp.GetRequiredService<Func<string, IResultLog>>(),
                poster);
            return new TriagePipelineProvider(pipeline);
        });
        services.AddSingleton<MainWindow>();

        return services;
    }
}
```

Create alongside the extension:

```csharp
public sealed class TriagePipelineProvider(ITriagePipeline? pipeline) : ITriagePipelineProvider
{
    public ITriagePipeline? Pipeline { get; } = pipeline;
}
```

`AddVideoTriageForTests` is public so tests can register the same graph. Triage state is per-folder
(co-located under `<scannedFolder>/_videotriage_data`), so tests isolate state simply by scanning a
temp folder — no global data-directory injection is needed. Do not register `NullTriagePipeline`, a
fake pipeline, or a factory that throws when tools are absent.

- [ ] **Step 5: Run green**

Run:

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter ServiceCollectionExtensionsTests
```

Expected: `Passed!` and `Failed: 0`.

- [ ] **Step 6: Commit**

```powershell
git add src/VideoTriage.App/VideoTriage.App.csproj src/VideoTriage.App/Services/ServiceCollectionExtensions.cs tests/VideoTriage.App.Tests/Services/ServiceCollectionExtensionsTests.cs
git commit -m "feat(app): register real VideoTriage services"
```

Expected: commit succeeds.

### Task 5: Own WPF Lifetime With The Generic Host

**Files:**
- Modify: `src/VideoTriage.App/App.xaml`
- Modify: `src/VideoTriage.App/App.xaml.cs`

- [ ] **Step 1: Remove `StartupUri`**

Replace the opening `Application` element in `App.xaml` with:

```xml
<Application x:Class="VideoTriage.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
```

Keep the existing resource dictionary unchanged.

- [ ] **Step 2: Replace `App.xaml.cs` with host lifetime code**

```csharp
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoTriage.App.Services;
using VideoTriage.App.Views;

namespace VideoTriage.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddVideoTriage())
            .Build();

        await _host.StartAsync();
        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
```

- [ ] **Step 3: Run the final green gate**

Run:

```powershell
dotnet build VideoTriage.sln -c Release
dotnet test VideoTriage.sln -c Release --no-build
```

Expected: build reports `0 Error(s)`; all Core and App tests report `Failed: 0`.

- [ ] **Step 4: Commit**

```powershell
git add src/VideoTriage.App/App.xaml src/VideoTriage.App/App.xaml.cs
git commit -m "feat(app): host the WPF application with dependency injection"
```

Expected: commit succeeds.

## Self-Review

### Spec Coverage

- The exact contract names `ToolPrerequisiteStatus` and `IPrerequisiteService` are used.
- ffprobe, ffmpeg, and HandBrakeCLI are checked in stable order with Windows install guidance.
- Missing tools leave the shell resolvable while `ITriagePipelineProvider.Pipeline` is null.
- The only registered pipeline is concrete `TriagePipeline`.
- Triage state (completed/manifest/result) is per-folder, co-located under
  `<scannedFolder>/_videotriage_data`, created via injected `Func<string, ...>` store factories.
  Only application **logs** live under `%LocalAppData%\VideoTriage\Logs`.
- Host startup and shutdown are owned by `App`; `StartupUri` is removed.

### Placeholder And Type Scan

- No task contains TBD, TODO, “implement later,” or an unspecified test.
- All production files have complete code.
- Constructor calls match the architecture contract; `TriagePipeline` is resolved by DI from the
  constructor established by the integrated pipeline/state/poster plans.

## Execution Handoff

Execute on `feature/prerequisites-composition` from updated `main` after the Core pipeline, state,
and poster branches are integrated. Use `superpowers:subagent-driven-development` task-by-task and
require specification plus code-quality review before merging.
