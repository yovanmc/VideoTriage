# Settings Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist validated user settings and project them into `TriageOptions` without exposing malformed configuration to Core.

**Architecture:** A versioned App DTO is serialized with `System.Text.Json`; invalid files are preserved and defaults returned. `SettingsViewModel` validates ranges before save.

**Tech Stack:** .NET 10, System.Text.Json, CommunityToolkit.Mvvm, xUnit, Shouldly.

---

## Scope Check

This plan persists user-editable options only. Tool paths remain discovered prerequisites and the
HandBrake preset remains an application asset.

## File Structure

```text
src/VideoTriage.App/
  Models/AppSettings.cs
  Services/ISettingsStore.cs
  Services/JsonSettingsStore.cs
  ViewModels/SettingsViewModel.cs
  Views/SettingsView.xaml
tests/VideoTriage.App.Tests/
  Services/JsonSettingsStoreTests.cs
  ViewModels/SettingsViewModelTests.cs
```

### Task 1: Settings DTO And Mapping

**Files:**
- Create: `src/VideoTriage.App/Models/AppSettings.cs`
- Create: `tests/VideoTriage.App.Tests/Models/AppSettingsTests.cs`

- [ ] **Step 1: Write red default and mapping tests**

Create `tests/VideoTriage.App.Tests/Models/AppSettingsTests.cs`:

```csharp
using Shouldly;
using VideoTriage.App.Models;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.Models;

public sealed class AppSettingsTests
{
    [Fact]
    public void Defaults_AreSafeForFirstRun()
    {
        var settings = new AppSettings();

        settings.CandidateBppThreshold.ShouldBe(0.13);
        settings.DeleteMode.ShouldBe(DeleteMode.RecycleBin);
        settings.DeepVerify.ShouldBeTrue();
        settings.EmbedPoster.ShouldBeTrue();
        settings.MinimumFreeGigabytes.ShouldBe(5);
        settings.DryRun.ShouldBeFalse();
    }

    [Fact]
    public void ToTriageOptions_MapsEveryEditableField()
    {
        var settings = new AppSettings
        {
            CandidateBppThreshold = 0.2,
            DeleteMode = DeleteMode.Permanent,
            DeepVerify = false,
            EmbedPoster = false,
            MinimumFreeGigabytes = 9,
            DryRun = true
        };

        var options = settings.ToTriageOptions();

        options.CandidateBppThreshold.ShouldBe(0.2);
        options.DeleteMode.ShouldBe(DeleteMode.Permanent);
        options.DeepVerify.ShouldBeFalse();
        options.EmbedPoster.ShouldBeFalse();
        options.MinimumFreeGigabytes.ShouldBe(9);
        options.DryRun.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Implement**

```csharp
using VideoTriage.Core.Models;

namespace VideoTriage.App.Models;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public double CandidateBppThreshold { get; init; } = 0.13;
    public DeleteMode DeleteMode { get; init; } = DeleteMode.RecycleBin;
    public bool DeepVerify { get; init; } = true;
    public bool EmbedPoster { get; init; } = true;
    public double MinimumFreeGigabytes { get; init; } = 5;
    public bool DryRun { get; init; }

    public TriageOptions ToTriageOptions() => new()
    {
        CandidateBppThreshold = CandidateBppThreshold,
        DeleteMode = DeleteMode,
        DeepVerify = DeepVerify,
        EmbedPoster = EmbedPoster,
        MinimumFreeGigabytes = MinimumFreeGigabytes,
        DryRun = DryRun
    };
}
```

- [ ] **Step 3: Run and commit**

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter AppSettingsTests
git add src/VideoTriage.App/Models tests/VideoTriage.App.Tests/Models
git commit -m "feat(app): define persisted application settings"
```

### Task 2: JSON Store

**Files:**
- Create: `src/VideoTriage.App/Services/ISettingsStore.cs`
- Create: `src/VideoTriage.App/Services/JsonSettingsStore.cs`
- Create: `tests/VideoTriage.App.Tests/Services/JsonSettingsStoreTests.cs`

- [ ] **Step 1: Write red tests**

Create `tests/VideoTriage.App.Tests/Services/JsonSettingsStoreTests.cs`:

```csharp
using Shouldly;
using VideoTriage.App.Models;
using VideoTriage.App.Services;

namespace VideoTriage.App.Tests.Services;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "VideoTriage.SettingsTests", Guid.NewGuid().ToString("N"));

    public JsonSettingsStoreTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults() =>
        new JsonSettingsStore(Path.Combine(dir, "settings.json")).Load().ShouldBe(new AppSettings());

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(dir, "settings.json");
        var store = new JsonSettingsStore(path);
        var expected = new AppSettings { CandidateBppThreshold = 0.21, DryRun = true };

        store.Save(expected);

        store.Load().ShouldBe(expected);
        File.Exists(path + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void Load_MalformedJson_BacksUpInvalidFileAndReturnsDefaults()
    {
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{broken");
        var store = new JsonSettingsStore(path);

        store.Load().ShouldBe(new AppSettings());

        Directory.GetFiles(dir, "settings.invalid.*.json").Length.ShouldBe(1);
    }

    [Fact]
    public void Load_UnsupportedSchemaVersion_ReturnsDefaults()
    {
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, """{"schemaVersion":99}""");

        new JsonSettingsStore(path).Load().ShouldBe(new AppSettings());
    }
}
```

- [ ] **Step 2: Implement interface**

```csharp
using VideoTriage.App.Models;
namespace VideoTriage.App.Services;
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
```

- [ ] **Step 3: Implement store**

Create `JsonSettingsStore.cs`:

```csharp
using System.Text.Json;
using VideoTriage.App.Models;

namespace VideoTriage.App.Services;

public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            return settings?.SchemaVersion == 1 ? settings : new AppSettings();
        }
        catch (JsonException)
        {
            var backup = Path.Combine(
                Path.GetDirectoryName(path)!,
                $"settings.invalid.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
            File.Move(path, backup, overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, overwrite: true);
    }
}
```

- [ ] **Step 4: Run and commit**

```powershell
dotnet test tests/VideoTriage.App.Tests -c Debug --filter JsonSettingsStoreTests
git add src/VideoTriage.App/Services tests/VideoTriage.App.Tests/Services
git commit -m "feat(app): persist settings as versioned JSON"
```

### Task 3: Settings ViewModel And View

**Files:**
- Create: `src/VideoTriage.App/ViewModels/SettingsViewModel.cs`
- Create: `src/VideoTriage.App/Views/SettingsView.xaml`
- Create: `tests/VideoTriage.App.Tests/ViewModels/SettingsViewModelTests.cs`

- [ ] **Step 1: Write red validation tests**

Threshold must be `> 0` and `<= 1`; free GB must be `>= 1`; permanent delete requires
`ConfirmPermanentDelete = true`. Invalid settings cannot save.

```csharp
using Shouldly;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1.1)]
    public void CanSave_InvalidThreshold_ReturnsFalse(double threshold)
    {
        var vm = new SettingsViewModel(new FakeSettingsStore()) { CandidateBppThreshold = threshold };
        vm.CanSave.ShouldBeFalse();
        vm.ValidationMessage.ShouldContain("threshold");
    }

    [Fact]
    public void CanSave_PermanentDeleteWithoutConfirmation_ReturnsFalse()
    {
        var vm = new SettingsViewModel(new FakeSettingsStore())
        {
            DeleteMode = DeleteMode.Permanent,
            ConfirmPermanentDelete = false
        };

        vm.CanSave.ShouldBeFalse();
    }

    [Fact]
    public void SaveCommand_ValidSettings_Persists()
    {
        var store = new FakeSettingsStore();
        var vm = new SettingsViewModel(store) { DryRun = true };

        vm.SaveCommand.Execute(null);

        store.Saved!.DryRun.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Implement ViewModel**

Use observable properties and `SaveCommand`. Expose `ValidationMessage` and `CanSave`. Save an
`AppSettings` only after validation.

- [ ] **Step 3: Build danger-aware view**

Bind toggles and numeric fields. Display permanent deletion in red with a separate confirmation
checkbox. Dry-run text explicitly says no encoding or file changes occur.

- [ ] **Step 4: Verify and commit**

```powershell
dotnet test tests/VideoTriage.App.Tests -c Release
dotnet build VideoTriage.sln -c Release --no-restore
git add src/VideoTriage.App tests/VideoTriage.App.Tests
git commit -m "feat(app): add validated settings editor"
```

## Self-Review

- Recycle Bin remains the default.
- Invalid JSON is preserved for diagnosis.
- Permanent deletion requires an explicit second control.

## Execution Handoff

Execute on `feature/settings-persistence` after run controls are integrated.
