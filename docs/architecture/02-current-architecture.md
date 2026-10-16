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

**Registry path (working, not yet fully generic):** `DocumentViewerRegistry` maps extension → `IDocumentViewerFactory` (case-insensitive; last-registered wins on conflict; unknown → fallback factory). Registered factories include `PdfViewerFactory` (`.pdf`), `DocxViewerFactory` (`.docx/.doc`), `PptxViewerFactory` (`.pptx/.ppt/.pptm/.ppsx`), `TextViewerFactory` (75 text/code extensions), `SpreadsheetViewerFactory` (.xlsx/.xlsm/.xltx/.xltm), and `FallbackViewerFactory` (.xls + anything unclaimed). `ViewerPage` resolves the extension through the registry and still uses type-specific load orchestration. TextViewer, SpreadsheetViewer, PdfViewer, DocxViewer, and PptxViewer are constructed eagerly in `ViewerPage`; FallbackViewer is created per load and hosted inside the `UnsupportedFileMessage` content control. Contracts implemented so far: PdfViewer → `IDocumentViewer, ISupportsZoom, ISupportsSearch, ISupportsThumbnails, ISupportsOutline`; DocxViewer → `IDocumentViewer, ISupportsSearch, ISupportsThumbnails, ISupportsOutline`; PptxViewer → `IDocumentViewer, ISupportsZoom, ISupportsSearch, ISupportsThumbnails, ISupportsOutline`; TextViewer → `IDocumentViewer, ISupportsSearch, ISupportsTextSelection`; SpreadsheetViewer → `IDocumentViewer, ISupportsZoom`.

**Remaining shell orchestration:** `ViewerPage.LoadDocumentCoreAsync` resolves the extension through the registry and branches by factory type to run PDF, DOCX, PPTX, XLSX, text, or fallback loading. Format-specific lifecycle/loading is therefore still in the shell, but PPTX extensions no longer appear in dispatch logic.

**Incremental changes after this audit:** the PDF `ScrollViewer`, viewport canvas, overlay canvases, progressive page-render loop, render cache, page-layout calculations, annotation and text-selection overlay rendering, PDF text-map cache and selection geometry/text extraction, thumbnail model/prefetch, outline and saved-annotation data loading/navigation, and fit-width/fit-page calculations are owned by `Formats/Pdf/PdfViewer`. `PdfViewer` implements `IDocumentViewer`, `ISupportsZoom`, `ISupportsSearch`, `ISupportsThumbnails`, and `ISupportsOutline`; it is created through a registered `PdfViewerFactory` and owns the `IPdfViewerService` instance and PDF document-state reference. It performs native document loading, password prompting, service cleanup, PDF search/highlight/navigation, and applies zoom changes to its viewport. Text-map cache generations are invalidated when the PDF document changes; thumbnail prefetch is cancelled on a newer request, document load, or cleanup. Fit is reapplied after PDF viewport initialization and debounced layout changes, including sidebar collapse/expand. Annotation data, overlay rendering, active annotation tool, and markup settings are viewer-owned, with an event notifying the shell to refresh the notes list. `ViewerPage` still coordinates PDF pointer/touch selection, annotation authoring dialogs and persistence/history, sidebar presentation, and load lifecycle orchestration. Generic registry loading and the full format ownership cutover are incomplete.

DOCX rendering and search/outline capabilities have moved behind a dedicated `Formats/Docx/DocxViewer`: it owns its scroll/canvas surface, native `DocxDocument` lifetime, page geometry, viewport render cache, debounced rerendering, thumbnails, search/highlighting, and outline extraction/navigation. `DocxViewerFactory` is registered for `.docx` and `.doc`; the shell selects it through the registry and hosts a distinct `DocxViewerHost`. Selection/copy interactions and fit-mode decisions remain shell-side adapters to the viewer's document, canvas, and page geometry. This is an initial cutover, not completion of Phase 2.

PPTX rendering/search/outline now live in `Formats/Pptx/PptxViewer`: it owns the presentation lifetime, slide geometry, virtualized viewport/cache, thumbnails, search/highlights, outline, navigation, and zoom/fit calculations. `PptxViewerFactory` is registered for `.pptx/.ppt/.pptm/.ppsx`, and a distinct `PptxViewerHost` contains its scroll/canvas surface. Selection/copy and presentation-mode orchestration remain in the shell and use viewer-exposed document/canvas geometry. Multi-line copy behavior remains intentionally deferred.

- `ViewerPage.Pdf.cs`: dispatch + PDF/DOCX/PPTX load orchestration + XLSX glue. The duplicate PPTX slide rendering/cache/search/outline implementation has been removed.
- `ViewerPage.PdfTextSelection.cs` (580): pointer/touch interaction and copy UI; text-map caching, range state, PDF text extraction/geometry, and selection overlay rendering have moved to PdfViewer. `ViewerPage.PdfAnnotations.cs` (698): markup authoring dialogs, history, and deferred flush; active tool and markup settings are held by PdfViewer.
- `Formats/Pdf/PdfViewer.xaml.cs` owns PDF document/service state, search, selection/annotation drawing, text-map cache, thumbnail and outline capabilities, and fit calculations; `PdfViewer.Viewport.cs` owns progressive rendering, the `RenderBudget` cache, and PDF canvas/page-offset sizing. `ViewerPage` still owns the shared resize debounce, which also serves SpreadsheetViewer.
- `Formats/Docx/DocxViewer.xaml(.cs)` owns DOCX document/rendering lifecycle, thumbnails, search/highlights, and outline; Office selection/copy and fit-mode decisions remain shell-side.
- `ViewerPage.OfficeTextSelection.cs` (516): DOCX/PPTX drag-select/copy remains a shell adapter. `ViewerPage.Search.cs` uses `ISupportsSearch` for registered viewers. `ViewerPage.Sidebar.cs` retains shared sidebar presentation and PDF bookmarks/notes; PPTX outline extraction is viewer-owned. `ViewerPage.Zoom.cs` retains shell fit-mode orchestration and delegates PPTX calculations to its viewer. `ViewerPage.Presentation.cs` (240): PPTX presentation mode + global shortcut scaffolding.

**State ownership (interim):** `ViewerViewModel` (MVVM Toolkit) exposes Document/CurrentPage/TotalPages/Zoom/NaturalZoom/Rotation/SidebarOpen/IsLoading/ErrorMessage + commands; `ViewerPage` keeps `_page/_zoom/_total/_rotation` mirrors for PDF math and pushes them via `SyncToViewModel()` (8 call sites). This is the documented interim, not a bug.

**Lifecycle:** `TitleBarWindow.OpenDocument` → `ViewerPage.LoadDocument`/`OnNavigatedTo` → `LoadDocumentAsync` (flushes pending PDF annotations first — deferred-save contract; logs via `Logger.BeginOperation("OpenDocument")`; error card on failure). Tab close → `ViewerPage.CancelLoad`: cancels load+search CTSes, `CleanupViewportRendering`, calls `CleanupAsync()` on every live viewer control, disposes pptx/docx documents, disposes + recreates `PdfViewerService` (owns the native handle), `ViewModel.Reset()`.

## 3. Rendering and interop contracts (as enforced today)

- WinUI *media* primitives (`WriteableBitmap`) are created on the UI dispatcher via `NativeBitmap.Initialize` at startup; engines never touch XAML controls. Viewer controls (Formats/) may use XAML freely — they are `Features` citizens.
- Rust allocates; C# copies and frees via matching `*_free_*` exports exactly once (table in `Interop/README.md`).
- Exceptions escaping fire-and-forget loads are caught at the `LoadDocumentAsync` boundary → log + error card; global handlers log FATAL and deliberately do not mark XAML exceptions handled.

## 4. What changed in `10db6a8` (context)

Interop consolidation + `LibraryImport` migration; `Rendering/` reorganized by format (PDF models out of Core); `App`/`TitleBarWindow` extractions (Activation/Session/QuickLookHotkey services); viewer contracts + registry + Text/Spreadsheet/Fallback viewers; deletion of the legacy renderer pipeline (`RendererRegistry`, `IFileRenderer`, `Rendering/Office/*`, `ViewerInfoCard`); PDF markup toolbar extraction; stale csproj excludes removed; docs deleted (user decision); README + LICENSE added. Verified absent: any `DllImport` for engine bindings.

## 5. Known gaps (feed the roadmap)

Factory type-matching and per-format load orchestration in dispatch (RA-06) · VM mirrors (RA-05) · ViewerToolbar imperative setters · 8 zero-consumer files awaiting deletion · no architecture enforcement tests · dangling `docs/reliability` comment references (RA-09). The dormant PptxViewer finding (RA-04) is resolved by the Phase 2 integration; remaining work is tracked in doc 10.
