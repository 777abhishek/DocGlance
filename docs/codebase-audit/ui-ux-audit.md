# QuickView — UI/UX Audit: Theme, Layout, Transitions, Animations, Smoothness (2026-10-11)

> **Method:** static analysis of all XAML + UI code-behind at HEAD `40cdfcd`, plus online research into current [Fluent motion guidance](https://learn.microsoft.com/en-us/windows/apps/design/motion/timing-and-easing) and comparable apps (SumatraPDF, Windows 11 shell). **No runtime/visual verification was performed** — items marked ⚠ need a live pass (launch app, record transitions) before treating as final.
> IDs here are UX findings (`UI-xxx`); functional defects remain in [findings.md](./findings.md).

## Reference standards used

- **Fluent/WinUI standard durations** ([Timing and easing](https://learn.microsoft.com/en-us/windows/apps/design/motion/timing-and-easing)): `ControlNormalAnimationDuration` **250 ms**, `ControlFastAnimationDuration` **167 ms**, `ControlFasterAnimationDuration` **83 ms**; entrances use **Fast-Out-Slow-In** `cubic-bezier(0,0,0,1)`, exits **Slow-Out-Fast-In** `cubic-bezier(1,0,1,1)`. Built-in theme transitions already conform — prefer them over custom Storyboards.
- **ItemsRepeater** ([overview](https://learn.microsoft.com/en-us/windows/apps/develop/controls/itemsrepeater)): provides virtualization but *not* scroll anchoring by itself; needs `ItemsRepeaterScrollHost` (or ListView/GridView) for stable, smooth large-list scrolling ([IScrollAnchorProvider](https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.controls.iscrollanchorprovider)).
- **SumatraPDF** ([official](https://www.sumatrapdfreader.org/free-pdf-reader)): the benchmark for "fast, no-bloat viewer" — instant open, tabbed, minimal chrome. QuickView's Mica + tabs already match its spirit; gaps are in *state feedback* (hover/loading) and *transition continuity*.

---

## What's already good (verified — do not regress)

| Area | Evidence |
|---|---|
| Mica backdrop + TabView-as-title-bar (Chrome pattern), transparent tab strip | `TitleBarWindow.xaml:14-16,42-52` |
| Standard WinUI theme resources → free light/dark, correct text/card/divider brushes everywhere | `App.xaml:8`, ThemeResource usage throughout HomePage/ViewerPage/Settings |
| Acrylic overlay pills + `ThemeShadow` for Find bar and Zoom pill | `ViewerPage.xaml:398-452` |
| Entrance transitions wired on Frame and ViewerPage; opacity transitions declared | `TitleBarWindow.xaml:84-111`, `ViewerPage.xaml:11-15` |
| Real empty state and no-results state with icon + guidance; ProgressRing for scan and find | `HomePage.xaml:210-277`, `ViewerPage.xaml:410-414` |
| Keyboard shortcuts + ToolTips consistently present (Ctrl±, Alt+C, Esc…) | toolbar/FindBar XAML |
| Sensible two-column Home layout (content + 300px filter rail), UniformGridLayout auto-columns | `HomePage.xaml:107-111,283-307` |
| DocumentCard: custom curved info panel, gradient + type icon, context menu (open/bookmark/explorer/share) | `DocumentCard.xaml:106-128,191-222` |

---

## Findings

### UI-01 — Tab switching is a hard cut (High, confirmed)
**Evidence:** `TitleBarWindow.xaml.cs:230-260` — `Tabs_SelectionChanged` swaps `HomeFrame.Visibility` ↔ `DocHost.Visibility` and sets `DocHost.Content` directly. `TitleBarWindow.xaml:93-95,109-111` declare `ScalarTransition` on **Opacity**, but Visibility changes don't animate Opacity; `EntranceThemeTransition` only fires when content first enters the tree, not on visibility swaps. Net effect: clicking between tabs snaps content with zero motion. This is the app's most-used interaction.
**Fix (minimal):** cross-fade on switch — on selection change: set outgoing host `Opacity=0`, incoming `Opacity=1` (the declared 0.15 s `ScalarTransition` then does the work), keep visibility swaps but animate Opacity; or give each tab its own Frame with `SlideNavigationTransitionInfo` (Edge-fluent, directional). Use the platform durations (167–250 ms).
**Acceptance:** switching tabs shows a 150–250 ms fade/slide; no flicker of stale content. ⚠ verify at runtime.
**Effort:** S.

### UI-02 — Document cards have no hover/pressed/focus feedback (High, confirmed)
**Evidence:** `DocumentCard.xaml:32-42` — custom `ControlTemplate` replaces the default Button template with a bare `Grid + ContentPresenter`; **all visual states removed**. Grep for `PointerEntered/Exited/VisualState` in `DocumentCard.xaml.cs` → none. Cards are the primary clickable surface of Home and feel dead under the cursor; Fluent expects a subtle state layer (hover: light overlay, pressed: dim, focus: 2px focus rect).
**Fix (minimal):** in the template, wrap the content in a `Border x:Name="StateLayer"` and add a `VisualStateManager` with PointerOver/Pressed states setting `StateLayer.Background` to `{ThemeResource SubtleFillColorSecondaryBrush}` / `TertiaryBrush` (exact Fluent tokens); or use composition implicit scale (1.0→1.02, 167 ms Fast-Out-Slow-In) + shadow lift for a more premium feel.
**Acceptance:** hover/pressed/focus visibly distinguish cards; state matches WinUI Gallery card examples. ⚠ runtime verify.
**Effort:** S (states) / M (composition lift).

### UI-03 — Home grid: no scroll anchoring → jumpy virtualization (Medium, confirmed structurally ⚠)
**Evidence:** `HomePage.xaml:283-307` — `ItemsRepeater` inside a bare `ScrollViewer`; no `ItemsRepeaterScrollHost`, no incremental loading (`FilteredDocuments` fully realized; no `ElementClearing` handling). Per [MS docs](https://learn.microsoft.com/en-us/windows/apps/develop/controls/itemsrepeater), without anchoring, realizing/recycling items above the viewport causes visible jumps on large collections; [known anchoring bugs](https://github.com/microsoft/microsoft-ui-xaml/issues/1425) also apply.
**Fix (minimal):** wrap with `ItemsRepeaterScrollHost` (one-line change); consider ListView/GridView if jumpiness persists; add data virtualization via the existing `IncrementalLoadingCollection` when folder counts grow.
**Acceptance:** scroll a 2,000-file folder: no vertical jumps; memory bounded. ⚠ runtime verify.
**Effort:** XS (wrapper) / M (data virtualization).

### UI-04 — Loading UX: opaque full-screen block + page pop-in (Medium, confirmed)
**Evidence:** `ViewerPage.xaml:491-506` — `LoadingOverlay` covers the whole viewport with `SolidBackgroundFillColorBaseBrush` + centered ring; `ShowLoading` toggles Visibility (the declared 0.2 s `OpacityTransition` again won't animate a Visibility change). PDF pages then appear instantly as `Image.Source` is set (`ViewerPage.Viewport.cs:441-462`) — pop-in, no fade.
**Fix:** (a) fade the overlay: set `Opacity` 1↔0 instead of Visibility (or use a connected fade); (b) fade-in each rendered page (`img.Opacity` 0→1 over 167 ms on first set — the ScalarTransition/implicit animation pattern); (c) medium-term: skeleton page placeholders sized like real pages instead of full-screen block, letting the shell stay visible.
**Acceptance:** open a PDF: overlay fades, pages fade in as they complete; no pop. ⚠ runtime verify.
**Effort:** S (fade) / M (skeleton).

### UI-05 — Zoom and navigation motion inconsistent (Medium, confirmed)
**Evidence:** `ViewerPage.Zoom.cs:83` (`disableAnimation: true`), `:91` (`false`), `ViewerPage.Pdf.cs:163` (`true`), `ViewerPage.Sidebar.cs:59` (`true`), `ViewerPage.Search.cs:266` (`true`), `ViewerPage.xaml.cs` SizeChanged re-render. Toolbar zoom animates; Ctrl+wheel/preset/click-to-page jump instantly. Also zoom uses `ScrollViewer.ChangeView` zoom factor — animated zoom on ScrollViewer is non-anchored (center zooms, not cursor-anchored).
**Fix:** pick one policy — animate preset/toolbar zoom (`disableAnimation:false`), keep instant for continuous Ctrl+wheel; add cursor-anchored zoom via composition scale transform on the canvas (bigger change, defer). Consistent "go to page" should animate scroll (167 ms) rather than jump.
**Acceptance:** same interaction always behaves the same; documented policy in code. ⚠ runtime verify.
**Effort:** S.

### UI-06 — Sidebar show/hide is an instant width/visibility jump (Medium, confirmed)
**Evidence:** `ViewerPage.Toolbar.cs:19-20` — `SidebarCard.Visibility` + `SidebarColumn.Width = 240|0`. No animation. Panel content (thumbnails/outline) also switches via `Visibility` (`ViewerPage.Sidebar.cs`) with no transition.
**Fix (minimal):** animate `SidebarColumn.Width` 0↔240 (250 ms, Fast-Out-Slow-In) via Storyboard on `ColumnDefinition` (needs a proxy: animate a `Grid` attached DoubleAnimation — simplest is a small code-behind storyboard on a `Double` using `EnableDependentAnimation`), or keep width fixed and slide the sidebar content with a `TranslateTransform` + clip. Also add `RepositionThemeTransition` to the viewport grid so content reflows smoothly.
**Acceptance:** toggling sidebar animates ~250 ms without layout thrash on large PDFs. ⚠ runtime verify.
**Effort:** S–M.

### UI-07 — Dead/duplicated style infrastructure; hardcoded accents (Medium, confirmed)
**Evidence:**
- `App.xaml:7-10` merges **only** `XamlControlsResources`. `UI/Styles/Colors.xaml`, `Typography.xaml`, `Controls.xaml`, `UI/Templates/FileTemplates.xaml`, `UI/Icons/FileTypeIcons.xaml` are **never merged and never referenced** → dead (grep: zero usages of their keys).
- `ViewerPage.xaml:20-73` and `ViewerToolbar.xaml:9-30` each carry private copies of the same icon-button styles (`IB`/`ITB` vs `IconButtonStyle` in dead Controls.xaml) — duplication.
- `DocumentCard.xaml:69-70` hardcodes the PDF gradient `#FFDC3545/#FFF0505F` while `Colors.xaml` defines `PdfAccentColor #E23350` (dead) — two sources of truth for brand color; gradient is not theme-aware (fine over thumbnails, but centralize).
- `DocumentCard.xaml:15` `ContextButtonStyle` background `#44000000` hardcoded; `:93` chip `#28FFFFFF`, white glyphs — theme-blind (acceptable over thumbnails, but should live as named resources).
- Unused assets: `HOME_DARK.png`, `HOME_LIGHT.png`, `SETTINGDARK.png`, `TEXT_viewer.png`, `Viewer.png` (grep: zero references) — ship-weight and confusion.
- `ViewerPage.xaml:387-390` `MarkdownViewerHost` is a dead WebView2-era leftover ("rendered HTML in WebView2" comment; host never used since WebView2 removal).
**Fix:** delete dead dictionaries/assets/host (fold into audit task T-022); single source for type-accent colors (use Colors.xaml values in DocumentCard via `x:Static`-style resources after merging the dictionary in App.xaml); dedupe icon-button styles into one merged dictionary.
**Acceptance:** App.xaml merges app dictionaries; no duplicate style keys; accent colors defined once. (Build/test green.)
**Effort:** S.

### UI-08 — Settings page: decorative, unwired controls (Medium, confirmed)
**Evidence:** `SettingsPage.xaml:53` "Enable Mica background" `ToggleSwitch IsOn="True"` with **no x:Name/handler** — grep for `Mica|ToggleSwitch|IsOn` in `SettingsPage.xaml.cs` → nothing; `:67` "Show page thumbnails" likewise unwired; theme ComboBox is wired (`ThemeComboBox_SelectionChanged` → `App.SetTheme`). Non-functional toggles that reset to "on" every launch are a trust-killer.
**Fix:** wire Mica toggle to swap `Window.SystemBackdrop` (MicaBackdrop ↔ DesktopAcrylicBackdrop/None) and persist via `SettingsStore`; wire thumbnails toggle to `ViewerPage` sidebar behavior; persist both.
**Acceptance:** toggles change behavior and survive restart. ⚠ runtime verify.
**Effort:** S.

### UI-09 — Theme switching exists but coverage unverified (Low ⚠)
**Evidence:** `App.SetTheme` sets `rootElement.RequestedTheme` + title-bar colors (`App.xaml.cs:253-270`). Static review found the risky hardcoded colors above (UI-07); `MicaBackdrop` adapts automatically. Remaining risk: custom-drawn surfaces — `SpreadsheetViewer` grid colors (`_bgGrid/_gridLine/_selection` fields — **check whether they're theme-aware**, they're code-defined Color values in `SpreadsheetViewer.xaml.cs`) and Win2D-rendered content won't follow `RequestedTheme` automatically.
**Fix:** audit the three code-defined grid colors against theme (`ActualTheme` switch handler); standardize.
**Acceptance:** dark mode: spreadsheet grid, selection, headers all legible. ⚠ runtime verify.
**Effort:** S.

### UI-10 — Smoothness inventory (mixed, partially ⚠)
| Surface | Mechanism | Verdict |
|---|---|---|
| PDF scroll | viewport-culled canvas + page cache + eviction | structurally sound (see performance-audit.md) |
| PDF page insertion | instant Image swap | pop-in → UI-04 fix |
| Zoom | `ChangeView` zoom, mixed animated/instant | inconsistent → UI-05 |
| Home grid | ItemsRepeater, no anchor host | jumpy risk → UI-03 |
| Spreadsheet | Win2D viewport-culled draw; per-cell `CanvasTextFormat` alloc (FIND-027); header `ChangeView` sync correct | good, minor churn |
| Text/code | capped reads, `ScrollViewer` default smooth | fine |
| Tab strip | TabView default motion | fine (Equal width mode arguable) |
| Window resize | SizeChanged → full re-render per event (`ViewerPage.xaml.cs:52-56`) — no debounce | risk of render storm while dragging edges ⚠ |
**Fix:** debounce SizeChanged (~100 ms) or re-render on final size only; fold others into UI-03/04/05.
**Effort:** XS (debounce).

---

## Comparison — QuickView vs. references

| Capability | QuickView (static) | SumatraPDF | Windows 11 native (Explorer/Photos) | Gap |
|---|---|---|---|---|
| Theme (light/dark/system) | ✅ standard resources; Settings combo | limited (UI settings) | ✅ full | **UI-07/09** hardcoded accents, unverified dark surfaces |
| Mica/backdrop | ✅ Mica, transparent tab strip | ❌ (WinForms-classic UI) | ✅ | — (UI-08 toggle unwired) |
| Tabs in title bar | ✅ Chrome pattern | ✅ (basic tabs) | ✅ (Explorer 22H2+) | **UI-01** switch is a hard cut |
| Hover/pressed states on primary surfaces | ❌ cards stripped | n/a (menu-driven) | ✅ state layers everywhere | **UI-02** |
| Entrance transitions | ✅ declared for first-load only | ❌ | ✅ everywhere | **UI-01/04** not on switches/content |
| Loading feedback | ProgressRing + opaque overlay | instant / plain | skeletons + fades | **UI-04** |
| Scroll stability large lists | no anchor host | excellent (custom) | excellent | **UI-03** |
| Zoom motion | mixed | instant (by design) | smooth, anchored | **UI-05** |
| Perceived speed | strong (96-DPI render pipeline) | best-in-class | strong | maintain; FIND-001 leaks undermine long sessions |

**Positioning:** QuickView already *looks* more modern than SumatraPDF (Mica, acrylic, Fluent controls). The gap to Windows-native polish is entirely in **interaction feedback**: nothing animates between states, and primary surfaces give no hover response. Fluent's guidance is explicit that built-in transitions at 167–250 ms are the fix — no custom animation framework needed.

## Recommended order (smallest → largest)

1. **UI-03** `ItemsRepeaterScrollHost` (one element) + **UI-10** resize debounce + **UI-04a** overlay fade — same-day wins.
2. **UI-02** card visual states via Fluent tokens (S).
3. **UI-01** tab cross-fade using existing Opacity transitions (S).
4. **UI-05/06** motion policy unification + sidebar animation (S–M).
5. **UI-07** dead-style/asset cleanup folded into audit T-022 (S).
6. **UI-08** wire Settings toggles (S).
7. Later polish: connected animation Home-card → viewer tab (signature Fluent moment), composition hover lift on cards, skeleton loaders.
