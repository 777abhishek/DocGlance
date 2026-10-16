# 01 — Repository Inventory (DocGlance)

Date: 2026-10-16 · Baseline: `main` @ `6fa6515` (working tree clean)
Method: direct inspection of every maintained source file (sizes via `wc -l`), consumer tracing via project-wide grep, and workflow tracing through the actual call chains. Vendored Rust code (`native/pdfcraft_ffi/vendor/`, 4.1 MB — hayro, hayro-interpret, hayro-syntax, hayro-jbig2, lopdf) is treated as third-party dependency, **not** application source.

> Naming note: this app was renamed QuickView → DocGlance (commit `543fc8a`). One `qv_` prefix survives in `native/translation_ffi` exports; `docs/reliability/REPOSITORY_AUDIT.md` and `docs/android-kotlin-integration.md` still use "QuickView". This document set uses **DocGlance** throughout.

## 1. Solution shape

| Unit | Kind | LOC (maintained) | Notes |
|---|---|---:|---|
| `DocGlance.csproj` (repo root) | WinUI 3 app, single project | ~28,600 C#/XAML | net8.0-windows10.0.19041.0, WinAppSDK 2.4, Win2D, EF Core SQLite, CommunityToolkit.Mvvm 8.4. MSIX-packaged with `PACKAGED` symbol; unpackaged path retained via `#if !PACKAGED`. |
| `DocGlance.Tests/` | xUnit test project | ~2,075 | References the main project; tests only non-UI types. |
| `native/pdfcraft_ffi` | Rust `cdylib` | 2,184 (+vendor) | PDF engine. 27 exports `pc_*`. Vendored: hayro renderer + lopdf parser. |
| `native/wordcraft_ffi` | Rust `cdylib` | 895 | DOCX engine. 14 exports `wc_*`. |
| `native/gridcraft_ffi` | Rust `cdylib` | 746 | XLSX engine. 11 exports `gc_*`. |
| `native/deckcraft_ffi` | Rust `cdylib` | 575 | PPTX engine. 12 exports `dc_*`. |
| `native/translation_ffi` | Rust `cdylib` | 584 | Offline translation. 5 exports `qv_*` (rename leftover). |
| `tools/` | 2 tiny C# utilities | ~350 | `EmbedIcon` (PE icon embedding), `imggen` (asset generation). Excluded from app compilation (`Compile Remove="tools\**"` in csproj). |
| `installer/` | Inno Setup + winget | — | `DocGlance.iss`, winget manifest generator. |
| `build.ps1`, `build-installer.ps1`, `native/build-native.ps1` | Build scripts | — | Native DLLs built by `build-native.ps1`, consumed by csproj as conditional `Content` items (`DocGlance.csproj:99-120`). |

There is **no `.sln` file** — the test csproj is discovered by IDE/dotnet directly, and the main project excludes `DocGlance.Tests\**` and `tools\**` from its own compilation (`DocGlance.csproj:89-95`).

## 2. C# folder inventory (all maintained files)

### Root
| File | LOC | Responsibility |
|---|---:|---|
| `Program.cs` | 232 | Custom `Main`: PerMonitorV2 DPI awareness before WinUI init, single-instance redirect via `AppInstance`, named-pipe activation handover to first instance. |
| `App.xaml.cs` | 392 | App lifecycle: global exception handlers (crash-logging policy documented inline), renderer registration, activation-args decoding, file opening, theme. |
| `App.xaml` | 22 | Merged resource dictionaries (Colors, Typography, Controls, FileTemplates, FileTypeIcons). |
| `Package.appxmanifest`, `app.manifest`, `Properties/AssemblyInfo.cs` | — | MSIX identity, file associations, per-monitor DPI manifest, manual assembly attributes. |

### `Common/` (5 files, 595 LOC) — small pure helpers
- `Formatters/DateFormatter.cs`, `Formatters/FileSizeFormatter.cs` — display formatting.
- `Utilities/FileTypeDetector.cs` — extension → `FileCategory` mapping (Document/Text/Image/Video/Audio/Code/Archive/Executable/Unknown).
- `Validators/FileSizeValidator.cs`, `Validators/PathValidator.cs` — open-request validation (DoS/security).

### `Core/` (19 files, 726 LOC) — shared models & interfaces
- `Constants/FileExtensions.cs`, `Constants/SupportedExtensions.cs` — extension lists (`SupportedExtensions.All` is the canonical file-association list).
- `Enums/FileCategory.cs`, `Enums/ViewMode.cs`.
- `Extensions/UIThreadHelper.cs` — dispatcher helper.
- `Interfaces/IFileRenderer.cs` (25 LOC — render file → `UIElement`), `Interfaces/IFileScanner.cs`, `Interfaces/IThumbnailProvider.cs`.
- `Models/` — shared: `DocumentItem`, `DocumentGroup`, `OutlineItem`, `BookmarkItem`, `PageThumbnailItem`; **PDF-specific** (see finding F5): `PdfMetadata`, `PdfPageTextMap`, `PdfSearchResult`, `PdfViewportState`, `PdfLink`, `PdfPermissions`.

### `Features/` (37 files, 15,170 LOC) — pages, by far the largest layer
| Area | Files | LOC | Responsibility |
|---|---|---:|---|
| `Viewer/ViewerPage.xaml(.cs)` | 2 | 913 | Viewer page shell; declares the partial class (main + 15 partials); shared state fields. |
| `Viewer/Partials/` | 15 files | 6,543 | **One `ViewerPage` partial class**: `Pdf.cs` (1,439 — actually the *all-format* loader/dispatcher), `PdfAnnotations.cs` (859), `PdfTextSelection.cs` (729), `Viewport.cs` (640), `OfficeTextSelection.cs` (515), `Sidebar.cs` (504), `Search.cs` (478), `Zoom.cs` (315), `Text.cs` (270), `Presentation.cs` (250), `Toolbar.cs` (235), `KeyboardShortcuts.cs` (105), `Spreadsheet.cs` (127), `Translator.cs` (55), `Helpers.cs` (22). |
| `Viewer/ViewerViewModel.cs` | 1 | 131 | Observable state (page/zoom/rotation/sidebar/loading/error) + relay commands; state is *pushed into it* from ViewerPage fields. |
| `Viewer/Components/` | 6 | ~1,500 | `ViewerToolbar.xaml(.cs)` (464 XAML + code-behind), code-only dialogs `DocumentInfoDialog.cs`, `TranslationDialog.cs`, `TranslationModelsDialog.cs`, `ViewerPopupStyle.cs`, `SearchHighlighter.cs`. |
| `Viewer/Spreadsheet/` | 2 | 1,883 | `SpreadsheetViewer.xaml(.cs)` — custom Win2D cell-grid control (virtualized rendering, selection, zoom, search highlighting). |
| `Home/` | 3 | 1,192 | Recent/All-Documents home with segmented toggle; uses `FileIndexService`, `FolderScanner`, `ThumbnailService`. |
| `Tools/` | 2 | 1,765 | Utility tools (Word→PDF conversion, PDF merge/split); code-behind calls `DocxDocument`, `PptxDocument`, `PdfViewerService` native wrappers directly. |
| `Settings/` | 2 | 1,061 | Settings page (theme, defaults, Quick Look hotkey, file associations, verbose logging, translation models, updater). |
| `Browser/` | 3 | 77 | **Near-empty stub**: 10-line `BrowserPage`, 12-line XAML, 65-line VM wrapping `FolderScanner`. No navigation entry point reaches it. |

### `Infrastructure/` (7 files, 899 LOC)
- `Logging/Logger.cs`, `LogFileWriter.cs`, `LogScope.cs`, `TaskExtensions.cs` (`SafeFireAndForget`) — async queued file logger with operation scopes.
- `Concurrency/NativeCallGate.cs` — serializes native calls against dispose (prevents use-after-free at the FFI boundary).
- `Configuration/AppSettings.cs` — static typed settings facade persisted via `SettingsStore`; `SettingsChanged` event.
- `Diagnostics/DiagnosticsService.cs` — milestones, memory checkpoints, environment logging.

### `Rendering/` (22 files, 3,932 LOC) — format engines + interop (mixed concerns)
| Path | LOC | Responsibility |
|---|---:|---|
| `Pdf/PdfCraftNative.cs` | 124 | P/Invoke to `pdfcraft_ffi.dll` (25 `DllImport`s, older style). |
| `Pdf/PdfRenderer.cs` | ~300 | Thin safe wrapper over `PdfCraftNative` (open/render/text/outline/search). |
| `Pdf/PdfDocumentState.cs` | ~150 | Owns `PcDocument*` handle + rendered-page bookkeeping (native gate on dispose). |
| *(PDF orchestration lives in `Services/Documents/PdfViewerService.cs`, 284 LOC — see Services table)* | | — | — |
| `Pdf/PdfAnnotationService.cs` | 527 | Annotation add/delete/save via FFI + history snapshots. |
| `Pdf/PdfTextService.cs` | ~200 | Text extraction, char maps, hit-testing, selection rects. |
| `Document/WordCraftNative.cs` | 200 | `[LibraryImport]` bindings `wc_*` + memory contract docs. |
| `Document/GridCraftNative.cs` | 161 | `[LibraryImport]` bindings `gc_*`. |
| `Document/DocxDocument.cs` | 537 | DOCX lifecycle: open, page render, text, search, selection, export. |
| `Document/SpreadsheetDocument.cs` | ~250 | XLSX lifecycle: open, sheet data, export to PDF. |
| `Document/SheetData.cs` | ~200 | Managed model of a sheet (cells, dims) from `gc_get_sheet_data_json`. |
| `Office/DocxRenderer.cs` | 26 | `IFileRenderer` → **info-card fallback only**. |
| `Office/XlsxRenderer.cs` | ~25 | `IFileRenderer` → info-card fallback. |
| `Office/PptxRenderer.cs` | 56 | `IFileRenderer` that **returns `null`** after writing debug lines to `%TEMP%\DocGlance_pptx_debug.txt` — legacy scaffolding. |
| `Presentation/DeckCraftNative.cs` | 166 | `[LibraryImport]` bindings `dc_*`. |
| `Presentation/PptxDocument.cs` | ~250 | PPTX lifecycle: open, slide render, text, search. |
| `Presentation/PptxViewerControl.xaml(.cs)` | 178 | **A UI control** living in Rendering. |
| `Code/CodeRenderer.cs` | ~150 | Syntax-highlighted code viewer. **No consumers found** (only self-reference). |
| `Text/TextFileReader.cs` | ~80 | Capped, encoding-detecting text read (`ReadCapped`). |
| `NativeBitmap.cs` | ~120 | RGBA→`WriteableBitmap` on the UI dispatcher; static init in `App.OnLaunched`. |
| `RenderBudget.cs` | ~80 | Memory budget for page caches. |
| `RendererRegistry.cs` | 26 | Static ext→`IFileRenderer` map. |

### `Services/` (33 files, 4,403 LOC)
- `Activation/ActivationIpc.cs` (159), `ActivationPaths.cs` (157) — named-pipe single-instance handover; activation-arg decoding.
- `Documents/` — `IPdfViewerService.cs` (95) + `PdfViewerService.cs` (284) + `PdfLoadResult.cs`. **The only interface+implementation service pair in the app.**
- `FileSystem/Index/` — `FileIndexService` (129), `FileIndexContext` (56, EF Core SQLite), `FileIndexRepository` (54).
- `FileSystem/Scanner/FolderScanner.cs` (134); `FileSystem/Watcher/FileWatcherService.cs` (~140 — **no consumers found**).
- `Input/GlobalHotkeyRegistration.cs` (182) — RegisterHotKey wrapper for Quick Look.
- `Registration/AppRegistration.cs` (270) — HKCU file-association registration (unpackaged) / no-op shim (packaged). `RegistrationHelper.cs` (145) — startup ensure-registered.
- `Storage/` — `SettingsStore` (110, JSON per-key), `RecentFilesStore` (108-ish), `BookmarkStore` (108), `SessionStateStore` (~120).
- `Thumbnails/` — `ThumbnailService` (108), `ThumbnailCache` (197, disk cache), providers: `GenericThumbnailProvider`, `OfficeThumbnailProvider`, `PdfThumbnailProvider`.
- `Translation/` (8 files, ~1,460) — `ITranslationService`/`TranslationService`, `TranslationModelManager` (471, model download/deploy), `TranslationModelCatalog` (216), `TranslationEngineNative` (182, `qv_*` P/Invoke), `TranslationLanguage`, `TranslationSettings`, `TranslationTextChunker`.
- `Updates/` — `UpdateService.cs` (600, GitHub-release check + signature verification via wintrust) and **`UpdateBanner.xaml(.cs)` — a UI control inside Services**, consumed by `UI/Shell/TitleBarWindow.xaml`.

### `UI/` (12 files, 2,312 LOC)
- `Shell/TitleBarWindow.xaml(.cs)` — tabbed multi-window shell (1,331 LOC code-behind): tabs incl. tear-out/merge, drag-drop open, file picker, Quick-Look global hotkey ownership, session save/restore, acrylic backdrop, title-bar theming, `Current`/`AllWindows` static roots.
- `Controls/` — `DocumentCard.xaml(.cs)`, `PageThumbnailSidebar.xaml(.cs)` (unused by ViewerPage? — sidebar is re-implemented inline; only referenced by its own files), `ViewerInfoCard.cs` (code-only fallback card).
- `Icons/FileTypeIcons.xaml`, `Templates/FileTemplates.xaml`, `Styles/{Colors,Typography,Controls}.xaml`.

### `DocGlance.Tests/` (16 files, 2,075 LOC)
xUnit + FluentAssertions. Mirrors main folders: formatters, validators, `SupportedExtensions`, `PdfPageTextMap`, `HomeViewModel`, `ViewerViewModel`, `NativeCallGate`, `Logger`, `DocxOutline`, `PdfEncryptionProbe`, `PdfViewerService`, translation (Language/ModelCatalog/TextChunker). No architectural tests, no viewer-level tests.

## 3. Native (Rust) inventory

| Crate | Exports | Responsibility |
|---|---|---|
| `pdfcraft_ffi` | 27 `pc_*` | open (path/bytes), page count/size, render page, text extract, annotations (text/ink/image, list, delete), outline, search, export PNG, merge/split PDFs, selection hit-test/rects, char maps, free fns (`pc_free_image/png_buffer/pdf_buffer/document`). Vendored hayro+lopdf for parsing/rendering. |
| `wordcraft_ffi` | 14 `wc_*` | open, page count/size, render, text, search, export-to-PDF, hit-test/selection/char maps, free fns. |
| `gridcraft_ffi` | 11 `gc_*` | open, sheet count/name/size, export sheet→PDF, sheet data JSON, text, search, free fns. |
| `deckcraft_ffi` | 12 `dc_*` | open, slide count/size, render, titles, text, char rects/ranges, search, free fns. |
| `translation_ffi` | 5 `qv_*` | detect language, translate, verify model decompress, release cached model, free buffer. |

All crates are `cdylib`, exports are `extern "C"` with `catch_unwind` wrappers (per `docs/reliability/REPOSITORY_AUDIT.md`; spot-checked in `pdfcraft_ffi/src/lib.rs`). Memory contract: **Rust allocates, C# frees via the matching `*_free_*` export** — documented in each `*Native.cs` header comment.

## 4. Key dependency relationships (observed)

```
Program.cs ──► App ──► TitleBarWindow ──► ViewerPage (new tab) ──► ViewerViewModel
                     │                        │
                     │                        ├─ (hardcoded ext dispatch, ViewerPage.Pdf.cs:120)
                     │                        ├─ .pdf  ► Services/Documents/PdfViewerService ► Rendering/Pdf/* ► pdfcraft_ffi.dll
                     │                        ├─ .docx ► Rendering/Document/DocxDocument ► wordcraft_ffi.dll
                     │                        ├─ .pptx ► Rendering/Presentation/PptxDocument ► deckcraft_ffi.dll
                     │                        ├─ .xlsx ► Rendering/Document/SpreadsheetDocument ► gridcraft_ffi.dll
                     │                        │           └─► SpreadsheetViewer control (Win2D grid)
                     │                        ├─ text  ► Rendering/Text/TextFileReader
                     │                        └─ else ► RendererRegistry (info-card fallbacks)
                     ├─► SettingsStore ◄─ AppSettings (Infrastructure)
                     └─► ActivationIpc / ActivationPaths / RegistrationHelper
Home ─► FileIndexService (EF/SQLite) + FolderScanner + ThumbnailService/Cache
Tools ─► DocxDocument / PptxDocument / PdfViewerService (direct native-wrapper use)
All native wrappers ─► Infrastructure/Concurrency/NativeCallGate (serialize vs dispose)
Translation dialog(s) ─► Services/Translation/* ► translation_ffi.dll
```

There is **no dependency-injection container**. Cross-cutting wiring is via statics: `RendererRegistry`, `Logger`, `AppSettings`/`SettingsStore`, `TitleBarWindow.Current`, `App.AllWindows`, `NativeBitmap` (dispatcher init).

## 5. Duplicate / overlapping / misplaced responsibilities (findings index)

- **F1 — ViewerPage god class.** 15 partial files + main (16 files), one class, 6,823 LOC code-behind (7,587 with XAML + ViewModel), 83 private/internal shared `_fields` (e.g. `_doc`, `_pdfState`, `_zoom`, `_page`, `_total`, `_rotation`, `_cts`, `_spreadsheetDocument`, `_pptxDocument`, `_docxDocument`, `_pptxTextMaps`, `_searchCts`…). All format knowledge converges here (`Features/Viewer/Partials/ViewerPage.Pdf.cs:120-143`).
- **F2 — Misnamed dispatch partial.** `ViewerPage.Pdf.cs` contains `LoadDocumentCoreAsync` + `LoadDocxAsync/LoadPptxAsync/LoadXlsxAsync/LoadTextFileAsync` and thumbnails — it is the universal loader, not PDF-specific.
- **F3 — Dual viewer state.** `ViewerViewModel` exists but `ViewerPage` keeps `_page/_zoom/_total/_rotation` and manually calls `SyncToViewModel()` after each mutation (`ViewerPage.xaml.cs:28-38,180-189`). Two sources of truth.
- **F4 — Legacy renderer pipeline.** `IFileRenderer` + `RendererRegistry` + `Rendering/Office/*` are only reachable as an *else* fallback (`ViewerPage.Pdf.cs:133-143`) and return info cards; `PptxRenderer.RenderAsync` returns `null` after writing a debug temp-file log. `Core/Interfaces/IFileScanner` and `IThumbnailProvider` have single implementations used once.
- **F5 — PDF-specific models in shared Core.** `Core/Models/Pdf*.cs` (6 files) are PDF-only concepts living in the shared model namespace (consumers: 1–6 files each, all PDF-path).
- **F6 — Rendering layer mixes three concerns.** FFI bindings, document lifecycle services, and UI controls (`PptxViewerControl`, and `SpreadsheetViewer` conversely lives in Features) all sit under `Rendering/`. Naming is inconsistent: `Document/` holds both DOCX and XLSX; `Office/` holds fallback cards; `Presentation/` holds PPTX + its UI control; `Code/CodeRenderer` appears dead.
- **F7 — TitleBarWindow god shell.** 1,331 LOC: tabs + tear-out + drag-drop + picker + Quick-Look hotkey + session persistence + acrylic/theming + icon.
- **F8 — UI in Services.** `Services/Updates/UpdateBanner.xaml` is a control; `Services/Registration/AppRegistration` embeds a UI-facing icon-name mapping.
- **F9 — Dead/near-dead code.** `Rendering/Code/CodeRenderer.cs` (no consumers; `IsCodeFile() => false` — "No longer using separate code viewer"), `Services/FileSystem/Watcher/FileWatcherService.cs` (no consumers), `Core/Interfaces/IFileScanner.cs` (zero consumers — `FolderScanner` neither implements nor references it, AF-07), `Features/Browser/` (77 LOC stub, unreferenced by navigation), `UI/Controls/PageThumbnailSidebar` (ViewerPage implements its own thumbnail list inline; control has no external consumers).
- **F10 — Settings triplication risk.** `AppSettings` (typed facade) over `SettingsStore` (JSON) is fine; `TranslationSettings` (in Services/Translation) follows the same pattern separately — acceptable but duplicated mechanism; document convention.
- **F11 — Interop style drift.** `PdfCraftNative` uses `[DllImport]` (25 imports); WordCraft/GridCraft/DeckCraft/Translation use source-generated `[LibraryImport]`. Prefix scheme `pc_/wc_/gc_/dc_/qv_` is consistent except `qv_` (pre-rename).
- **F12 — Activation logic in App.** `App.xaml.cs` carries activation decoding, IPC server start, error dialogs, command-line fallback — an app-class responsibility creep (392 LOC).

## 6. Naming conventions currently observed

- C#: PascalCase types/members; `_camelCase` private fields; `Async` suffix mostly honored; MVVM via CommunityToolkit source generators (`[ObservableProperty]`, `[RelayCommand]`).
- Namespaces mirror folders (`DocGlance.Features.Viewer`, `DocGlance.Rendering.Pdf`, …) with exceptions: `Rendering.Document` namespace inside folder `Rendering/Document`; `Services.Documents`.
- Rust: crates `*_craft_ffi` (except `translation_ffi`); modules `snake_case`; export prefixes per engine.
- XAML: pages `*Page.xaml`, controls `*Control`/`*Card`/`*Dialog` inconsistently (`SpreadsheetViewer`, `ViewerToolbar`, `DocumentCard`, `UpdateBanner`).
- Docs: two docs still say "QuickView"; test/audit docs reference a `QuickView.Tests` project name that no longer exists.

## 7. Files whose purpose is uncertain

| File | Uncertainty |
|---|---|
| `Rendering/Code/CodeRenderer.cs` | No consumers found; possibly superseded by unified text viewer (`ViewerPage.Pdf.cs:130` comment: "All text-based files now use the same lightweight viewer"). |
| `Services/FileSystem/Watcher/FileWatcherService.cs` | No consumers found; likely built for a Home auto-refresh that shipped without wiring. |
| `Features/Browser/*` | 77-LOC stub; no navigation route references it. |
| `UI/Controls/PageThumbnailSidebar.xaml(.cs)` | No external consumers; ViewerPage builds thumbnails inline (`Thumbnails` + `ThumbnailList`). |
| `UI/Icons/FileTypeIcons.xaml` | Loaded in App.xaml; verify template keys match usage (believed live). |

Everything else in the maintained tree has an identified responsibility and at least one consumer.
