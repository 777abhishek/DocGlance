# QuickView — Task Backlog

> Last updated: 2026-09-27
> Ordered by impact and dependency. Do not add new format support until tasks C1–H4 are complete.

---

## Legend
- ✅ Done / confirmed working
- 🔴 Crash / data-loss — fix before shipping
- 🟠 High impact on performance or RAM
- 🟡 Medium — correctness or UX defect
- 🔵 Architecture / cleanup
- ⚪ Future / low priority

---

## What Is Already Working ✅

| Area | Status |
|---|---|
| Single PDFium session, shared `Pdfium.Gate` lock | ✅ |
| `FPDF_LoadDocument` uses UTF-8 path (non-ANSI paths work) | ✅ |
| PDF search uses the open `PdfDocumentState`, not a file re-open | ✅ |
| Plain text uses `TextFileReader.ReadCapped` (1 MB cap, never ReadToEnd) | ✅ |
| Code viewer uses `TextFileReader.ReadCapped` (500 KB / 10 K line cap) | ✅ |
| Text/code shows truncation banner | ✅ |
| PDF first-page progressive render before prefetch | ✅ |
| Page bitmap eviction ±6 pages (`_renderedPages` cleanup) | ✅ |
| PDF 300 ms search debounce | ✅ |
| Search result count (`1 of N`) for PDF, text, and code | ✅ |
| Search highlights on code — black forced text (visibility fix) | ✅ |
| Search live-updates on delete (query extracted before routing) | ✅ |
| `ClearRichText` called before `ApplyToRichText` on every search | ✅ |
| Settings page navigation wired | ✅ |
| App icon — multi-size `.ico` (16, 20, 24, 32, 48, 64, 128, 256 px) | ✅ |
| File association system — full ProgID registry, Settings Register/Unregister | ✅ |
| Self-contained installer — `WindowsAppSDKSelfContained=true`, 0 warnings | ✅ |

---

## 🔴 Crash / Correctness Bugs

### C1 — Dispose race on rapid file switch
**File:** `Rendering/Pdf/PdfDocumentState.cs`, `Features/Viewer/Components/ViewerPage.Pdf.cs`

**Problem:** `LoadDocumentAsync` calls `_pdfState?.Dispose()` then immediately starts a new load. A background `RenderPageAsync` or `PrefetchPagesAroundAsync` task that was started on the old state can still be running. The only guard is a `ReferenceEquals(_pdfState, pdfState)` check on the dispatcher thread *after* the render completes — by which time `FPDF_CloseDocument` may already have been called on the handle the background task is using.

**Fix:**
1. Add `int _generation` field to `PdfDocumentState` (or as a counter in `ViewerPage`).
2. Each render task captures `generation` at start; checks `generation == _currentGeneration` inside the lock before accessing the handle.
3. `Dispose()` already zeros the handle under `Pdfium.Gate` — the generation check is the additional safety net for the window between task dispatch and lock entry.

---

### C2 — `GetCodeText()` corrupts file when saved after search
**File:** `Features/Viewer/Components/ViewerPage.Text.cs` → `SaveButton_Click`, `GetCodeText()`

**Problem:** After `ApplyToRichText` runs, the `RichTextBlock` inline structure is split (one `Run` becomes `[Run | InlineUIContainer | Run]`). `GetCodeText()` only collects `Run.Text`, silently dropping `InlineUIContainer` spans. Saving while a search is active truncates the matched text in the saved file.

**Fix:** Call `SearchHighlighter.ClearRichText(TextViewerBlock)` at the start of `SaveButton_Click` and `SaveAsButton_Click` before calling `GetCodeText()`. Re-apply the current search state after saving if `_textSearchState != null`.

---

## 🟠 High Impact — Performance & RAM

### H1 — Thumbnail memory never freed
**File:** `Features/Viewer/Components/ViewerPage.Pdf.cs` → `_renderedThumbs`

**Problem:** `_renderedThumbs` entries are never removed — only cleared on new document load. `PrefetchPagesAroundAsync` evicts main-view bitmaps outside ±6 pages but has no equivalent eviction for thumbnails. Every visited thumbnail page keeps its `WriteableBitmap` for the entire document lifetime. A 200-page PDF with all thumbs visited ≈ 380 MB of thumbnail RAM.

**Fix:** After the existing `_renderedPages` eviction loop in `PrefetchPagesAroundAsync`, add an equivalent loop for `_renderedThumbs`: evict entries outside ±10 pages from `centerPage`, set `Thumbnails[i].Thumbnail = null`, and remove from `_renderedThumbs`.

---

### H3 — Zoom DPI cap is a hardcoded absolute value
**File:** `Features/Viewer/Components/ViewerPage.Zoom.cs` → `ReRenderVisiblePagesAsync`

**Problem:** `Math.Min(effectiveDpi, 768f)` is a fixed absolute cap. On a 200% display (192 DPI physical), 400% zoom attempts 768 DPI — 118 MB per letter page. On a 100% display (96 DPI), 768 DPI is unnecessarily high.

**Fix:** Replace with `Math.Min(effectiveDpi, DisplayScale * 96f * 2.5f)` — caps at 2.5× the physical display DPI. On a 96 DPI screen: 240 DPI max. On a 192 DPI screen: 480 DPI max. This is what Rune does.

---

### H4 — Text/code search has no debounce
**File:** `Features/Viewer/Components/ViewerPage.Search.cs` → `FindBox_TextChanged`

**Problem:** The `IsTextViewerActive()` branch returns immediately before the 300 ms debounce block. `SearchInTextViewer` → `ClearRichText` + `ApplyToRichText` runs synchronously on the UI thread on every keystroke. For a 10 000-line `RichTextBlock` this visibly freezes the UI.

**Fix:** Add a 150 ms debounce to the text/code path using the same `_searchDebounceCts` pattern already used for PDF. Move `SearchInTextViewer` behind the debounce delay.

---

### H5 — Disk thumbnail cache has no eviction
**File:** `Infrastructure/Storage/ThumbnailCache.cs`, `ThumbnailService.cs`

**Problem:** `%LocalAppData%\QuickView\thumbcache\` grows without bound. No cleanup runs automatically.

**Fix:** Track access times via `File.SetLastAccessTime`. On startup (or when cache size exceeds 500 MB threshold), delete entries sorted by `LastAccessTime` ascending until cache is at 80% of threshold.

---

## 🟡 Medium — UX / Correctness

### M1 — Plain text search shows only the active match, not all matches
**File:** `Rendering/Search/SearchHighlighter.cs` → `ApplyToTextBox`

**Problem:** `TextBox.Select()` supports only one selection — passive matches are not visible. The user sees "3 of 12" but only one highlight.

**Fix (short term):** Add a note below the search box ("showing active match"). Long term: switch plain text to a read-only `RichTextBlock` with `ApplyToRichText` (same as code viewer) to get all-match highlighting.

---

### M2 — SaveButton is always disabled; `_textHasChanges` never set to true
**File:** `Features/Viewer/Components/ViewerPage.Text.cs`

**Problem:** `SaveButton.IsEnabled = false` is set on every load and never enabled. `_textHasChanges` is only ever set to `false`. No `TextChanged` handler exists on `PlainTextBlock` or `TextViewerBlock`. The Save button appears in the toolbar but is permanently grayed-out.

**Fix (recommended):** Remove `SaveButton` and `SaveAsButton` from the toolbar entirely. QuickView is a viewer, not an editor. Removing them also eliminates the C2 save-corruption risk.

---

### M3 — Rotation distorts or creates gaps
**File:** `Features/Viewer/Components/ViewerPage.Helpers.cs`

**Problem:** `Rotate()` sets `Border.Width = physW / DisplayScale` while placeholders were created at `physW` (physical pixels). Mixed DIP/pixel spaces cause gaps or incorrect sizing after rotation.

**Fix:** Re-derive `physW`/`physH` from the rotated page size using the same DIP formula used at placeholder-creation time. Apply `DisplayScale` consistently.

---

### M4 — `App` class is in `Document` namespace, not `QuickView`
**File:** `App.xaml.cs`

**Problem:** `Application.Current as Document.App` is used in save handlers and other places. The namespace mismatch is fragile and confusing.

**Fix:** Move `App` to the `QuickView` namespace, or add a `static QuickView.App.Current` accessor.

---

## 🔵 Architecture / Cleanup

### A1 — Wire `RendererRegistry` or delete it
**Files:** `Rendering/RendererRegistry.cs`, `App.xaml.cs`, `Features/Viewer/Components/ViewerPage.Pdf.cs`

**Status:** `RendererRegistry.Register()` is never called. `LoadDocumentAsync` uses a hardcoded `if/else` chain. `IFileRenderer` implementations exist for images, video, HTML, Office (DOCX, XLSX, PPTX) but are completely unreachable.

**Fix:** In `App.xaml.cs` `OnLaunched`, call `RendererRegistry.Register()` for each renderer. In `LoadDocumentAsync`, after the existing PDF/text/code checks, fall through to `RendererRegistry.GetRenderer(ext)?.RenderAsync(...)` before showing the info card. This unblocks F3 (images) and F7 (Office) without another hardcoded fork.

---

### A2 — `TextRenderer.cs` is unused
**File:** `Rendering/Text/TextRenderer.cs`

**Problem:** `LoadTextFileAsync` bypasses `TextRenderer` entirely and assigns text directly to a `TextBox`. `TextRenderer` implements chunked `RichTextBlock` paragraph splitting but is never called.

**Fix:** Either adopt `TextRenderer` in `LoadTextFileAsync` (switching plain text to `RichTextBlock` for full-match search highlights, resolving M1), or delete it. Decide at F2 milestone.

---

## ⚪ Future / Later Milestones

### F1 — Virtualize PDF pages (ItemsRepeater)
Replace `StackPanel` with `ItemsRepeater + StackLayout`. Maintain `List<double>` of page heights so scroll extent is correct without realizing every element. Realize ±2 viewports of `Border` slots. Biggest RAM win for large documents; large refactor — do after A1.

### F2 — Virtualize code viewer lines
Replace `RichTextBlock` (10 000 `Paragraph` nodes) with `ItemsRepeater` of plain `TextBlock` rows. Keep ~200 realized rows. Line numbers from realized index. Syntax color as one brush per row. Fixes H4 permanently and makes large files scroll at 60 fps.

### F3 — Image viewer via `IFileRenderer`
`ImageRenderer` exists. Wire through `RendererRegistry` (A1 required first). Use `DecodePixelWidth` and background decode.

### F4 — Progressive PDF quality (96 DPI preview → display DPI)
Paint a fast 96 DPI placeholder first, then replace with full-quality in the background. Makes first scroll feel instant on slow machines.

### F5 — Password PDF dialog
Show `ContentDialog` with `PasswordBox` when `FPDF_GetLastError() == ErrPassword`. Retry `FPDF_LoadDocument` with the entered password. Low complexity, good user value.

### F6 — Replace Markdown WebView2 with RichTextBlock
`MarkdownRenderer` spawns a Chromium process per `.md` file. A `RichTextBlock`-based renderer (Markdig) is lighter and faster for a Quick Look viewer. Do after F2.

### F7 — Office / video format support
`DocxRenderer`, `XlsxRenderer`, `PptxRenderer`, `MediaRenderer` implementations exist but are unreachable. Wire through `RendererRegistry` after A1. Do not activate COM Office until F1–F2 are complete.

---

## Recommended Order

```
C2 (save corruption: ClearRichText before save)
  → M2 (just remove Save/SaveAs — they're a viewer not editor)
  → H4 (text/code search debounce — 150 ms)
  → C1 (dispose race: generation counter)
  → H1 (thumbnail eviction: ±10 pages)
  → H3 (zoom DPI cap: displayDpi × 2.5 formula)
  → A1 (wire RendererRegistry)
  → F3 (images via IFileRenderer)
  → C3 / F5 (password dialog)
  → F2 (virtualize code lines)
  → F1 (virtualize PDF pages — largest refactor)
  → F7 (Office / video)
```
