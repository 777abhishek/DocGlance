# Implementation Plan — Mica Backdrop + Remove Hardcoded Backgrounds

## Summary of findings

| Area | Finding |
|---|---|
| **MicaBackdrop wiring** | Already done — `Shell\TitleBarWindow.xaml` has `<Window.SystemBackdrop><MicaBackdrop/></Window.SystemBackdrop>` |
| **Windows App SDK version** | `2.4.0` (project file); `MicaBackdrop` XAML element is the correct SDK 1.3+ API — no C# wiring changes needed |
| **`ViewerPage.xaml`** | `Background="{ThemeResource ApplicationPageBackgroundThemeBrush}"` on the `<Page>` root — this is an opaque brush that covers Mica on the viewer canvas. Must be removed (set to `Transparent`). |
| **`ViewerSidebar.xaml`** | `Background="{ThemeResource ApplicationPageBackgroundThemeBrush}"` on its root `<Grid>` — same opaque problem. Should use `LayerFillColorDefaultBrush` (used elsewhere in the project for layered panels) or `Transparent`. |
| **`ViewerPage.xaml` LoadingOverlay** | `Background="{ThemeResource SolidBackgroundFillColorBaseBrush}"` on the loading spinner overlay Grid — intentionally opaque (it's a full-screen loading screen). This is correct behavior and should be kept. |
| **`DocumentListItem.xaml.cs` `UpdateThemeColors()`** | Sets `RowRoot.Background` to hardcoded near-black (`#FF242424`) in dark mode and near-white (`#FFFAFAFA`) in light mode. These opaque colors do not need to be window-surface colors (the row is a card component), but they should use theme resources so they look right in both themes. Replace with `CardBackgroundFillColorDefaultBrush`. |
| **`DocumentCard.xaml.cs` `UpdateThemeColors()`** | Sets `InfoPanelShape.Fill` (the lower third of the card) to `#E6202020` / `#E6F5F5F5`. These are fine as semi-opaque card content colors, but the near-opaque dark value (`#E6202020`) is noticeably different from system theme. Replace with theme-aware values using `CardBackgroundFillColorDefaultBrush`-equivalent ARGB values. |
| **`DocumentCard.xaml.cs` / `DocumentListItem.xaml.cs` text Foregrounds** | Set via code-behind to hardcoded ARGB values instead of `ThemeResource` brushes. Replace with `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush`, `TextFillColorTertiaryBrush` equivalents — avoids any custom update loop. |
| **`ViewerPage.Pdf.cs`** | `new SolidColorBrush(Colors.White)` for the PDF page placeholder border. A PDF page background is always white (that is the paper color, not a window surface), so this is intentional and correct. **No change needed.** |
| **`DocumentCard.xaml` / `DocumentListItem.xaml`** | `Background="Transparent"` on buttons/small elements — these are correct usage of Transparent (hit-test transparency on controls, not window-surface coloring). No change needed. |
| **All other `.xaml` pages** | `HomePage.xaml`, `SettingsPage.xaml`, `BrowserPage.xaml`, `TitleBarWindow.xaml`, `ViewerToolbar.xaml`, `PdfActionBar.xaml`, `UpdateBanner.xaml`, `PageThumbnailSidebar.xaml` — no `Page`-level or root-Grid hardcoded opaque background found. All use `{ThemeResource ...}` or `Transparent`. No changes needed. |

---

## Implementation Plan

- [ ] 1. Remove the opaque `Page` background from `ViewerPage.xaml`.
      The `Background="{ThemeResource ApplicationPageBackgroundThemeBrush}"` attribute on the `<Page>` root
      covers the Mica backdrop on the viewer surface. Removing it lets the Mica show through. This is the
      primary fix; all other surface elements in the viewer already use `Background="Transparent"`.
      Files: `Features\Viewer\ViewerPage.xaml`
      Change: Delete `Background="{ThemeResource ApplicationPageBackgroundThemeBrush}"` from the `<Page ...>` tag.
      Verify: `dotnet build c:\Code\QuickView\QuickView.csproj` — build must succeed with 0 errors.

- [ ] 2. Fix the `ViewerSidebar.xaml` root Grid background.
      `Background="{ThemeResource ApplicationPageBackgroundThemeBrush}"` on the root `<Grid>` in `ViewerSidebar.xaml`
      makes the sidebar opaque-white/black. This sidebar is currently a stub (not yet wired into the main view)
      but should be consistent. Replace with `{ThemeResource LayerFillColorDefaultBrush}` — the same resource
      used by `PdfActionBar.xaml` and `PageThumbnailSidebar.xaml` for layered surfaces — so it produces a
      slightly elevated surface without blocking Mica.
      Files: `Features\Viewer\Components\ViewerSidebar.xaml`
      Change: Replace `Background="{ThemeResource ApplicationPageBackgroundThemeBrush}"` with
      `Background="{ThemeResource LayerFillColorDefaultBrush}"`.
      Verify: `dotnet build c:\Code\QuickView\QuickView.csproj` — build must succeed with 0 errors.

- [ ] 3. Replace hardcoded `RowRoot.Background` in `DocumentListItem.xaml.cs` with a theme resource.
      `UpdateThemeColors()` currently sets `RowRoot.Background` to `#FF242424` (dark) / `#FFFAFAFA` (light) —
      opaque hex values set in code that bypass the theme system. The row item is a card, so the correct
      replacement is `CardBackgroundFillColorDefaultBrush`. Wire it in XAML on the `<Border x:Name="RowRoot">`
      directly and remove the `RowRoot.Background = ...` line from `UpdateThemeColors()` so it is no longer
      overwritten in code.
      Additionally, replace all Foreground `SolidColorBrush(...)` assignments in `UpdateThemeColors()` with
      `{ThemeResource ...}` bindings set in the XAML template (using `TextFillColorPrimaryBrush`,
      `TextFillColorSecondaryBrush`, `TextFillColorTertiaryBrush`) — removes the manual update loop
      for text colors so they follow the system theme automatically.
      Files: `Controls\DocumentListItem.xaml`, `Controls\DocumentListItem.xaml.cs`
      XAML change: Add `Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"` and
      `BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"` to `<Border x:Name="RowRoot">`.
      Add `Foreground="{ThemeResource TextFillColorPrimaryBrush}"` to `FileNameText`,
      `Foreground="{ThemeResource TextFillColorSecondaryBrush}"` to `FileTypeDesc`, `ModifiedText`, `ModifiedIcon`,
      `Foreground="{ThemeResource TextFillColorSecondaryBrush}"` to `FileSizeText`.
      CS change: Remove `RowRoot.Background = ...`, `RowRoot.BorderBrush = ...`, and all Foreground assignments
      from `UpdateThemeColors()`. If `UpdateThemeColors()` becomes empty after that, remove it entirely and
      remove the `ActualThemeChanged` subscription.
      Verify: `dotnet build c:\Code\QuickView\QuickView.csproj` — build must succeed with 0 errors.

- [ ] 4. Replace hardcoded `InfoPanelShape.Fill` and Foreground colors in `DocumentCard.xaml.cs`.
      `UpdateThemeColors()` sets `InfoPanelShape.Fill` to `#E6202020` (dark) / `#E6F5F5F5` (light) and all
      text Foreground properties to manually computed ARGB values. Replace `InfoPanelShape.Fill` with a theme
      resource that approximates the same elevated surface — use `CardBackgroundFillColorDefaultBrush` which
      already carries the correct alpha/tint for both themes. Replace text Foreground assignments with
      `{ThemeResource TextFillColorPrimaryBrush}` / `TextFillColorSecondaryBrush` / `TextFillColorTertiaryBrush`
      bindings set in XAML on each named TextBlock and FontIcon.
      Note: `CardBorder.BorderBrush` uses a theme-aware alpha value — this should also be replaced with
      `{ThemeResource CardStrokeColorDefaultBrush}` set directly in XAML.
      Files: `Controls\DocumentCard.xaml`, `Controls\DocumentCard.xaml.cs`
      XAML changes:
        - Add `BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"` to `<Border x:Name="CardBorder">`.
        - Add `Fill="{ThemeResource CardBackgroundFillColorDefaultBrush}"` to `<Path x:Name="InfoPanelShape">`.
        - Add `Foreground="{ThemeResource TextFillColorPrimaryBrush}"` to `FileNameText`.
        - Add `Foreground="{ThemeResource TextFillColorSecondaryBrush}"` to `FileTypeDesc`, `ModifiedText`, `ModifiedIcon`.
        - Add `Foreground="{ThemeResource TextFillColorSecondaryBrush}"` to `FileSizeText`.
      CS change: Remove `CardBorder.BorderBrush = ...`, `InfoPanelShape.Fill = ...`, and all Foreground
      assignments from `UpdateThemeColors()`. Remove `UpdateThemeColors()` method and `ActualThemeChanged`
      subscription if no code remains.
      Verify: `dotnet build c:\Code\QuickView\QuickView.csproj` — build must succeed with 0 errors.

---

## Files changed summary

| File | Change type |
|---|---|
| `Features\Viewer\ViewerPage.xaml` | Remove opaque `Page` background attribute |
| `Features\Viewer\Components\ViewerSidebar.xaml` | Replace opaque background with `LayerFillColorDefaultBrush` |
| `Controls\DocumentListItem.xaml` | Add theme resource bindings for background, border, and text colors |
| `Controls\DocumentListItem.xaml.cs` | Remove `UpdateThemeColors()` manual color assignments |
| `Controls\DocumentCard.xaml` | Add theme resource bindings for border, info panel fill, and text colors |
| `Controls\DocumentCard.xaml.cs` | Remove `UpdateThemeColors()` manual color assignments |

## Files explicitly NOT changed (and why)

| File | Reason |
|---|---|
| `Shell\TitleBarWindow.xaml` | `<MicaBackdrop/>` already in place |
| `Shell\TitleBarWindow.xaml.cs` | No C# Mica wiring needed; `MicaBackdrop` in XAML handles it |
| `Features\Viewer\Components\ViewerPage.Pdf.cs` | `Colors.White` is intentional — PDF pages have white paper backgrounds |
| `Features\Viewer\ViewerPage.xaml` `LoadingOverlay` | `SolidBackgroundFillColorBaseBrush` is intentional — a full-screen loading cover that should be opaque |
| `App.xaml` | No background; `XamlControlsResources` already present |
| `Features\Home\HomePage.xaml` | No opaque root background; transparent by default |
| `Features\Settings\SettingsPage.xaml` | No opaque root background; transparent by default |
| `Features\Browser\BrowserPage.xaml` | No opaque root background; transparent by default |
| `Controls\DocumentCard.xaml` `#28FFFFFF` | Semi-transparent icon backing — not a window-surface color |
| `Controls\DocumentListItem.xaml` `#40000000` | Semi-transparent extension label badge — not a window-surface color |
| `Features\Search\SearchHighlighter.cs` | Dynamic highlight colors derived from content, not surface backgrounds |
| `Features\Viewer\Components\ViewerPage.Search.cs` | PDF search highlight overlays — intentional tinted colors, not surfaces |
