# UI Cosmetic Polish Design

**Date:** 2026-06-08
**Scope:** Cosmetic/XAML-only changes. No layout restructuring, no new view models, no new C# types.

## Goal

Replace developer-facing boolean strings and default-styled controls with polished visual indicators,
typographic hierarchy, and status-aware UI — making the app feel intentional rather than default-styled.

---

## 1. Sidebar Panel

### 1a. Scrollbar (MainWindow.xaml)

Change the sidebar `ScrollViewer` from `VerticalScrollBarVisibility="Auto"` to
`VerticalScrollBarVisibility="Hidden"`. Mouse-wheel and touchpad scrolling remain functional;
the visible scrollbar track disappears.

### 1b. Section header typography (MainWindow.xaml, SettingsView.xaml)

Apply `Foreground="{StaticResource AccentBrush}"` (`#5CC8FF`) to every section header
`TextBlock` with `FontWeight="SemiBold"` in `MainWindow.xaml`. In `SettingsView.xaml` use
`{DynamicResource AccentBrush}` instead — `StaticResource` does not resolve cross-file against
window-level resources at parse time; `DynamicResource` walks the visual tree at runtime.

| File | Element | Resource syntax |
|---|---|---|
| MainWindow.xaml | "Source folder" header | `{StaticResource AccentBrush}` |
| MainWindow.xaml | "Preset" header | `{StaticResource AccentBrush}` |
| MainWindow.xaml | "Prerequisites" header | `{StaticResource AccentBrush}` |
| SettingsView.xaml | "Settings" header | `{DynamicResource AccentBrush}` |

This separates section labels from their content without changing font size or layout.

### 1c. Prerequisites status dots (MainWindow.xaml)

Replace the `Text="{Binding IsAvailable}"` `TextBlock` in the prerequisites `ItemsControl`
`DataTemplate` with a 10×10 `Ellipse`:

- Fill: `#5AD17F` (green, `SuccessBrush`) when `IsAvailable = True`
- Fill: `#FF6B6B` (red, `DangerBrush`) when `IsAvailable = False`
- Use a `Style` with a `DataTrigger` on `IsAvailable` to switch the fill

Add a `ToolTip` to the row `Grid` bound to `InstallHint` so users can see install guidance
on hover when a tool is missing.

### 1d. Dry run checkbox wrapping (SettingsView.xaml)

Change the `Content` of the dry-run `CheckBox` from a plain string to a nested `TextBlock`
with `TextWrapping="Wrap"` so the label wraps inside the sidebar instead of truncating:

```xml
<CheckBox Margin="0,6,0,0" IsChecked="{Binding DryRun}">
    <TextBlock Text="Dry run: no encoding or file changes" TextWrapping="Wrap" />
</CheckBox>
```

---

## 2. Toolbar

### 2a. Start button accent styling (MainWindow.xaml)

Change the Start button from `<Button>` to `<ui:Button Appearance="Primary">`. The WPF-UI
Primary appearance fills the button with the system accent color (teal), making it the clear
dominant action in the toolbar. All existing bindings and width properties carry over unchanged.

### 2b. Scanning indicator (MainWindow.xaml)

Replace the current `TextBlock` that displays `StringFormat=Scanning: {0}` with a conditional
`StackPanel`:

- **Default state (`IsScanning = False`):** `Visibility="Collapsed"` — nothing shown.
- **Scanning state (`IsScanning = True`):** `Visibility="Visible"` — shows a
  `<ui:ProgressRing IsIndeterminate="True" Width="16" Height="16" />` followed by a
  `TextBlock` reading "Scanning…" with `Margin="8,0,0,0"`.

Use a `Style` with a `DataTrigger` on `IsScanning` to toggle `Visibility`.

---

## 3. Status Bar

### 3a. Queue count badge (MainWindow.xaml)

Wrap the "Queue: N files" `TextBlock` in a `Border` to give it a badge appearance:

- `CornerRadius="4"`
- `BorderBrush="#735CC8FF"` (AccentBrush `#5CC8FF` at ~45% alpha — avoids dimming the text
  that would happen if `Opacity` were set on the Border element itself)
- `BorderThickness="1"`
- `Padding="8,2"`

The `TextBlock` binding `{Binding Items.Count, StringFormat=Queue: {0} files}` is unchanged.

---

## 4. Summary View

### 4a. Ring chart center label (SummaryView.xaml)

Overlay the `DonutChart` with centered text by wrapping it in a `Grid`:

```xml
<Grid Width="180" Height="180">
    <controls:DonutChart ItemsSource="{Binding Segments}" />
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False">
        <TextBlock Text="{Binding ProcessedCount}"
                   FontSize="22"
                   FontWeight="SemiBold"
                   HorizontalAlignment="Center" />
        <TextBlock Text="processed"
                   FontSize="11"
                   Opacity="0.6"
                   HorizontalAlignment="Center" />
    </StackPanel>
</Grid>
```

`IsHitTestVisible="False"` on the overlay `StackPanel` keeps the chart's own hit-testing intact.
The `DonutChart` explicit `Width` and `Height` attributes move to the outer `Grid` and are removed
from the `controls:DonutChart` element.

---

## Scope

**In scope:** XAML changes in `MainWindow.xaml`, `SettingsView.xaml`, `SummaryView.xaml`.

**Out of scope:**
- No new C# types, converters, or view-model properties
- No layout restructuring (column widths, panel reorganization)
- No changes to `DiagnosticsView.xaml`
- No changes to `DonutChart.cs`
- No changes to tests

## Verification

1. App builds with `dotnet build src/VideoTriage.App/VideoTriage.App.csproj -c Release` → 0 errors.
2. Sidebar scrollbar track is invisible; scroll wheel moves the panel.
3. Section headers appear in teal; content text remains white/default.
4. Prerequisites show colored dots; missing tool row tooltip shows install hint.
5. Dry run checkbox label wraps to a second line without truncation.
6. Start button has solid accent fill; Stop remains default style.
7. "Scanning: False" text no longer appears; no scanning indicator visible at idle.
8. Queue badge has visible rounded border in the status bar.
9. Summary ring chart shows "0 / processed" center text on an empty run.
