# QuickView — Task Backlog

> Based on code review (2026-09-25) and live audit of the codebase.
> Ordered by impact and dependency. Do not add new format support until tasks 1–6 are complete.

---

## Legend
- ✅ Already done / confirmed working
- 🔴 Crash / data-loss class bug — fix before shipping
- 🟠 High impact on performance or RAM
- 🟡 Medium — correctness or UX defect
- 🔵 Architecture / cleanup
- ⚪ Future / low priority

---

## What Is Already Working ✅

| Area | Status |
|---|---|
| PDFium single document, shared `Pdfium.Gate` lock | ✅ Confirmed — no second `FPDF_LoadDocument` |
| `FPDF_LoadDocument` uses `LPUTF8Str` (non-ANSI paths work) | ✅ Fixed |
| PDF search uses the open `PdfDocumentState`, not a file re-open | ✅ Confirmed |
| Plain text uses `TextFileReader.ReadCapped` (not `ReadToEnd`) | ✅ Fixed — 1 MB cap |
| Code viewer uses `TextFileReader.ReadCapped` inside `CodeRenderer` | ✅ Fixed — 500 KB / 10 K line cap |
| Text/code shows truncation banner | ✅ Working |
| PDF first-page progressive render before prefetch | ✅ Working |
| Page eviction ±6 with `_renderedPages` cleanup | ✅ Working |
| PDF 300 ms search debounce | ✅ Working |
| Search result count (`1 of N`) for PDF, text, and code | ✅ Working |
| Search highlights on code use black forced text (visibility fix) | ✅ Fixed today |
| Search live-updates on delete (query extracted before routing) | ✅ Fixed today |
| `ClearRichText` called before `ApplyToRichText` on every search | ✅ Fixed today |
| Settings page navigation wired | ✅ Working |

---

## 🔴 Crash / Correctness Bugs

### C1 — Dispose race on document close
**File:** `ViewerPage.Pdf.cs`, `PdfDocumentState.cs`  
**Problem:** When the user opens a new file quickly, `_pdfState?.Dispose()` is called at the top of `LoadDocumentAsync` while a background `RenderPageAsync` or `PrefetchPagesAroundAsync` task may still hold the `IntPtr`. `PdfDocumentState.Dispose` calls `FPDF_CloseDocument` under the lock, but `RenderPageAsync` checks `state.Handle == IntPtr.Zero` outside the lock before entering. A second `Dispose` (if called) would close an already-closed handle.  
**Fix:** Add a generation counter to `PdfDocumentState`. Each render task captures the generation at start and checks it before calling into native code. `Dispose` sets the handle to `IntPtr.Zero` inside the lock (already done in `PdfDocumentState`); add a null guard in `RenderPageAsync` after `Lock` entry.

### C2 — `GetCodeText` breaks file after search highlight
**File:** `ViewerPage.Text.cs` → `GetCodeText()`  
**Problem:** `GetCodeText` reconstructs the file text by iterating `RichTextBlock.Blocks` and extracting `Run.Text`. After `ApplyToRichText` splits a `Run` into `[Run, InlineUIContainer, Run, ...]`, `GetCodeText` only collects the `Run` parts and skips the `InlineUIContainer` text. Saving while a search is active will corrupt the file (matched text is silently dropped).  
**Fix:** Call `SearchHighlighter.ClearRichText(TextViewerBlock)` at the start of `SaveButton_Click` and `SaveAsButton_Click` before extracting the text. Re-apply the current search state after saving if needed.

### C3 — Password PDFs silently fail
**File:** `PdfRenderer.cs` → `OpenDocumentAsync`  
**Problem:** `FPDF_LoadDocument(path, null)` with a null password returns `IntPtr.Zero` for password-protected files. `DescribeLastError()` returns a message but there is no retry UI.  
**Fix:** Check `FPDF_GetLastError() == ErrPassword` and show a password input dialog (`ContentDialog`). Retry with the entered password string.

---

## 🟠 High Impact — Performance & RAM

### H1 — Thumbnail memory never freed
**File:** `ViewerPage.Pdf.cs` → `RenderThumbnailAsync`, `_renderedThumbs`  
**Problem:** `_renderedThumbs` only grows. Sidebar thumbnails accumulate `WriteableBitmap` instances for all visited pages for the entire lifetime of the open document. A 200-page PDF with all thumbs visited at 96 DPI = ~200 × 1.9 MiB ≈ 380 MiB of thumbnail RAM.  
**Fix:** Evict thumbnails outside ±10 pages from the current page. On evict, set `Thumbnails[i].Thumbnail = null` and remove from `_renderedThumbs`. Mirror the existing main-page eviction pattern.

### H2 — No virtualization — all page Borders built on open
**File:** `ViewerPage.Pdf.cs` → `LoadPdfAsync`  
**Problem:** All `N` page `Border` elements are added to the `StackPanel` before first paint. An 800-page PDF creates 800 layout nodes plus 800 `PageThumbnailItem` ViewModels immediately. This blocks the UI thread on open for large docs.  
**Priority:** Medium-high — not a crash but degrades open time.  
**Fix (later milestone):** Replace `StackPanel` with `ItemsRepeater` + `StackLayout`. Build a `PageSizeProvider` that knows each page's logical size so the repeater can compute scroll extent without realizing every item. Realise ±2 viewports of Borders. This is a large refactor — track separately.

### H3 — Zoom re-raster at 768 DPI absolute cap
**File:** `ViewerPage.Zoom.cs` → `ReRenderVisiblePagesAsync`  
**Problem:** The 768 DPI cap is an absolute value, not relative to display DPI. On a 200% display (192 DPI physical), a 400% zoom would attempt `192 * 4 = 768 DPI` raster for every visible page. On a 4K display (288 DPI), this is still too high. A letter page at 768 DPI = 118 MiB per page.  
**Fix:** Cap at `min(768, displayDpi * 2.5)` using `XamlRoot.RasterizationScale * 96` as display DPI, or better, cap at a bitmap pixel limit (e.g., 4096 × 4096 per page).

### H4 — Code search: no debounce on `ApplyToRichText`
**File:** `ViewerPage.Search.cs` → `FindBox_TextChanged`  
**Problem:** Text/code search runs synchronously on every keystroke. For a 10 000-line `RichTextBlock`, `ClearRichText` + `ApplyToRichText` modifies thousands of `Paragraph.Inlines` on the UI thread. This can freeze the UI visibly.  
**Fix:** Add a 150 ms debounce for text/code search, same pattern as PDF (cancel previous `_searchDebounceCts`, await `Task.Delay(150, ct)`). Since `FindInText` / `FindInCode` + `ApplyToRichText` are all synchronous and run on the UI thread, the debounce alone is the main win.

### H5 — Disk thumbnail cache has no LRU eviction
**File:** `Infrastructure/Storage/ThumbnailCache.cs`, `ThumbnailService.cs`  
**Problem:** The disk cache at `%LocalAppData%\QuickView\thumbcache\` grows without bound. No cleanup is called automatically.  
**Fix:** In `ThumbnailCache`, track access times (file `LastAccessTime`). On startup or when cache exceeds a size threshold (e.g., 500 MB), delete the oldest entries down to 80% capacity.

---

## 🟡 Medium — UX / Correctness

### M1 — Plain text search highlights only active match (no passive highlights)
**File:** `Rendering/Search/SearchHighlighter.cs` → `ApplyToTextBox`  
**Problem:** `TextBox.Select()` supports only one selection. Passive matches (all matches except the active one) are not highlighted. The user sees "3 of 12" but only one orange highlight.  
**Fix (short term):** This is a TextBox limitation. Add a small annotation below the search box explaining "active match shown". For a real fix, switch plain text to a read-only `RichTextBlock` with the same `ApplyToRichText` approach used for code. The downside is losing TextBox's native selection UX. Consider making this a setting or just accept the limitation for now.

### M2 — `SaveButton` is always disabled (no change tracking)
**File:** `ViewerPage.Text.cs`  
**Problem:** `_textHasChanges` is set to `false` on load and never set to `true`. `SaveButton.IsEnabled = false` is set but never unset. The TextBox has no `TextChanged` handler wired to set `_textHasChanges = true`. The Save button appears in the toolbar but never activates.  
**Fix options:** (a) Remove Save/SaveAs entirely — this is a viewer, not an editor. (b) Wire `PlainTextBlock.TextChanged` to set `_textHasChanges = true` and `SaveButton.IsEnabled = true`. Option (a) is correct for a Quick Look viewer.

### M3 — Rotation gaps / distortion
**File:** `ViewerPage.Helpers.cs` or `ViewerPage.Toolbar.cs` → `Rotate()`  
**Problem:** The review notes `Rotate()` sets `Border.Width = physW / DisplayScale` while placeholders were created at `physW`. Mixed DIP/pixel spaces cause gaps or incorrect sizing after rotation.  
**Fix:** Re-derive `physW`/`physH` from the rotated page size and use the same DIP formula for both placeholder creation and rotated sizing. Ensure `DisplayScale` is applied consistently.

### M4 — `Document.App` namespace vs `QuickView` namespace mismatch
**File:** `App.xaml.cs`, any file using `Application.Current as Document.App`  
**Problem:** The app class lives in namespace `Document` while all features use `QuickView.*`. The cast `(Application.Current as Document.App)?.MainWindow` in `SaveAsButton_Click` and potentially other places is fragile and confusing.  
**Fix:** Either move `App` into the `QuickView` namespace or create an accessor like `App.CurrentWindow` as a static property to avoid the repeated cast.

---

## 🔵 Architecture / Cleanup

### A1 — Wire `RendererRegistry` or delete it
**Files:** `Rendering/RendererRegistry.cs`, `Core/Interfaces/IFileRenderer.cs`, `Features/Viewer/Components/ViewerPage.Pdf.cs`  
**Status:** `RendererRegistry` has no callers. `IFileRenderer` implementations exist for images, HTML, video, Office (DOCX, XLSX, PPTX) but are never invoked. `LoadDocumentAsync` uses a hardcoded `if/else` chain and shows `ShowInfoCard` for everything not handled.  
**Fix:** Before adding new format support: wire `RendererRegistry.Register()` for each renderer at startup (in `App.xaml.cs`), change `LoadDocumentAsync` to call `RendererRegistry.GetRenderer(ext)?.RenderAsync(...)`, and remove the hardcoded chain. This prevents a third fork for every new format.

### A2 — `TextRenderer.cs` is unused
**File:** `Rendering/Text/TextRenderer.cs`  
**Problem:** `TextRenderer.RenderAsync` exists and implements chunked paragraph splitting for large text files, but `LoadTextFileAsync` calls `TextFileReader.ReadCapped` directly and assigns to a `TextBox`, bypassing `TextRenderer` entirely.  
**Fix:** Either use `TextRenderer` in `LoadTextFileAsync` (switching to RichTextBlock like code), or delete `TextRenderer` and keep the current simpler path. Decide which approach to take when virtualizing the text viewer.

### A3 — `ViewerViewModel.cs` is a thin shell
**File:** `Features/Viewer/ViewerViewModel.cs`  
**Problem:** The VM has no behavior; all state lives in `ViewerPage.xaml.cs` as fields. This is fine for now but will hurt when adding MVVM data binding for future features.  
**Fix (later):** No action needed until the viewer grows significantly. Flag for next major refactor.

---

## ⚪ Future / Later Milestones

### F1 — Virtualize PDF page layout (ItemsRepeater)
Replace `StackPanel` with `ItemsRepeater` + `StackLayout`. Realize only ±2 viewports of page Borders. Maintain a `List<double>` of page heights so the scroll extent is correct without realizing every element. This is the biggest RAM win for large documents.

### F2 — Virtualized line list for code viewer
Replace `RichTextBlock` (10 000 `Paragraph` nodes) with an `ItemsRepeater` of plain `TextBlock` rows. Keep ~200 realized rows. Line numbers from the realized index, not a pre-built string. Syntax color: one `SolidColorBrush` per row (the `GetLineDominantColor` logic already exists). Search highlight: per-row background on the visible set only. This fixes H4 entirely and makes large code files scroll at 60 fps.

### F3 — Image viewer via `IFileRenderer`
`ImageRenderer` implementation exists. Wire through `RendererRegistry` (A1 must be done first). Use `DecodePixelWidth` and background decode so large images don't block UI.

### F4 — Progressive PDF quality (96 DPI preview → display DPI)
On first render, paint a fast 96 DPI placeholder in the slot, then replace with `RenderDpi` quality in the background. This makes the first scroll feel instant even on slow machines.

### F5 — Password PDF dialog
See C3. Requires a `ContentDialog` with a `PasswordBox`. Low complexity, good user value.

### F6 — Markdown: replace WebView2 with RichTextBlock
`MarkdownRenderer` spawns a Chromium process for every `.md` file. For a Quick Look viewer, a `RichTextBlock`-based markdown renderer (using the existing Markdig or a simple parser) is lighter. Do not add a second WebView2 instance. Revisit after line virtualization (F2) since both use `RichTextBlock`.

### F7 — Office / video format support
`DocxRenderer`, `XlsxRenderer`, `PptxRenderer`, `MediaRenderer` implementations exist but are unreachable. Wire through `RendererRegistry` after A1. Do not activate COM Office automation until F1–F2 are complete to avoid memory pressure on large documents.

---

## Recommended Order

```
C2 (save corruption)  →  C1 (dispose race)  →  M2 (save button)
  →  H4 (search debounce)  →  H1 (thumbnail eviction)
  →  A1 (wire renderer registry)  →  F3 (images)
  →  H3 (zoom DPI cap)  →  C3 (password dialog)
  →  F2 (virtualize code lines)  →  F1 (virtualize PDF pages)
  →  F7 (Office / video)
```

Do not start F1 (PDF virtualization) until the renderer registry and image viewer are wired — the refactor scope is large and it is easier to do it with the registry pattern in place.
