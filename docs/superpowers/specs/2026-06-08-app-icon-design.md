# App Icon Design Spec

**Date:** 2026-06-08
**Branch target:** feature/app-icon (new branch from main after release-polish merges)

## Goal

Replace the default WPF window icon and MSIX package tile placeholders with a purpose-built
VideoTriage icon — a play triangle with downward compression chevrons on a navy background.

## Visual Design

**Option B selected.** All sizes share the same palette:

- Background: `#141C28` (navy, matches existing MSIX tile background color)
- Foreground: `#4FC3F7` (teal, play triangle and chevrons)

### Size-adaptive rendering

| Size range | Elements drawn |
|---|---|
| ≤ 24 px | Play triangle only (chevrons too small to read) |
| 32–48 px | Play triangle + one chevron |
| ≥ 50 px | Play triangle + two chevrons (second at 50% opacity) |

**Play triangle:** centred, height ≈ 55% of icon size, shifted slightly upward when chevrons are present.

**Background:** rounded rectangle, corner radius ≈ 18% of icon size.

**Chevrons:** V-shaped, pointing downward, centred below the triangle.

## Output Files

| Path | Format | Size(s) |
|---|---|---|
| `src/VideoTriage.App/Assets/app.ico` | Multi-size ICO (PNG-in-ICO, Vista+) | 16, 24, 32, 48, 256 |
| `src/VideoTriage.Package/Assets/Square44x44Logo.png` | PNG | 44×44 |
| `src/VideoTriage.Package/Assets/StoreLogo.png` | PNG | 50×50 |
| `src/VideoTriage.Package/Assets/Square150x150Logo.png` | PNG | 150×150 |

The three MSIX PNGs overwrite existing placeholder files. The `.ico` is a new file.

## Generation Script

`build/Generate-Icons.ps1` — PowerShell, uses `System.Drawing` only (no external tools).

### Responsibilities

1. Define `Draw-Icon [int]$Size` — returns a `System.Drawing.Bitmap` with the icon rendered at
   the requested pixel size using the size-adaptive rules above.
2. For each ICO size (16, 24, 32, 48, 256): call `Draw-Icon`, encode as PNG into a `MemoryStream`.
3. Assemble a valid ICO file from the PNG streams:
   - 6-byte ICONDIR header (reserved=0, type=1, count=N)
   - N × 16-byte ICONDIRENTRY (width, height clamped to 0 for 256, colorCount=0, reserved=0,
     planes=1, bitCount=32, sizeInBytes, offsetInBytes)
   - Raw PNG bytes for each entry
4. Write ICO to `src/VideoTriage.App/Assets/app.ico`, creating the `Assets/` directory if needed.
5. For each MSIX size (44, 50, 150): call `Draw-Icon`, save as PNG to the Package Assets path.
6. Report each output path and dimensions on success.

The script is idempotent — re-running overwrites existing outputs.

## Project Wire-up

`src/VideoTriage.App/VideoTriage.App.csproj` — add inside the existing `<PropertyGroup>`:

```xml
<ApplicationIcon>Assets\app.ico</ApplicationIcon>
```

Add in an `<ItemGroup>`:

```xml
<None Include="Assets\app.ico" />
```

No changes to `Package.appxmanifest` — it already references the three PNG paths.

## Verification

1. `build/Generate-Icons.ps1` parses without errors.
2. Script runs and all five output files exist and are non-empty.
3. `System.Drawing.Icon` loads `app.ico` without exception; frame count ≥ 5.
4. Each PNG file loads with `System.Drawing.Image`; dimensions match expected size.
5. `dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Release` exits 0 with 0 errors.
6. The running app shows the new icon in the window title bar and taskbar.

## Scope

This plan owns only icon generation, output files, and the two `.csproj` lines.
It does not modify application behaviour, change XAML, touch CI, or alter the release checklist.

## Not In Scope

- Animated or adaptive (light/dark) icons
- Store submission assets beyond the three existing PNG slots
- Scale-factor variants (`.scale-200` etc.) — the existing manifest uses unscaled assets
