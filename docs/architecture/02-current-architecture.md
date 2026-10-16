# 02 — Current Architecture (verified 2026-10-16, after `a22ebd9`)

DocGlance is a WinUI 3 / .NET 8 packaged (MSIX) desktop document reader with Rust rendering engines behind a C ABI. One app project, one test project. This describes the current mid-refactor state after the PDF capability extraction commit and follow-up changes.

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

**Registry path (final mechanism, working):** `DocumentViewerRegistry` maps extension → `IDocumentViewerFactory` (case-insensitive; last-registered wins on conflict; unknown → fallback factory). Registered factories now include `PdfViewerFactory` (`.pdf`), `TextViewerFactory` (75 text/code extensions), `SpreadsheetViewerFactory` (.xlsx/.xlsm/.xltx/.xltm), and `FallbackViewerFactory` (.xls + anything unclaimed). `ViewerPage` now resolves the extension through the registry to select the PDF factory, but PDF load orchestration and DOCX/PPTX branches remain shell-owned; type-matched registry dispatch for text/spreadsheets/fallback remains legacy. TextViewer + SpreadsheetViewer are constructed eagerly in the `ViewerPage` constructor; PdfViewer is constructed through its factory; FallbackViewer is created per load and hosted inside the `UnsupportedFileMessage` content control. Contracts implemented so far: PdfViewer → `IDocumentViewer, ISupportsZoom, ISupportsSearch, ISupportsThumbnails, ISupportsOutline` (incremental extraction); TextViewer → `IDocumentViewer, ISupportsSearch, ISupportsTextSelection`; SpreadsheetViewer → `+ ISupportsZoom`; PptxViewer → `IDocumentViewer` **but is constructed nowhere** (dormant — RA-04).

**Legacy path (shell, still load-bearing):** `ViewerPage.LoadDocumentCoreAsync` (in `Partials/ViewerPage.Pdf.cs`) handles `.pdf`, `.docx/.doc`, `.pptx/.ppt/.pptm/.ppsx` with format literals; for everything else it resolves the registry **and then pattern-matches the factory type** (`is SpreadsheetViewerFactory` → `LoadXlsxAsync`, `is TextViewerFactory` → `LoadTextFileAsync`, else → `ShowFallbackViewerAsync`). At the original audit snapshot, PDF rendering/selection/annotation still lived in shell partials; the changes below record the incremental extraction since then.

**Incremental changes after this audit:** the PDF `ScrollViewer`, viewport canvas, overlay canvases, progressive page-render loop, render cache, page-layout calculations, annotation and text-selection overlay rendering, PDF text-map cache and selection geometry/text extraction, thumbnail model/prefetch, outline and saved-annotation data loading/navigation, and fit-width/fit-page calculations are owned by `Formats/Pdf/PdfViewer`. `PdfViewer` implements `IDocumentViewer`, `ISupportsZoom`, `ISupportsSearch`, `ISupportsThumbnails`, and `ISupportsOutline`; it is created through a registered `PdfViewerFactory` and owns the `IPdfViewerService` instance and PDF document-state reference. It performs native document loading, password prompting, service cleanup, PDF search/highlight/navigation, and applies zoom changes to its viewport. Text-map cache generations are invalidated when the PDF document changes; thumbnail prefetch is cancelled on a newer request, document load, or cleanup. Fit is reapplied after PDF viewport initialization and debounced layout changes, including sidebar collapse/expand. Annotation data, overlay rendering, active annotation tool, and markup settings are viewer-owned, with an event notifying the shell to refresh the notes list. `ViewerPage` still coordinates PDF pointer/touch selection, annotation authoring dialogs and persistence/history, sidebar presentation, and load lifecycle orchestration. Generic registry loading and the full format ownership cutover are incomplete.

`PdfViewerHost` explicitly stretches its hosted viewer in both dimensions. The PDF, DOCX, and PPTX shell renderers share this hosted `ScrollViewer`/canvas, so this constraint is necessary for all three fit modes to use the full viewer column rather than the canvas's intrinsic width.

- `ViewerPage.Pdf.cs` (1,130): dispatch + PDF load presentation orchestration + PPTX block (slide cache, viewport loop, search highlights — parallel to dormant PptxViewer) + XLSX glue + DOCX block (page caches, viewport loop, thumbnails).
- `ViewerPage.PdfTextSelection.cs` (580): pointer/touch interaction and copy UI; text-map caching, range state, PDF text extraction/geometry, and selection overlay rendering have moved to PdfViewer. `ViewerPage.PdfAnnotations.cs` (698): markup authoring dialogs, history, and deferred flush; active tool and markup settings are held by PdfViewer.
- `Formats/Pdf/PdfViewer.xaml.cs` owns PDF document/service state, search, selection/annotation drawing, text-map cache, thumbnail and outline capabilities, and fit calculations; `PdfViewer.Viewport.cs` owns progressive rendering, the `RenderBudget` cache, and PDF canvas/page-offset sizing. `ViewerPage` still owns the shared resize debounce, which also serves SpreadsheetViewer.
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
