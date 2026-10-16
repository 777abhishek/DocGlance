# 02 — Current Architecture (verified 2026-10-16, commit `10db6a8`)

DocGlance is a WinUI 3 / .NET 8 packaged (MSIX) desktop document reader with Rust rendering engines behind a C ABI. One app project, one test project. This describes what **is**, mid-refactor — the hybrid state left by `10db6a8`.

## 1. Layer map (as built)

```
UI/Shell (TitleBarWindow, tabs)  ─┐
Features/{Home,Tools,Settings}    ├─→ Features/Viewer (ViewerPage + formats) ─→ Rendering/* ─→ Interop/* ─→ Rust
Services/* (app services)        ─┘                    └→ Contracts (IDocumentViewer, ISupports*)
Infrastructure/* (logging, config, diagnostics, NativeCallGate)
Common/* + Core/* (pure helpers, models, enums)
```

- **Startup/activation:** `Program.cs` (DPI awareness, single-instance redirect) → `App.xaml.cs` (208 LOC: exception policy, `OnLaunched` wiring) → `Services/Activation/{ActivationService,ActivationIpc,ActivationPaths}` (activation args, command line, named-pipe handover) → `Services/Session/SessionService` (restore) → `TitleBarWindow` (tabs; `SaveSession`/`RestoreSession` are 2-line call-throughs). Hotkeys: `Services/Input/QuickLookHotkeyService` + `GlobalHotkeyRegistration`, owned by TitleBarWindow via call-throughs.
- **FFI boundary:** `Interop/{PdfCraft,WordCraft,GridCraft,DeckCraft,Translation}/*Native.cs`, all `[LibraryImport]`, one folder per engine, ABI table in `Interop/README.md`. Document handles/disposal serialized by per-engine `NativeCallGate`. `Services/Translation` is the only non-Rendering consumer (sanctioned exception).
- **Engines:** `Rendering/{Pdf,Docx,Xlsx,Pptx}` wrap the bindings (document state, caches, budgets); `Rendering/Shared` (NativeBitmap, RenderBudget, TextFileReader). `ToolsPage` consumes engines directly (sanctioned).
- **Thumbnails/files:** `Services/Thumbnails` (4 `IThumbnailProvider` implementations + disk cache), `Services/FileSystem/{Index,Scanner}`, stores in `Services/Storage`.

## 2. The viewer: hybrid dispatch (the one non-final area)

**Registry path (final mechanism, working):** `DocumentViewerRegistry` maps extension → `IDocumentViewerFactory` (case-insensitive; last-registered wins on conflict; unknown → fallback factory). Registered today: `TextViewerFactory` (75 text/code extensions), `SpreadsheetViewerFactory` (.xlsx/.xlsm/.xltx/.xltm), `FallbackViewerFactory` (.xls + anything unclaimed). TextViewer + SpreadsheetViewer are constructed eagerly in the `ViewerPage` constructor; FallbackViewer is created per load and hosted inside the `UnsupportedFileMessage` content control. Contracts implemented so far: TextViewer → `IDocumentViewer, ISupportsSearch, ISupportsTextSelection`; SpreadsheetViewer → `+ ISupportsZoom`; PptxViewer → `IDocumentViewer` **but is constructed nowhere** (dormant — RA-04).

**Legacy path (shell, still load-bearing):** `ViewerPage.LoadDocumentCoreAsync` (in `Partials/ViewerPage.Pdf.cs`) handles `.pdf`, `.docx/.doc`, `.pptx/.ppt/.pptm/.ppsx` with format literals; for everything else it resolves the registry **and then pattern-matches the factory type** (`is SpreadsheetViewerFactory` → `LoadXlsxAsync`, `is TextViewerFactory` → `LoadTextFileAsync`, else → `ShowFallbackViewerAsync`). PDF rendering/selection/annotation still live in shell partials:

**Incremental change after this audit:** the PDF `ScrollViewer`, viewport canvas, and overlay canvases are hosted by `Formats/Pdf/PdfViewer.xaml`. `ViewerPage` still owns and drives their loading, rendering, selection, annotation, and lifecycle logic; the extraction is not yet a viewer-contract cutover.

- `ViewerPage.Pdf.cs` (1,365): dispatch + PDF load/password/thumbnail-prefetch + PPTX block (slide cache, viewport loop, search highlights — parallel to dormant PptxViewer) + XLSX glue + DOCX block (page caches, viewport loop, thumbnails).
- `ViewerPage.PdfTextSelection.cs` (730): selection rects/hit-test/copy. `ViewerPage.PdfAnnotations.cs` (859): markup tools/history/deferred flush (drives `PdfMarkupToolbar` events via `ViewerPage.Toolbar.cs`).
- `ViewerPage.Viewport.cs` (642): PDF progressive render loop, page cache with `RenderBudget`, resize debounce (shared shell debounce also serves SpreadsheetViewer).
- `ViewerPage.OfficeTextSelection.cs` (516): docx+pptx drag-select/copy. `ViewerPage.Search.cs` (534): find bar + both the new `ISupportsSearch` path **and** legacy per-format highlight/scroll. `ViewerPage.Sidebar.cs` (504): thumbnails/outline/bookmarks/notes; docx+pptx outline loaders still here. `ViewerPage.Zoom.cs` (302): fit math. `ViewerPage.Presentation.cs` (240): pptx presentation mode + global shortcut scaffolding. `ViewerPage.Helpers.cs` (22): pending merge.

**State ownership (interim):** `ViewerViewModel` (MVVM Toolkit) exposes Document/CurrentPage/TotalPages/Zoom/NaturalZoom/Rotation/SidebarOpen/IsLoading/ErrorMessage + commands; `ViewerPage` keeps `_page/_zoom/_total/_rotation` mirrors for PDF math and pushes them via `SyncToViewModel()` (8 call sites). This is the documented interim, not a bug.

**Lifecycle:** `TitleBarWindow.OpenDocument` → `ViewerPage.LoadDocument`/`OnNavigatedTo` → `LoadDocumentAsync` (flushes pending PDF annotations first — deferred-save contract; logs via `Logger.BeginOperation("OpenDocument")`; error card on failure). Tab close → `ViewerPage.CancelLoad`: cancels load+search CTSes, `CleanupViewportRendering`, calls `CleanupAsync()` on every live viewer control, disposes pptx/docx documents, disposes + recreates `PdfViewerService` (owns the native handle), `ViewModel.Reset()`.

## 3. Rendering and interop contracts (as enforced today)

- WinUI *media* primitives (`WriteableBitmap`) are created on the UI dispatcher via `NativeBitmap.Initialize` at startup; engines never touch XAML controls. Viewer controls (Formats/) may use XAML freely — they are `Features` citizens.
- Rust allocates; C# copies and frees via matching `*_free_*` exports exactly once (table in `Interop/README.md`).
- Exceptions escaping fire-and-forget loads are caught at the `LoadDocumentAsync` boundary → log + error card; global handlers log FATAL and deliberately do not mark XAML exceptions handled.

## 4. What changed in `10db6a8` (context)

Interop consolidation + `LibraryImport` migration; `Rendering/` reorganized by format (PDF models out of Core); `App`/`TitleBarWindow` extractions (Activation/Session/QuickLookHotkey services); viewer contracts + registry + Text/Spreadsheet/Fallback viewers; deletion of the legacy renderer pipeline (`RendererRegistry`, `IFileRenderer`, `Rendering/Office/*`, `ViewerInfoCard`); PDF markup toolbar extraction; stale csproj excludes removed; docs deleted (user decision); README + LICENSE added. Verified absent: any `DllImport` for engine bindings.

## 5. Known gaps (feed the roadmap)

Format literals + factory type-matching in dispatch (RA-06) · dormant PptxViewer (RA-04) · VM mirrors (RA-05) · ViewerToolbar imperative setters · 8 zero-consumer files awaiting deletion · no architecture enforcement tests · dangling `docs/reliability` comment references (RA-09). All mapped to Phases 1–5 in doc 10.
