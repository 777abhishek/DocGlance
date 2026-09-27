# QuickView — Code Review

> First pass: 2026-09-25 | Updated: 2026-09-27
> Source: Rendering/Pdf, Features/Viewer, Rendering/Code, Rendering/Text, App.xaml.cs

---

## Current State (2026-09-27)

Build: **0 errors, 0 warnings** (CA1416 and MVVMTK0045 suppressed project-wide — correct for a Windows-only app).
Installer: `QuickView-Setup-x64.exe` — 40.3 MB, self-contained, `WindowsAppSDKSelfContained=true`.
App icon: multi-size `.ico` (16, 20, 24, 32, 48, 64, 128, 256 px) — crisp at all DPI and contexts.

---

## What Is Solid ✅

| Area | Detail |
|---|---|
| PDFium single session | One `FPDF_InitLibrary`, one `Pdfium.Gate` lock across all render/search/text calls |
| UTF-8 paths | `FPDF_LoadDocument` via `LPUTF8Str` — non-ANSI filenames work |
| Pooled render buffers | `ArrayPool<byte>.Shared.Rent` for BGRA frames; returned in `finally` |
| Progressive load | First page paints before prefetch starts |
| Page bitmap eviction | ±6 pages kept in `_renderedPages`; others have `Image.Source` cleared |
| Capped text reads | `TextFileReader.ReadCapped` — 1 MB for plain text, 500 KB / 10 K lines for code |
| Truncation banner | Shown when file exceeds cap |
| PDF search debounce | 300 ms; cancels previous token correctly |
| Search result count | "1 of N" for PDF, text, and code |
| Code highlight visibility | Forced black text on yellow/orange background |
| Search clears on delete | Query extracted before routing check; `ClearRichText` before every `ApplyToRichText` |
| File associations | Full ProgID registry in `HKCU\Software\Classes`; Settings page Register/Unregister |
| Deployment | `WindowsAppSDKSelfContained=true` bundles runtime; no external dependencies |

---

## Open Bugs (verified in code)

### C1 — Dispose race | `PdfDocumentState.cs`, `ViewerPage.Pdf.cs`

`LoadDocumentAsync` calls `_pdfState?.Dispose()` and immediately starts a new load. A background render task on the old state checks `ReferenceEquals(_pdfState, pdfState)` on the dispatcher thread *after* rendering completes — but `FPDF_CloseDocument` may have already run while the native render was in flight. The `IntPtr.Zero` guard inside `Pdfium.Gate` is present but does not cover the window between render dispatch and lock entry.

**No generation counter exists.** Fix: add `int _generation` to `ViewerPage`; capture it per-task; check it inside the lock before using the handle.

---

### C2 — Save corruption after search | `ViewerPage.Text.cs`

`SaveButton_Click` calls `GetCodeText()` which walks `TextViewerBlock.Blocks` collecting `Run.Text`. After `ApplyToRichText` splits runs around highlights, `GetCodeText()` silently drops `InlineUIContainer` spans. Saving while a search is active corrupts the file.

Additionally, `_textHasChanges` is never set to `true` and `SaveButton` is never enabled by any `TextChanged` handler. The Save button is structurally present but permanently grayed-out.

**Recommended fix:** Remove Save/SaveAs from the toolbar entirely. QuickView is a viewer.

---

### C3 — Password PDFs silently fail | `PdfRenderer.cs`

`FPDF_LoadDocument(path, null)` always passes `null` password. On failure, `DescribeLastError()` returns `"This PDF is password protected."` and `ShowErrorCard` is called. No retry dialog, no `PasswordBox`. The document is simply unreadable.

---

## Performance Issues (verified in code)

### H1 — Thumbnail RAM grows unbounded | `ViewerPage.Pdf.cs`

`PrefetchPagesAroundAsync` evicts main-view bitmaps outside ±6 pages. There is **no equivalent eviction for `_renderedThumbs`**. Entries are only cleared when a new document loads. Every visited thumbnail page keeps its `WriteableBitmap` alive for the entire document session. 200-page PDF, all thumbs visited ≈ 380 MB.

**Fix:** Evict `_renderedThumbs` entries outside ±10 pages. Mirror the existing `_renderedPages` eviction loop.

---

### H3 — Zoom DPI cap is absolute 768 | `ViewerPage.Zoom.cs`

```csharp
effectiveDpi = Math.Min(effectiveDpi, 768f);  // hardcoded
```

768 DPI produces a 118 MB bitmap for a single letter page. No relation to the actual display DPI. `DisplayScale` (the XAML rasterization scale) is available but unused in this calculation.

**Fix:** `Math.Min(effectiveDpi, DisplayScale * 96f * 2.5f)` — caps relative to physical display DPI.

---

### H4 — Text/code search has no debounce | `ViewerPage.Search.cs`

```csharp
if (IsTextViewerActive())
{
    SearchInTextViewer(query, matchCase);
    return;  // ← bypasses the 300ms debounce entirely
}
```

`ClearRichText` + `ApplyToRichText` on a 10 000-line `RichTextBlock` runs synchronously on the UI thread on every keystroke. Visible freeze on large files.

**Fix:** Move `SearchInTextViewer` behind a 150 ms debounce using the same `_searchDebounceCts` pattern already used for PDF.

---

### H5 — Disk thumbnail cache unbounded | `ThumbnailCache.cs`

`%LocalAppData%\QuickView\thumbcache\` grows without limit. No LRU eviction, no size threshold, no cleanup on startup.

---

## Architecture

### A1 — RendererRegistry is completely unwired

`RendererRegistry.Register()` is never called anywhere in the codebase. `App.xaml.cs` `OnLaunched` does not register any renderer. `LoadDocumentAsync` dispatches by hardcoded extension chain (`if ext == ".pdf" ... else if IsMarkdownFile ... else ShowInfoCard`). `IFileRenderer` implementations for images, HTML, video, and Office exist but are unreachable.

```
RendererRegistry.GetRenderer()  ← zero call sites
RendererRegistry.Register()     ← zero call sites
ImageRenderer, MediaRenderer, DocxRenderer, XlsxRenderer, PptxRenderer, HtmlRenderer
                                ← all exist, all unreachable
```

This must be wired before any new format is added, otherwise every format forks another `else if` chain.

### A2 — `TextRenderer.cs` is unused

`LoadTextFileAsync` bypasses `TextRenderer` and assigns text directly to a `TextBox`. `TextRenderer` (chunked `RichTextBlock` paragraph splitting) is never called.

---

## Memory Model Reference

BGRA uncompressed. Formula: `px = round(pt × dpi / 96)`. Letter page = 612×792 pt.

| Raster | Pixels | Bytes/page | Context |
|---|---|---|---|
| Thumbnail 96 DPI | 612×792 | 1.9 MB | Sidebar — never freed (H1) |
| Base 192 DPI | 1224×1584 | 7.4 MB | Normal view, ±6 kept |
| 200% zoom (384 DPI) | 2448×3168 | 29.6 MB | Re-render |
| Cap 768 DPI | 4896×6336 | 118.4 MB | High zoom (H3) |

---

## What Not To Do

- Do not raise `RenderDpi` above 192 as a default. Re-render at zoom-relative DPI instead.
- Do not add a second PDFium library (PdfSharpCore is already being pulled in — remove it from hot paths).
- Do not add WebView2 for Office rendering; wire `RendererRegistry` first.
- Do not add another `else if` to `LoadDocumentAsync` for a new format — wire `RendererRegistry` instead.
- Do not enable `ZoomMode` on the `ScrollViewer` — Ctrl+Wheel zoom is already implemented correctly.

---

## Recommended Next Steps

1. **C2 + M2** — Remove Save/SaveAs. Eliminates the save-corruption risk in one move.
2. **H4** — Add 150 ms debounce to text/code search. 2-line change, immediate UX win.
3. **C1** — Add generation counter to `ViewerPage`. Prevents native crashes on rapid file switching.
4. **H1** — Thumbnail eviction ±10 pages. Copy the existing `_renderedPages` eviction pattern.
5. **H3** — Replace `768f` cap with `DisplayScale * 96f * 2.5f`. One-line change.
6. **A1** — Wire `RendererRegistry`. Unlocks images, Office, video without code duplication.
