# 09 — File Migration Map (re-audit 2026-10-16: current state → final tree)

**Basis:** commit `10db6a8` (post-refactor) working tree, verified 2026-10-16. Every current maintained file appears exactly once. Since Phases 1–4 and half of Phase 5 already shipped, most rows are `[FINAL]` (already at final path — no action). The remaining delta is concentrated in `Features/Viewer`.

Columns: Current path · Action · Final path (when different) · Reason / consumers to update · Confidence.

**Legend:** FINAL = no action · EDIT = in-place scope work (see tree §2) · SPLIT = survives, parts move out · DISSOLVE = file removed, responsibilities move · MERGE = absorbed · MOVE = relocates · DELETE = removed (ledger evidence in tree §3) · NEW = created.

Namespace changes in the remaining delta: moved responsibilities carry `DocGlance.Features.Viewer` → `DocGlance.Features.Viewer.Formats.{Pdf,Docx,Pptx}`; no namespace changes for FINAL files. XAML URI changes: `UpdateBanner` in `UI/Shell/TitleBarWindow.xaml` (with the MOVE). Project/build implications: none (SDK-style globbing; single csproj pair unchanged).

## Root (11 files)

| Current | Action | Final | Notes |
|---|---|---|---|
| `DocGlance.csproj` | FINAL | — | stale `Assets\windows|SampleMedia` excludes already removed (supersedes AF-12) |
| `Program.cs` | FINAL | — | 232 LOC; Win32 P/Invoke sanctioned |
| `App.xaml` / `App.xaml.cs` | FINAL | — | 208 LOC; activation/session extracted |
| `Package.appxmanifest`, `app.manifest`, `.gitignore`, `build.ps1`, `build-installer.ps1` | FINAL | — | — |
| `README.md`, `LICENSE` | FINAL | — | added in `10db6a8` |

## Common (5) — all FINAL
`DateFormatter.cs` · `FileSizeFormatter.cs` · `FileTypeDetector.cs` · `FileSizeValidator.cs` · `PathValidator.cs`

## Core (11 → 10)

| Current | Action | Final | Notes |
|---|---|---|---|
| `Core/Constants/FileExtensions.cs`, `SupportedExtensions.cs` | FINAL | — | — |
| `Core/Enums/FileCategory.cs`, `ViewMode.cs`, `FitMode.cs` | FINAL | — | FitMode extracted (done) |
| `Core/Interfaces/IThumbnailProvider.cs` | FINAL | — | 4 implementations intact |
| `Core/Interfaces/IFileScanner.cs` | **DELETE** | — | zero consumers (grep 2026-10-16; AF-07) — Phase 4 |
| `Core/Models/DocumentItem.cs`, `DocumentGroup.cs`, `OutlineItem.cs`, `BookmarkItem.cs`, `PageThumbnailItem.cs` | FINAL | — | PDF models already moved to `Rendering/Pdf/Models` |

## Infrastructure (8) — all FINAL
`NativeCallGate.cs` · `AppSettings.cs` · `DiagnosticsService.cs` · `UIThreadHelper.cs` (moved from Core, done) · `Logger.cs` · `LogFileWriter.cs` · `LogScope.cs` · `TaskExtensions.cs`

## Interop (6) — all FINAL (Phase 1 complete in `10db6a8`)
`PdfCraft/PdfCraftNative.cs` (LibraryImport ×25 done) · `WordCraft/WordCraftNative.cs` · `GridCraft/GridCraftNative.cs` · `DeckCraft/DeckCraftNative.cs` · `Translation/TranslationNative.cs` (renamed done) · `README.md`

## Rendering (21 → 20)

| Current | Action | Final | Notes |
|---|---|---|---|
| `Rendering/Pdf/*` (8) + `Models/*` (6) | FINAL | — | IPdfViewerService/PdfViewerService/PdfLoadResult moved from Services/Documents (done); consumers updated: ViewerPage, tests |
| `Rendering/Docx/DocxDocument.cs` | FINAL | — | consumers: ToolsPage, ViewerPage.Pdf.cs (until Phase 2) |
| `Rendering/Xlsx/SpreadsheetDocument.cs`, `SheetData.cs` | FINAL | — | consumers: SpreadsheetViewer, ToolsPage |
| `Rendering/Pptx/PptxDocument.cs` | FINAL | — | consumers: PptxViewer (owns one), ViewerPage.Pdf.cs (until Phase 2) |
| `Rendering/Shared/NativeBitmap.cs`, `RenderBudget.cs`, `TextFileReader.cs` | FINAL | — | — |
| `Rendering/Code/CodeRenderer.cs` | **DELETE** | — | zero consumers (grep) — Phase 4 |

## Features/Home, Tools, Settings (7) — all FINAL
`HomePage.xaml(.cs)`, `HomeViewModel.cs` · `ToolsPage.xaml(.cs)` · `SettingsPage.xaml(.cs)`. ToolsPage consumes `Rendering.{Docx,Pptx,Xlsx,Pdf}` engines directly — sanctioned (no renderer indirection).

## Features/Viewer — the remaining delta

| Current | Action | Final path | Responsibilities moving | Consumers to update | Confidence |
|---|---|---|---|---|---|
| `ViewerPage.xaml` | EDIT | same | host layout: per-viewer ContentControls; PDF canvas folds into PdfViewer | — | high |
| `ViewerPage.xaml.cs` | SPLIT | same | absorbs dispatch (`LoadDocumentAsync`/`CoreAsync` from Pdf.cs 48–157) + `Helpers.cs`; drops state mirrors when VM takes ownership | TitleBarWindow (public API unchanged) | high |
| `ViewerViewModel.cs` | EDIT | same | sole state owner; delete `_page/_zoom/_total/_rotation` mirrors + `SyncToViewModel()` (8 call sites) | all Partials (compiler-driven) | high |
| `DocumentViewerRegistry.cs` | FINAL | — | — | — | — |
| `Contracts/*.cs` (8 files) | FINAL | — | — | — | — |
| `Components/ViewerToolbar.xaml(.cs)` | EDIT | same | imperative proxies → ViewModel bindings (Phase 3) | Partials | high |
| `Components/ViewerToolbarIcons.xaml` | FINAL | — | new in `10db6a8`, adopted | — | — |
| `Components/PdfMarkupToolbar.xaml(.cs)` | FINAL | — | new in `10db6a8`, adopted | — | — |
| `Components/SearchHighlighter.cs`, `DocumentInfoDialog.cs`, `TranslationDialog.cs`, `TranslationModelsDialog.cs`, `ViewerPopupStyle.cs` | FINAL | — | — | — | — |
| `Partials/ViewerPage.Pdf.cs` | **DISSOLVE** | (removed) | 48–157 dispatch → `ViewerPage.xaml.cs`; 158–437 PDF → `Formats/Pdf/PdfViewer.xaml.cs`; 438–919 PPTX → `Formats/Pptx/PptxViewer.xaml.cs`; 920–984 XLSX glue → generic registry path in `ViewerPage.xaml.cs`; 985–1365 DOCX → `Formats/Docx/DocxViewer.xaml.cs`; `ShowErrorCard`/`ShowFallbackViewerAsync` → shell | whole-file; Phases 1–2 | high |
| `Partials/ViewerPage.PdfTextSelection.cs` | **DISSOLVE** | (removed) | → `Formats/Pdf/PdfSelectionLayer.cs` | Phase 1 | high |
| `Partials/ViewerPage.PdfAnnotations.cs` | **DISSOLVE** | (removed) | → `Formats/Pdf/PdfAnnotationLayer.cs` | Phase 1 | high |
| `Partials/ViewerPage.OfficeTextSelection.cs` | **DISSOLVE** | (removed) | docx parts → `DocxViewer`; pptx parts → `PptxViewer` | Phase 2 | high |
| `Partials/ViewerPage.Presentation.cs` | **DISSOLVE** | (removed) | presentation parts → `PptxViewer`; `InitializeViewerShortcuts`/`AddViewerShortcut`/handlers → `ViewerPage.KeyboardShortcuts.cs` | Phase 2 | high |
| `Partials/ViewerPage.Viewport.cs` | SPLIT | same | PDF render loop + cache/budget → `PdfViewer`; sizing/debounce stay | Phase 1 | high |
| `Partials/ViewerPage.Sidebar.cs` | SPLIT | same | `LoadDocx/PptxOutlineAsync` → viewers via `ISupportsOutline` | Phase 2 | high |
| `Partials/ViewerPage.Search.cs` | SPLIT | same | per-format highlight/scroll → viewers via `ISupportsSearch` | Phase 1–2 | high |
| `Partials/ViewerPage.Zoom.cs` | SPLIT | same | `ApplyFitWidth`/`ApplyFitPage` → `PdfViewer` via `ISupportsZoom` | Phase 1 | high |
| `Partials/ViewerPage.Toolbar.cs`, `KeyboardShortcuts.cs`, `Translator.cs` | FINAL | — | — | — | — |
| `Partials/ViewerPage.Helpers.cs` | **MERGE** | (removed) | → `ViewerPage.xaml.cs` | Phase 3 | high |
| `Formats/Text/*` (2) | FINAL | — | TextViewerFactory claims 75 text/code extensions | — | — |
| `Formats/Spreadsheet/*` (2) | FINAL | — | factory claims .xlsx/.xlsm/.xltx/.xltm | — | — |
| `Formats/Pptx/PptxViewer.xaml` | FINAL | — | — | — | — |
| `Formats/Pptx/PptxViewer.xaml.cs` | EDIT | same | **currently unwired** (RA-04); Phase 2 wires + absorbs PPTX block | ViewerPage.xaml.cs | high |
| `Formats/Fallback/FallbackViewer.cs` | FINAL | — | claims .xls; pinned by registry test | — | — |
| `Formats/Pdf/PdfViewer.xaml` + `.cs` | **NEW** | — | from Pdf.cs PDF block + Viewport.cs PDF loop + Zoom.cs math | — | — |
| `Formats/Pdf/PdfSelectionLayer.cs` | **NEW** | — | ← PdfTextSelection.cs | — | — |
| `Formats/Pdf/PdfAnnotationLayer.cs` | **NEW** | — | ← PdfAnnotations.cs | — | — |
| `Formats/Docx/DocxViewer.xaml` + `.cs` | **NEW** | — | ← Pdf.cs DOCX block + OfficeTextSelection docx parts + Sidebar docx outline | — | — |

## Services (31 → 29 + 2 moved)

| Current | Action | Final | Notes |
|---|---|---|---|
| `Activation/{ActivationIpc,ActivationPaths,ActivationService}.cs` | FINAL | — | extracted from App.xaml.cs (done) |
| `FileSystem/Index/{FileIndexService,FileIndexContext,FileIndexRepository}.cs`, `Scanner/FolderScanner.cs` | FINAL | — | — |
| `FileSystem/Watcher/FileWatcherService.cs` | **DELETE** | — | zero consumers — Phase 4 |
| `Input/{GlobalHotkeyRegistration,QuickLookHotkeyService}.cs` | FINAL | — | Win32 P/Invoke sanctioned (rule updated, RA-07) |
| `Registration/{AppRegistration,RegistrationHelper}.cs` | FINAL | — | — |
| `Session/SessionService.cs` | FINAL | — | extracted (done); TitleBarWindow call-throughs verified |
| `Storage/*` (4) | FINAL | — | — |
| `Thumbnails/*` (5) | FINAL | — | — |
| `Translation/*` (7) | FINAL | — | sole sanctioned consumer of `Interop/Translation` |
| `Updates/UpdateService.cs` | FINAL | — | 600 LOC; Win32 P/Invoke sanctioned |
| `Updates/UpdateBanner.xaml(.cs)` | **MOVE** | `UI/Controls/UpdateBanner.xaml(.cs)` | UI out of Services (carried AF-08); update `TitleBarWindow.xaml` `update:` URI — Phase 3 |

## UI (11)

| Current | Action | Final | Notes |
|---|---|---|---|
| `Shell/TitleBarWindow.xaml(.cs)` | FINAL | — | revised final scope ~1,050 LOC (RA-15); UpdateBanner URI updates with the MOVE |
| `Controls/DocumentCard.xaml(.cs)` | FINAL | — | — |
| `Controls/PageThumbnailSidebar.xaml(.cs)` | **DELETE** | — | zero external consumers — Phase 4 |
| `Icons/FileTypeIcons.xaml`, `Templates/FileTemplates.xaml`, `Styles/{Colors,Typography,Controls}.xaml` | FINAL | — | — |

## Tests (17 files, 2,146 LOC)

| Current | Action | Notes |
|---|---|---|
| `Common/*` (4), `Core/Constants/SupportedExtensionsTests.cs`, `Features/Home/HomeViewModelTests.cs`, `Infrastructure/*` (2), `Rendering/Docx/DocxOutlineTests.cs`, `Rendering/Pdf/*` (3), `Services/Translation/*` (3) | FINAL | all relocated to mirror new layout in `10db6a8` |
| `Features/Viewer/ViewerViewModelTests.cs` | FINAL (extends in Phase 3) | 351 LOC; add sole-state-owner behaviors |
| `Features/Viewer/DocumentViewerRegistryTests.cs` | FINAL | pins `.xls`→FallbackViewer + fallback-for-unclaimed (AF-06 verified) |
| `Architecture/NamespaceDependencyTests.cs` | **NEW** | Phase 5 |

## Tools, native, installer, assets — all FINAL
`tools/EmbedIcon/*`, `tools/imggen/*` (excluded from app compilation) · `native/` 5 crates (Rust intact; one test-string rename in `translation_ffi` — RA-08) · `installer/` · `Assets/` (enumerated in tree §1) · `docs/architecture/` (this set) · `docs/offline-translation.md` deleted at user decision (recoverable from history).

## Disposition counts (consistent counting, one disposition per current file)

Current maintained app files: **150** (28,766 LOC). Current test files: **17** (2,146 LOC).

| Action | App files |
|---|---|
| FINAL | 125 |
| EDIT (in-place scope work) | 4 — ViewerPage.xaml, ViewerViewModel.cs, ViewerToolbar.xaml, ViewerToolbar.xaml.cs |
| SPLIT (survives, parts move) | 5 — ViewerPage.xaml.cs, Viewport, Sidebar, Search, Zoom partials |
| DISSOLVE | 5 — Pdf, PdfTextSelection, PdfAnnotations, OfficeTextSelection, Presentation partials |
| MERGE | 1 — Helpers.cs |
| MOVE | 2 — UpdateBanner.xaml, UpdateBanner.xaml.cs |
| DELETE | 8 — see tree §3 |
| NEW (app) | 6 — Formats/Pdf ×4, Formats/Docx ×2 |
| NEW (test) | 1 — NamespaceDependencyTests.cs |

Check: 125+4+5+5+1+2+8 = 150 current ✓. Final app tree = 150 − 8 − 6 + 6 = **142 files**; final test tree = **18 files**.
