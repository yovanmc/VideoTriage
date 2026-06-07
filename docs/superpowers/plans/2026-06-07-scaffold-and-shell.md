# VideoTriage — Scaffold & Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the VideoTriage solution — git repo, 3-project structure, a passing test, a Fluent (Mica dark) WPF window that launches, and green CI — so every later plan builds on a working, tested foundation.

**Architecture:** Three projects: `VideoTriage.Core` (engine, no UI), `VideoTriage.App` (WPF + WPF-UI, MVVM), `VideoTriage.Core.Tests` (xUnit + Shouldly). This plan only lays the foundation and one tiny real unit (`HumanSize`) to establish the red→green→commit TDD loop; the engine logic lands in later plans.

**Tech Stack:** .NET 10 (`net10.0-windows`), WPF, WPF-UI (lepoco) for Fluent/Mica, CommunityToolkit.Mvvm, xUnit + Shouldly, GitHub Actions CI.

---

## File Structure (created by this plan)

```
C:\Agent Projects\VideoTriage\
├─ .gitignore
├─ .editorconfig
├─ LICENSE                         (MIT)
├─ README.md                       (stub; full README is a later plan)
├─ VideoTriage.sln
├─ .github/workflows/ci.yml        (build + test on windows-latest)
├─ docs/superpowers/plans/         (this plan lives here)
├─ src/
│  ├─ VideoTriage.Core/
│  │   ├─ VideoTriage.Core.csproj
│  │   └─ Formatting/HumanSize.cs  (first real unit)
│  └─ VideoTriage.App/
│      ├─ VideoTriage.App.csproj
│      ├─ App.xaml / App.xaml.cs
│      └─ Views/MainWindow.xaml / MainWindow.xaml.cs
└─ tests/
   └─ VideoTriage.Core.Tests/
       ├─ VideoTriage.Core.Tests.csproj
       └─ Formatting/HumanSizeTests.cs
```

**Working directory for all commands:** `C:\Agent Projects\VideoTriage`

---

## Task 1: Initialize git repo + base files

**Files:**
- Create: `C:\Agent Projects\VideoTriage\.gitignore`
- Create: `C:\Agent Projects\VideoTriage\.editorconfig`
- Create: `C:\Agent Projects\VideoTriage\LICENSE`
- Create: `C:\Agent Projects\VideoTriage\README.md`

- [ ] **Step 1: Initialize the repository**

Run:
```bash
cd "C:/Agent Projects/VideoTriage"
git init -b main
```
Expected: `Initialized empty Git repository in C:/Agent Projects/VideoTriage/.git/`

- [ ] **Step 2: Create `.gitignore`** (standard .NET)

Create `C:\Agent Projects\VideoTriage\.gitignore`:
```gitignore
## .NET
bin/
obj/
[Dd]ebug/
[Rr]elease/
*.user
*.suo
.vs/
artifacts/
TestResults/
*.binlog

## Rider/VS
.idea/

## OS
Thumbs.db
.DS_Store
```

- [ ] **Step 3: Create `.editorconfig`** (consistent style — reviewers notice)

Create `C:\Agent Projects\VideoTriage\.editorconfig`:
```editorconfig
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true
indent_style = space
indent_size = 4
trim_trailing_whitespace = true

[*.{json,yml,yaml,xml,xaml,csproj}]
indent_size = 2

[*.cs]
dotnet_sort_system_directives_first = true
csharp_new_line_before_open_brace = all
dotnet_style_namespace_match_folder = true
```

- [ ] **Step 4: Create `LICENSE`** (MIT)

Create `C:\Agent Projects\VideoTriage\LICENSE` with the standard MIT text, copyright line:
```
MIT License

Copyright (c) 2026 <YOUR NAME>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 5: Create `README.md`** (stub)

Create `C:\Agent Projects\VideoTriage\README.md`:
```markdown
# VideoTriage

Batch-compress a folder of videos to AV1 (NVEnc), keeping a new encode **only when it is
smaller AND verified playable**, then safely removing the original. A Windows desktop app
(WPF + Fluent) built on an engine calibrated on 1,700+ real files.

> 🚧 Early development. See `docs/superpowers/plans/` for the build plan.

## Status
- [x] Scaffold + Fluent shell
- [ ] Core engine (probe / classify / verify / safe-replace)
- [ ] UI wiring + live progress
- [ ] Embedded poster thumbnails
```

- [ ] **Step 6: First commit**

Run:
```bash
cd "C:/Agent Projects/VideoTriage"
git add .gitignore .editorconfig LICENSE README.md docs/
git commit -m "chore: initialize repo with license, gitignore, editorconfig, and plan"
```
Expected: a commit is created listing the added files.

---

## Task 2: Create solution and three projects

**Files:**
- Create: `VideoTriage.sln`
- Create: `src/VideoTriage.Core/VideoTriage.Core.csproj`
- Create: `src/VideoTriage.App/VideoTriage.App.csproj`
- Create: `tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj`

- [ ] **Step 1: Create the solution**

Run:
```bash
cd "C:/Agent Projects/VideoTriage"
dotnet new sln -n VideoTriage
```
Expected: `The template "Solution File" was created successfully.`

- [ ] **Step 2: Create the Core class library**

Run:
```bash
dotnet new classlib -n VideoTriage.Core -o src/VideoTriage.Core -f net10.0
```
Then edit `src/VideoTriage.Core/VideoTriage.Core.csproj` so the `<TargetFramework>` and properties read:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWindowsForms>false</UseWindowsForms>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```
(Note: `net10.0-windows` because later tasks use the Recycle Bin via `Microsoft.VisualBasic.FileIO`. Delete the auto-generated `Class1.cs`: `rm src/VideoTriage.Core/Class1.cs`.)

- [ ] **Step 3: Create the WPF app**

Run:
```bash
dotnet new wpf -n VideoTriage.App -o src/VideoTriage.App -f net10.0
```
Expected: WPF template created. Confirm `src/VideoTriage.App/VideoTriage.App.csproj` has `<TargetFramework>net10.0-windows</TargetFramework>` and `<UseWPF>true</UseWPF>` (the template sets these).

- [ ] **Step 4: Create the test project**

Run:
```bash
dotnet new xunit -n VideoTriage.Core.Tests -o tests/VideoTriage.Core.Tests -f net10.0
```
Then set its TFM to `net10.0-windows` (must match Core) in `tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj`:
```xml
<TargetFramework>net10.0-windows</TargetFramework>
```

- [ ] **Step 5: Add projects to the solution and wire references**

Run:
```bash
dotnet sln add src/VideoTriage.Core/VideoTriage.Core.csproj
dotnet sln add src/VideoTriage.App/VideoTriage.App.csproj
dotnet sln add tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj
dotnet add src/VideoTriage.App reference src/VideoTriage.Core
dotnet add tests/VideoTriage.Core.Tests reference src/VideoTriage.Core
```
Expected: each command prints `Reference ... added to the project` / `Project ... added to the solution`.

- [ ] **Step 6: Verify the solution builds**

Run:
```bash
dotnet build VideoTriage.sln -c Debug
```
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add VideoTriage.sln src/ tests/
git commit -m "chore: scaffold solution with Core, App (WPF), and Core.Tests projects"
```

---

## Task 3: Add NuGet packages

**Files:**
- Modify: `src/VideoTriage.App/VideoTriage.App.csproj`
- Modify: `tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj`

- [ ] **Step 1: Add UI + MVVM packages to the App**

Run:
```bash
dotnet add src/VideoTriage.App package WPF-UI
dotnet add src/VideoTriage.App package CommunityToolkit.Mvvm
```
Expected: `PackageReference for package 'WPF-UI' ... added`. (If WPF-UI reports no `net10.0-windows` asset, it ships `net8.0-windows` which is compatible — the restore still succeeds. Verify in Step 3.)

- [ ] **Step 2: Add Shouldly to the test project**

Run:
```bash
dotnet add tests/VideoTriage.Core.Tests package Shouldly
```
Expected: `PackageReference for package 'Shouldly' ... added`.

- [ ] **Step 3: Restore + build to confirm package compatibility**

Run:
```bash
dotnet build VideoTriage.sln -c Debug
```
Expected: `Build succeeded.` `0 Error(s)`. (If WPF-UI raises a TFM warning, it is non-blocking; record it but continue.)

- [ ] **Step 4: Commit**

```bash
git add src/VideoTriage.App/VideoTriage.App.csproj tests/VideoTriage.Core.Tests/VideoTriage.Core.Tests.csproj
git commit -m "chore: add WPF-UI, CommunityToolkit.Mvvm, and Shouldly packages"
```

---

## Task 4: First TDD unit — `HumanSize` formatter (red → green → commit)

This establishes the test loop with a genuinely useful unit (the UI will format bytes as `1.4 GB`, `540 MB`).

**Files:**
- Test: `tests/VideoTriage.Core.Tests/Formatting/HumanSizeTests.cs`
- Create: `src/VideoTriage.Core/Formatting/HumanSize.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/VideoTriage.Core.Tests/Formatting/HumanSizeTests.cs`:
```csharp
using Shouldly;
using VideoTriage.Core.Formatting;
using Xunit;

namespace VideoTriage.Core.Tests.Formatting;

public class HumanSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1610612736, "1.5 GB")]
    public void Format_ReturnsHumanReadable(long bytes, string expected)
    {
        HumanSize.Format(bytes).ShouldBe(expected);
    }

    [Fact]
    public void Format_NegativeBytes_IsTreatedAsZero()
    {
        HumanSize.Format(-5).ShouldBe("0 B");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug
```
Expected: **build error / FAIL** — `The type or namespace name 'Formatting' does not exist ... (HumanSize)`. (Red.)

- [ ] **Step 3: Write the minimal implementation**

Create `src/VideoTriage.Core/Formatting/HumanSize.cs`:
```csharp
namespace VideoTriage.Core.Formatting;

/// <summary>Formats byte counts as human-readable strings (e.g. "1.5 GB").</summary>
public static class HumanSize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
    {
        if (bytes <= 0) return "0 B";

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // Bytes show no decimal; KB and up show one decimal place.
        return unit == 0
            ? $"{(long)value} {Units[unit]}"
            : $"{value:0.0} {Units[unit]}";
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
dotnet test tests/VideoTriage.Core.Tests -c Debug
```
Expected: `Passed!  - Failed: 0, Passed: 9` (8 theory cases + 1 fact). (Green.)

- [ ] **Step 5: Commit**

```bash
git add src/VideoTriage.Core/Formatting/HumanSize.cs tests/VideoTriage.Core.Tests/Formatting/HumanSizeTests.cs
git commit -m "feat(core): add HumanSize byte formatter with tests"
```

---

## Task 5: Fluent (Mica dark) WPF shell

Replace the WPF template's default window with a WPF-UI `FluentWindow` using Mica + dark theme and a minimal placeholder layout (full mockup layout is a later UI plan).

**Files:**
- Modify: `src/VideoTriage.App/App.xaml`
- Modify: `src/VideoTriage.App/App.xaml.cs`
- Create: `src/VideoTriage.App/Views/MainWindow.xaml` (move/replace template `MainWindow`)
- Create: `src/VideoTriage.App/Views/MainWindow.xaml.cs`
- Delete: template `src/VideoTriage.App/MainWindow.xaml` + `.cs` if present

- [ ] **Step 1: Remove the template MainWindow (if at project root)**

Run:
```bash
rm -f "src/VideoTriage.App/MainWindow.xaml" "src/VideoTriage.App/MainWindow.xaml.cs"
mkdir -p "src/VideoTriage.App/Views"
```

- [ ] **Step 2: Set up `App.xaml`** to load WPF-UI resources

Replace `src/VideoTriage.App/App.xaml` with:
```xml
<Application x:Class="VideoTriage.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             StartupUri="Views/MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Dark" />
                <ui:ControlsDictionary />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 3: Simplify `App.xaml.cs`**

Replace `src/VideoTriage.App/App.xaml.cs` with:
```csharp
using System.Windows;

namespace VideoTriage.App;

public partial class App : Application
{
}
```

- [ ] **Step 4: Create the Fluent window `Views/MainWindow.xaml`**

Create `src/VideoTriage.App/Views/MainWindow.xaml`:
```xml
<ui:FluentWindow x:Class="VideoTriage.App.Views.MainWindow"
                 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
                 Title="VideoTriage"
                 Width="1180" Height="760"
                 MinWidth="900" MinHeight="600"
                 WindowBackdropType="Mica"
                 ExtendsContentIntoTitleBar="True"
                 WindowStartupLocation="CenterScreen">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" Title="VideoTriage" />

        <StackPanel Grid.Row="1" VerticalAlignment="Center" HorizontalAlignment="Center">
            <ui:SymbolIcon Symbol="Video24" FontSize="48" HorizontalAlignment="Center" />
            <TextBlock Text="VideoTriage"
                       FontSize="24" FontWeight="SemiBold"
                       HorizontalAlignment="Center" Margin="0,12,0,4" />
            <TextBlock Text="AV1 batch compressor — shell is alive."
                       Opacity="0.6" HorizontalAlignment="Center" />
            <ui:Button Content="Choose folder…" Icon="{ui:SymbolIcon Folder24}"
                       Appearance="Primary" HorizontalAlignment="Center" Margin="0,20,0,0" />
        </StackPanel>
    </Grid>
</ui:FluentWindow>
```

- [ ] **Step 5: Create the code-behind `Views/MainWindow.xaml.cs`**

Create `src/VideoTriage.App/Views/MainWindow.xaml.cs`:
```csharp
using Wpf.Ui.Controls;

namespace VideoTriage.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 6: Build and run — confirm the window launches**

Run:
```bash
dotnet build VideoTriage.sln -c Debug
dotnet run --project src/VideoTriage.App
```
Expected: `Build succeeded`, then a **dark Mica window** titled "VideoTriage" with the video icon, heading, and a primary "Choose folder…" button. Close the window to end the run.

- [ ] **Step 7: Commit**

```bash
git add src/VideoTriage.App
git commit -m "feat(app): Fluent Mica dark shell window with WPF-UI"
```

---

## Task 6: GitHub Actions CI (build + test)

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Create the workflow**

Create `C:\Agent Projects\VideoTriage\.github\workflows\ci.yml`:
```yaml
name: CI

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  build-and-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore VideoTriage.sln

      - name: Build
        run: dotnet build VideoTriage.sln -c Release --no-restore

      - name: Test
        run: dotnet test tests/VideoTriage.Core.Tests -c Release --no-build --verbosity normal
```

- [ ] **Step 2: Verify the same commands pass locally (mirrors CI)**

Run:
```bash
cd "C:/Agent Projects/VideoTriage"
dotnet restore VideoTriage.sln
dotnet build VideoTriage.sln -c Release --no-restore
dotnet test tests/VideoTriage.Core.Tests -c Release --no-build
```
Expected: build succeeds; `Passed! - Failed: 0, Passed: 9`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: build and test on windows-latest via GitHub Actions"
```

---

## Task 7: Push to GitHub (optional, when ready)

- [ ] **Step 1: Create the remote and push** (requires `gh` authenticated)

Run:
```bash
cd "C:/Agent Projects/VideoTriage"
gh repo create VideoTriage --public --source=. --remote=origin --description "Batch AV1 video compressor with verify-before-delete safety (WPF/.NET 10)"
git push -u origin main
```
Expected: repo created, `main` pushed, CI begins running. Confirm the Actions run goes green.

---

## Self-Review (writing-plans)

**1. Spec coverage (this plan's slice):** repo init ✓ (T1), 3-project solution w/ correct TFMs ✓ (T2), packages WPF-UI/MVVM/Shouldly ✓ (T3), TDD loop established ✓ (T4), Fluent Mica shell launches ✓ (T5), CI ✓ (T6), remote ✓ (T7). Engine logic intentionally deferred to later plans.

**2. Placeholder scan:** No TBD/“handle errors”/“write tests for the above”. The only literal placeholder is `<YOUR NAME>` in the MIT license, which is an intentional user fill-in.

**3. Type consistency:** `HumanSize.Format(long)` defined in T4 and referenced only there. `MainWindow : FluentWindow` matches the `ui:FluentWindow` XAML root. App `StartupUri` → `Views/MainWindow.xaml` matches the created file. Namespaces (`VideoTriage.App`, `VideoTriage.App.Views`, `VideoTriage.Core.Formatting`) are consistent across XAML `x:Class`, code-behind, and tests.

---

## Execution Handoff

Plan complete. Two execution options:

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration (superpowers:subagent-driven-development).
2. **Inline Execution** — execute tasks in this session with checkpoints (superpowers:executing-plans).

Next plans (to be written after this lands): `…-core-probe-classify.md`, `…-verifier.md`, `…-encode-and-safe-replace.md`, `…-ui-wiring.md`, `…-poster-thumbnails.md`, `…-settings-summary-polish.md`.
