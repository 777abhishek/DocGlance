# QuickView — Codebase Overview (verified 2026-10-11, HEAD `40cdfcd`)

Everything below was verified against source at audit time. Where a claim could not be verified it is marked **unverified**.

## 1. Project identity

| Property | Value |
|---|---|
| Type | WinUI 3 desktop file viewer (packaged MSIX default; unpackaged EXE supported) |
| TFM | `net8.0-windows10.0.19041.0`, x64 only, `PACKAGED` define, custom `Main` (PerMonitorV2 DPI) |
| Native | 3 Rust `cdylib` crates: `deckcraft_ffi` (PPTX), `wordcraft_ffi` (DOCX, **unwired**), `gridcraft_ffi` (XLSX) |
| PDF | Hand-written P/Invoke over `pdfium.dll` (`Rendering/Pdf/Pdfium.cs`); `PDFiumCore` NuGet used **only** to carry the DLL |
| Key packages | WindowsAppSDK 2.4.0 · Win2D 1.4.0 · CommunityToolkit.Mvvm 8.4.2 · EF Core Sqlite 8.0.11 · Markdig 1.4.0 (**unused**, FIND-014) · PDFiumCore 97.0.4690 |
| Size | ~16.4k LOC C# / 113 files · ~2.4k XAML · ~1.4k Rust |

## 2. Folder responsibilities (actual)

```
Program.cs            entry: DPI awareness → single-instance redirect → Application.Start
App.xaml.cs           renderer registration, activation args → OpenFilePath → TitleBarWindow
UI/Shell              TitleBarWindow — tab shell, per-tab ViewerPage, tab tear-out disabled
Features/Home         HomePage + HomeViewModel (scan/filter/sort), file index UI
Features/Viewer       ViewerPage + 8 partials (Pdf/Viewport/Text/Search/Sidebar/Zoom/Toolbar/Helpers)
  └ Spreadsheet/      SpreadsheetViewer — Win2D canvas grid control (XLSX)
Features/Settings     SettingsPage
Rendering/Pdf         Pdfium P/Invoke, PdfDocumentState, PdfRenderer, Text/Link/Annot/Outline/Metadata services
Rendering/Document    GridCraft+WordCraft native wrappers, SheetData (JSON), SpreadsheetDocument, DocxDocument(dead)
Rendering/Presentation DeckCraft wrapper, PptxDocument, PptxViewerControl
Rendering/{Code,Text,Office,Unsupported}  code highlighter, capped text reader, registry info-card renderers
Services/Documents    IPdfViewerService + PdfViewerService (per-tab PDF lifecycle)
Services/FileSystem   FolderScanner (validated), FileWatcherService, EF Core SQLite index
Services/{Thumbnails,Storage,Registration,Updates}  thumbnail cache+providers, bookmarks/recents/settings, HKCU assoc., updater
Infrastructure        Logger (async queue), AppSettings, ServiceContainer (DI — unused)
Core / Common         models, interfaces, validators (Path/FileSize), formatters, detectors
QuickView.Tests       xUnit + FluentAssertions, 148 tests, references main project, uses real pdfium.dll
native/               3 Rust crates + build-native.ps1 (cargo build → copy to 4 bin layouts)
```

## 3. Primary execution paths (file:line)

**Launch:** `Program.Main` (DPI first, `Program.cs:70-105`) → `App.OnLaunched` (`App.xaml.cs:73-105`) → `RegisterRenderers` (126-144: Office info-card renderers into static `RendererRegistry`) → `TitleBarWindow` → activation/command-line file arg → `OpenFilePath` (`App.xaml.cs:223-251`, validates via `FileSizeValidator`) → `TitleBarWindow.OpenDocument` (`UI/Shell/TitleBarWindow.xaml.cs:96-149`, per-tab `ViewerPage`).

**Document dispatch:** `ViewerPage.LoadDocumentAsync` (`Partials/ViewerPage.Pdf.cs:32-91`): `.pdf`→`PdfViewerService.LoadDocumentAsync`→`PdfRenderer.OpenDocumentAsync` (Gate-locked FPDF open, page-size pre-cache) · `.pptx/.ppt/...`→`LoadPptxAsync` (own `PptxDocument`, slide viewport) · `.xlsx/...`→`LoadXlsxAsync` (own `SpreadsheetDocument` → `SpreadsheetViewer` UserControl) · code/text→Text paths · else→`RendererRegistry`→info card. **Note `.docx` lands in the registry→info-card branch (FIND-011).**

**PDF render:** scroll/zoom → `RenderPagesAsync` (`Partials/ViewerPage.Viewport.cs:225-346`, UI-thread only, cancels prior `_renderCts`, DPI = min(192, 96·scale·min(zoom,2))) → evict cache beyond visible±2 → visible pages sequentially, buffer pages via semaphore'd `Task.Run` → `RenderFullPage` (536-603, all FPDF calls under `Pdfium.Gate`, ArrayPool buffer, ≤2600 px) → `CreateBitmapFromBufferAsync` (UI-thread WriteableBitmap) → Image inserted into `PdfViewportCanvas`, cached in `_pdfPageCache`.

**Tab close:** `TitleBarWindow.CloseTab` (256-275) → `ViewerPage.CancelLoad` (`ViewerPage.xaml.cs:101-128`) — cancels CTSs, cleans renderer element, nulls `_pdfState` — **but frees nothing native (FIND-001)**.

## 4. Resource ownership map

| Resource | Created | Released | Gap |
|---|---|---|---|
| FPDF document | `PdfRenderer.OpenDocumentAsync:63` (service-owned) | `PdfViewerService.CloseDocument:113` — called only on next same-service load | **Never on tab close** (FIND-001) |
| Pinned byte[] (mem-loaded PDFs) | `PdfDocumentState.ctor:46` | `PdfDocumentState.Dispose:83` | same gap |
| `Box<GcDocument>` (XLSX) | `gc_open_xlsx` via `SpreadsheetDocument.OpenAsync` | `Dispose` → `gc_free_document` — only on next XLSX load | **Tab close leak** (FIND-001) |
| `Box<DcDocument>` (PPTX) | `dc_open_pptx` via `PptxDocument.OpenAsync` | same pattern | **Tab close leak** (FIND-001) |
| FPDF page/bitmap | `RenderFullPage` / `RenderPageAsync` | `finally` blocks — verified complete | none |
| Rust pixel/JSON buffers | `wc_render_page`/`gc_get_sheet_data_json` etc. | C# `finally` → `*_free_*` — verified paired | Dispose race (FIND-002) |
| Page Images (`_pdfPageCache`) | `RenderSinglePageAsync` | eviction in `RenderPagesAsync:275-289` | minor race (FIND-004) |
| Thumbnail disk cache | `ThumbnailCache` | 500 MB LRU eviction (verified) | LastAccessTime reliance (FIND-030) |

## 5. Threading model (verified)

- UI thread: WinUI `DispatcherQueue` + `DispatcherQueueSynchronizationContext` installed in `Program.cs:98-104`; all canvas/cache mutations marshaled (verified in Viewport.cs).
- PDFium: single process-wide `Pdfium.Gate`; every FPDF call site audited is inside it (one stale comment claims otherwise — FIND under PDF-003/findings register).
- Rust FFI: **no lock** — wrappers rely on C# `SemaphoreSlim` per document; fine per-document, but Dispose races (FIND-002).
- Logger: dedicated background thread, bounded queue.

## 6. Contradictions between docs and code (top)

| Old doc claim | Reality at `40cdfcd` |
|---|---|
| "0 unit tests" | 148 passing |
| "No dependency injection" | Container exists, never configured (FIND-013) |
| "WebView2 renders HTML/MD; SEC-01/02 critical" | WebView2 fully removed; renderers deleted |
| "ViewerPage ~1,500 lines / 8 partials" | ~3.4k lines / 9 files incl. Viewport (774) |
| Roadmap "Phase 5: replace Markdown parser with Markdig" | Markdig referenced but unused; no HTML/MD rendering at all |
| Tree.md structure | Missing Spreadsheet/, Rendering/Document|Presentation, 2 native crates, several models; lists deleted `.agents/`/`.kiro/` |
