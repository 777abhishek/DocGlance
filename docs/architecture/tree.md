# tree.md — The Complete, Authoritative Final Repository Tree

**This file is the single source of truth for the repository structure after the architecture refactoring.** Docs `01–10` provide rationale; where any document disagrees with this tree, this tree wins (deviations logged in `architecture-review-findings.md`, IDs `AF-xx`).

Scope decisions fixed by review:
- **Rust is intact** — no crate, export, prefix, lockfile, or vendored file changes (`native/` is reproduced below verbatim for completeness).
- One app csproj + one test csproj. No new projects, no DI container.
- Status of the plan: **approved design, not yet implemented.** Nothing below exists yet except files marked `[KEEP]`/`[MOVE]` sources.

Counting rules (AF-15): `[KEEP]` = path unchanged (content may be edited in place). `[MOVE]` = file relocates (possibly renamed), responsibility intact. `[SPLIT-in-place]` = file survives at the same path with reduced scope while parts move out. `[SPLIT→dissolve]` = file ceases to exist; every responsibility moves to named destinations. `[MERGE]` = source file absorbed. `[DELETE]` = removed (see deletion ledger). `[NEW]` = did not exist.

---

## 1. THE FINAL TREE

```text
DocGlance/
│
│   ── Root ─────────────────────────────────────────────────────────────
├── DocGlance.csproj                     [KEEP]    app project; native-DLL Content items unchanged
├── Program.cs                           [KEEP]    232 LOC — DPI awareness, single-instance redirect
├── App.xaml                             [KEEP]    22 LOC — merged resource dictionaries
├── App.xaml.cs                          [SPLIT-in-place → ~180 LOC] wiring + exception policy only
│                                                   (activation/cmdline/error-dialog logic → ActivationService, AF-12 of 01)
├── Package.appxmanifest                 [KEEP]    MSIX identity + file associations
├── app.manifest                         [KEEP]    PerMonitorV2 DPI
├── .gitignore                           [KEEP]
├── DocGlance.cer                        [KEEP]    public signing cert (git-tracked; used by installer)
├── build.ps1                            [KEEP]    managed build/publish
├── build-installer.ps1                  [KEEP]    MSIX + installer pipeline
│
│   ── Pure helpers ────────────────────────────────────────────────────
├── Common/
│   ├── Formatters/
│   │   ├── DateFormatter.cs             [KEEP]    relative/absolute date formatting
│   │   └── FileSizeFormatter.cs         [KEEP]    byte-size formatting
│   ├── Utilities/
│   │   └── FileTypeDetector.cs          [KEEP]    extension → FileCategory mapping
│   └── Validators/
│       ├── FileSizeValidator.cs         [KEEP]    open-request size/safety validation
│       └── PathValidator.cs             [KEEP]    path safety validation
│
│   ── Format-neutral core ─────────────────────────────────────────────
├── Core/
│   ├── Constants/
│   │   ├── FileExtensions.cs            [KEEP]    extension literals
│   │   └── SupportedExtensions.cs       [KEEP]    canonical open-with list
│   ├── Enums/
│   │   ├── FileCategory.cs              [KEEP]
│   │   ├── ViewMode.cs                  [KEEP]
│   │   └── FitMode.cs                   [NEW]     None/FitWidth/FitPage — extracted from ViewerPage.Zoom.cs
│   ├── Interfaces/
│   │   └── IThumbnailProvider.cs        [KEEP]    thumbnail provider contract (4 implementations)
│   └── Models/
│       ├── DocumentItem.cs              [KEEP]    openable file descriptor
│       ├── DocumentGroup.cs             [KEEP]    Home grouping
│       ├── OutlineItem.cs               [KEEP]    outline/TOC node (Pdf, Docx, Pptx)
│       ├── BookmarkItem.cs              [KEEP]    user bookmark
│       └── PageThumbnailItem.cs         [KEEP]    sidebar thumbnail VM
│
│   ── Cross-cutting infrastructure ────────────────────────────────────
├── Infrastructure/
│   ├── Concurrency/
│   │   └── NativeCallGate.cs            [KEEP]    serialize native calls vs dispose
│   ├── Configuration/
│   │   └── AppSettings.cs               [KEEP]    typed settings facade + SettingsChanged
│   ├── Diagnostics/
│   │   └── DiagnosticsService.cs        [KEEP]    milestones/memory checkpoints/env log
│   ├── Extensions/
│   │   └── UIThreadHelper.cs            [MOVE←Core/Extensions] Core goes WinUI-free (AF-02 rule)
│   └── Logging/
│       ├── Logger.cs                    [KEEP]    queued file logger + operation scopes
│       ├── LogFileWriter.cs             [KEEP]    batched file writer
│       ├── LogScope.cs                  [KEEP]    operation correlation
│       └── TaskExtensions.cs            [KEEP]    SafeFireAndForget
│
│   ── FFI boundary (one engine per folder, internal) ──────────────────
├── Interop/
│   ├── README.md                        [NEW]     ABI table: exports, structs, Rust-allocates/C#-frees pairs
│   ├── PdfCraft/
│   │   └── PdfCraftNative.cs            [MOVE←Rendering/Pdf] pc_* bindings; DllImport→LibraryImport
│   ├── WordCraft/
│   │   └── WordCraftNative.cs           [MOVE←Rendering/Document] wc_* bindings
│   ├── GridCraft/
│   │   └── GridCraftNative.cs           [MOVE←Rendering/Document] gc_* bindings
│   ├── DeckCraft/
│   │   └── DeckCraftNative.cs           [MOVE←Rendering/Presentation] dc_* bindings
│   └── Translation/
│       └── TranslationNative.cs         [MOVE+RENAME←Services/Translation/TranslationEngineNative.cs] qv_* bindings
│
│   ── Rendering engines, one folder per format ────────────────────────
├── Rendering/
│   ├── Pdf/
│   │   ├── IPdfViewerService.cs         [MOVE←Services/Documents] PDF orchestration contract (viewer + Tools)
│   │   ├── PdfViewerService.cs          [MOVE←Services/Documents] lifecycle, passwords, progressive render, prefetch
│   │   ├── PdfLoadResult.cs             [MOVE←Services/Documents] load outcome
│   │   ├── PdfRenderer.cs               [KEEP]    safe wrapper over PdfCraftNative
│   │   ├── PdfDocumentState.cs          [KEEP]    owns PcDocument* + page bookkeeping
│   │   ├── PdfTextService.cs            [KEEP]    text extract, char maps, hit-test
│   │   ├── PdfAnnotationService.cs      [KEEP]    annotation CRUD + history via FFI
│   │   └── Models/
│   │       ├── PdfMetadata.cs           [MOVE←Core/Models]
│   │       ├── PdfPageTextMap.cs        [MOVE←Core/Models]
│   │       ├── PdfSearchResult.cs       [MOVE←Core/Models]
│   │       ├── PdfViewportState.cs      [MOVE←Core/Models]
│   │       ├── PdfLink.cs               [MOVE←Core/Models]
│   │       └── PdfPermissions.cs        [MOVE←Core/Models]
│   ├── Docx/
│   │   └── DocxDocument.cs              [MOVE←Rendering/Document] DOCX engine: open/render/text/search/export
│   ├── Xlsx/
│   │   ├── SpreadsheetDocument.cs       [MOVE←Rendering/Document] XLSX engine: open/sheets/export-pdf
│   │   └── SheetData.cs                 [MOVE←Rendering/Document] managed sheet model (from gc JSON)
│   ├── Pptx/
│   │   └── PptxDocument.cs              [MOVE←Rendering/Presentation] PPTX engine: open/slides/text/search
│   └── Shared/
│       ├── NativeBitmap.cs              [MOVE←Rendering] RGBA→WriteableBitmap on UI dispatcher
│       ├── RenderBudget.cs              [MOVE←Rendering] page-cache memory budget
│       └── TextFileReader.cs            [MOVE←Rendering/Text] capped encoding-aware text read
│
│   ── Features (pages + viewers) ──────────────────────────────────────
├── Features/
│   ├── Viewer/
│   │   │   # ── Shell ──
│   │   ├── ViewerPage.xaml              [KEEP]    viewer tab layout: canvas host, sidebar, overlays
│   │   ├── ViewerPage.xaml.cs           [SPLIT-in-place → ~250 LOC] coordinator: registry lookup, lifecycle,
│   │   │                                           loading/error states, resize debouncing, public API
│   │   ├── ViewerViewModel.cs           [KEEP→grows] SOLE owner of page/zoom/rotation/fit/sidebar state
│   │   ├── DocumentViewerRegistry.cs    [NEW]     ext→IDocumentViewerFactory priority map (always resolves)
│   │   ├── Partials/
│   │   │   ├── ViewerPage.Toolbar.cs        [KEEP] toolbar event wiring (capability-driven)
│   │   │   ├── ViewerPage.Sidebar.cs        [SPLIT-in-place → ~200 LOC] sidebar hosting; outline/thumbnail
│   │   │   │                                            data now via ISupportsOutline/Thumbnails
│   │   │   ├── ViewerPage.Search.cs         [SPLIT-in-place → ~200 LOC] search panel hosting; execution
│   │   │   │                                            via ISupportsSearch
│   │   │   ├── ViewerPage.Zoom.cs           [SPLIT-in-place → ~150 LOC] fit/zoom coordination; math moved
│   │   │   │                                            to viewers; FitMode enum extracted to Core
│   │   │   ├── ViewerPage.Viewport.cs       [SPLIT-in-place → ~150 LOC] viewport sizing/debounce only;
│   │   │   │                                            per-format render loops moved to viewers
│   │   │   ├── ViewerPage.KeyboardShortcuts.cs [KEEP] shared key routing → focused viewer
│   │   │   └── ViewerPage.Translator.cs      [KEEP] translation dialog launch (AF-08)
│   │   ├── Contracts/                        [NEW folder]
│   │   │   ├── IDocumentViewer.cs            [NEW] Control, LoadAsync, CleanupAsync — mandatory
│   │   │   ├── IDocumentViewerFactory.cs     [NEW] SupportedExtensions, Create(ViewerViewModel)
│   │   │   ├── ISupportsZoom.cs              [NEW] SetZoom/SetFitMode/ZoomChanged (Pdf,Docx,Pptx,Xlsx)
│   │   │   ├── ISupportsSearch.cs            [NEW] SearchAsync/Highlight/ClearHighlight (all viewers)
│   │   │   ├── ISupportsTextSelection.cs     [NEW] SelectionChanged/GetSelectedText/CopySelection (all)
│   │   │   ├── ISupportsOutline.cs           [NEW] Outline/NavigateTo (Pdf,Docx,Pptx)
│   │   │   ├── ISupportsAnnotations.cs       [NEW] tool state/FlushPendingAsync (Pdf only)
│   │   │   └── ISupportsThumbnails.cs        [NEW] thumbnail source (Pdf,Docx,Pptx)
│   │   ├── Components/
│   │   │   ├── ViewerToolbar.xaml            [KEEP] shared toolbar visual
│   │   │   ├── ViewerToolbar.xaml.cs         [KEEP] 268 LOC; imperative setters→VM bindings over time
│   │   │   ├── SearchHighlighter.cs          [KEEP] text-run highlight helper (TextViewer + shared)
│   │   │   ├── DocumentInfoDialog.cs         [KEEP] metadata dialog (code-only)
│   │   │   ├── TranslationDialog.cs          [KEEP] translate dialog (code-only)
│   │   │   ├── TranslationModelsDialog.cs    [KEEP] model picker dialog (code-only)
│   │   │   └── ViewerPopupStyle.cs           [KEEP] shared popup styling
│   │   └── Viewers/                          [NEW folder — one format, one folder]
│   │       ├── Pdf/
│   │       │   ├── PdfViewer.xaml            [NEW←SPLIT] PDF canvas + page stack
│   │       │   ├── PdfViewer.xaml.cs         [NEW←SPLIT] load/password/viewport/thumbnails
│   │       │   │                                       (from ViewerPage.Pdf.cs PDF parts + Viewport.cs PDF loop)
│   │       │   ├── PdfSelectionLayer.cs      [NEW←SPLIT] selection rects/hit-test/copy
│   │       │   │                                       (from ViewerPage.PdfTextSelection.cs, 729 LOC)
│   │       │   └── PdfAnnotationLayer.cs     [NEW←SPLIT] markup tools/history/deferred flush
│   │       │                                           (from ViewerPage.PdfAnnotations.cs, 859 LOC)
│   │       ├── Docx/
│   │       │   ├── DocxViewer.xaml            [NEW←SPLIT]
│   │       │   └── DocxViewer.xaml.cs         [NEW←SPLIT] pagination/viewport/thumbs/outline/selection
│   │       │                                           (from ViewerPage.Pdf.cs LoadDocx*, Viewport docx loop,
│   │       │                                            OfficeTextSelection docx parts, Sidebar outline parts)
│   │       ├── Pptx/
│   │       │   ├── PptxViewer.xaml            [MOVE+RENAME←Rendering/Presentation/PptxViewerControl.xaml]
│   │       │   └── PptxViewer.xaml.cs         [MOVE+RENAME+EXTEND] absorbs slide loop, text maps,
│   │       │                                           presentation mode (from Pdf.cs/Viewport/OfficeTextSelection/
│   │       │                                           Presentation partials)
│   │       ├── Spreadsheet/
│   │       │   ├── SpreadsheetViewer.xaml     [MOVE←Features/Viewer/Spreadsheet] behavior unchanged
│   │       │   └── SpreadsheetViewer.xaml.cs  [MOVE] 1,250 LOC Win2D grid control + factory adapter
│   │       ├── Text/
│   │       │   ├── TextViewer.xaml            [NEW←SPLIT]
│   │       │   └── TextViewer.xaml.cs         [NEW←SPLIT] capped load/highlight/copy
│   │       │                                           (from ViewerPage.Text.cs incl. _textExts, IsTextFile)
│   │       └── Fallback/
│   │           └── FallbackViewer.cs          [MOVE+RENAME+EXTEND←UI/Controls/ViewerInfoCard.cs]
│   │                                                   implements IDocumentViewer; factory claims .xls and
│   │                                                   every extension not otherwise claimed (AF-06)
│   ├── Home/
│   │   ├── HomePage.xaml                 [KEEP]
│   │   ├── HomePage.xaml.cs              [KEEP]  480 LOC recents/all-docs
│   │   └── HomeViewModel.cs              [KEEP]
│   ├── Tools/
│   │   ├── ToolsPage.xaml                [KEEP]
│   │   └── ToolsPage.xaml.cs             [KEEP]  1,174 LOC convert/merge/split — calls engines directly (sanctioned)
│   └── Settings/
│       ├── SettingsPage.xaml             [KEEP]
│       └── SettingsPage.xaml.cs          [KEEP]  532 LOC
│
│   ── App services ─────────────────────────────────────────────────────
├── Services/
│   ├── Activation/
│   │   ├── ActivationIpc.cs              [KEEP]    named-pipe handover server
│   │   ├── ActivationPaths.cs            [KEEP]    activation-arg decoding
│   │   └── ActivationService.cs          [NEW←SPLIT←App.xaml.cs] TryOpenFromActivationArgs/TryOpenFromCommandLine/
│   │                                               OpenExternalFiles/ShowOpenError (~200 LOC)
│   ├── FileSystem/
│   │   ├── Index/
│   │   │   ├── FileIndexService.cs       [KEEP]    SQLite index facade
│   │   │   ├── FileIndexContext.cs       [KEEP]    EF Core context
│   │   │   └── FileIndexRepository.cs    [KEEP]    queries
│   │   └── Scanner/
│   │       └── FolderScanner.cs          [KEEP]    async folder enumeration
│   ├── Input/
│   │   ├── GlobalHotkeyRegistration.cs   [KEEP]    RegisterHotKey wrapper
│   │   └── QuickLookHotkeyService.cs     [NEW←SPLIT←TitleBarWindow] register/release/owner arbitration/
│   │                                               Explorer-selection fetch (~200 LOC)
│   ├── Registration/
│   │   ├── AppRegistration.cs            [KEEP]    HKCU file associations (unpackaged) / shim (packaged)
│   │   └── RegistrationHelper.cs         [KEEP]    startup ensure-registered
│   ├── Session/
│   │   └── SessionService.cs             [NEW←SPLIT←TitleBarWindow] SaveSession/RestoreSession via
│   │                                               SessionStateStore (~150 LOC)
│   ├── Storage/
│   │   ├── SettingsStore.cs              [KEEP]    JSON per-key persistence
│   │   ├── RecentFilesStore.cs           [KEEP]
│   │   ├── BookmarkStore.cs              [KEEP]
│   │   └── SessionStateStore.cs          [KEEP]
│   ├── Thumbnails/
│   │   ├── ThumbnailService.cs           [KEEP]    provider dispatch
│   │   ├── ThumbnailCache.cs             [KEEP]    disk cache, size-bounded
│   │   └── Providers/
│   │       ├── GenericThumbnailProvider.cs  [KEEP] IThumbnailProvider
│   │       ├── OfficeThumbnailProvider.cs   [KEEP] IThumbnailProvider
│   │       └── PdfThumbnailProvider.cs      [KEEP] IThumbnailProvider
│   ├── Translation/
│   │   ├── ITranslationService.cs        [KEEP]    translate contract
│   │   ├── TranslationService.cs         [KEEP]    orchestrates detect/translate via TranslationNative
│   │   ├── TranslationModelManager.cs    [KEEP]    471 LOC model download/deploy/release
│   │   ├── TranslationModelCatalog.cs    [KEEP]    catalog metadata
│   │   ├── TranslationLanguage.cs        [KEEP]    language model
│   │   ├── TranslationSettings.cs        [KEEP]    per-feature settings (documented pattern)
│   │   └── TranslationTextChunker.cs     [KEEP]    chunking
│   └── Updates/
│       └── UpdateService.cs              [KEEP]    600 LOC release check + wintrust verification
│
│   ── Shared UI ────────────────────────────────────────────────────────
├── UI/
│   ├── Shell/
│   │   ├── TitleBarWindow.xaml           [KEEP]    shell layout
│   │   └── TitleBarWindow.xaml.cs        [SPLIT-in-place → ~700 LOC] tabs/tear-out/drop/picker/chrome only
│   ├── Controls/
│   │   ├── DocumentCard.xaml             [KEEP]    Home document card
│   │   ├── DocumentCard.xaml.cs          [KEEP]
│   │   ├── UpdateBanner.xaml             [MOVE←Services/Updates] update banner UI (AF-08 of 01: UI out of Services)
│   │   └── UpdateBanner.xaml.cs          [MOVE←Services/Updates]
│   ├── Icons/
│   │   └── FileTypeIcons.xaml            [KEEP]    file-type icon resources
│   ├── Templates/
│   │   └── FileTemplates.xaml            [KEEP]    file list templates
│   └── Styles/
│       ├── Colors.xaml                   [KEEP]
│       ├── Typography.xaml               [KEEP]
│       └── Controls.xaml                 [KEEP]
│
│   ── Tests (mirror the main tree) ─────────────────────────────────────
├── DocGlance.Tests/
│   ├── DocGlance.Tests.csproj            [KEEP]
│   ├── Common/Formatters/DateFormatterTests.cs            [KEEP]
│   ├── Common/Formatters/FileSizeFormatterTests.cs        [KEEP]
│   ├── Common/Validators/FileSizeValidatorTests.cs        [KEEP]
│   ├── Common/Validators/PathValidatorTests.cs            [KEEP]
│   ├── Core/Constants/SupportedExtensionsTests.cs         [KEEP]
│   ├── Core/Models/PdfPageTextMapTests.cs                 [MOVE] → Rendering/Pdf/ (follows model move)
│   ├── Features/Home/HomeViewModelTests.cs                [KEEP]
│   ├── Features/Viewer/ViewerViewModelTests.cs            [KEEP]  (extended: sole-state-owner behaviors)
│   ├── Features/Viewer/DocumentViewerRegistryTests.cs     [NEW]   resolution incl. .xls fallback (AF-06)
│   ├── Infrastructure/Concurrency/NativeCallGateTests.cs  [KEEP]
│   ├── Infrastructure/Logging/LoggerTests.cs              [KEEP]
│   ├── Rendering/Pdf/PdfViewerServiceTests.cs             [MOVE←Services/Documents] (namespace update)
│   ├── Rendering/Pdf/PdfEncryptionProbeTests.cs           [MOVE←Rendering root] (namespace update)
│   ├── Rendering/Docx/DocxOutlineTests.cs                 [MOVE←Rendering/Document] (namespace update)
│   ├── Services/Translation/LanguageCatalogTests.cs       [KEEP]
│   ├── Services/Translation/TranslationModelCatalogTests.cs [KEEP]
│   ├── Services/Translation/TranslationTextChunkerTests.cs  [KEEP]
│   └── Architecture/NamespaceDependencyTests.cs           [NEW]   encodes 08 §1 layer table as assertions
│
│   ── Native engines: INTACT, listed for completeness ─────────────────
├── native/
│   ├── build-native.ps1                  [KEEP]    cargo build --release for the 5 crates
│   ├── pdfcraft_ffi/
│   │   ├── Cargo.toml                    [KEEP]    cdylib; path-deps on vendor/*
│   │   ├── Cargo.lock                    [KEEP]    committed lockfile
│   │   ├── src/lib.rs                    [KEEP]    2,184 LOC; 27 pc_* exports (catch_unwind)
│   │   └── vendor/                       [THIRD-PARTY] hayro, hayro-interpret, hayro-syntax,
│   │                                                   hayro-jbig2, lopdf (4.1 MB) — do not modify
│   ├── wordcraft_ffi/
│   │   ├── Cargo.toml · Cargo.lock       [KEEP]
│   │   └── src/lib.rs                    [KEEP]    895 LOC; 14 wc_* exports
│   ├── gridcraft_ffi/
│   │   ├── Cargo.toml · Cargo.lock       [KEEP]
│   │   └── src/lib.rs                    [KEEP]    746 LOC; 11 gc_* exports
│   ├── deckcraft_ffi/
│   │   ├── Cargo.toml · Cargo.lock       [KEEP]
│   │   └── src/lib.rs                    [KEEP]    575 LOC; 12 dc_* exports
│   └── translation_ffi/
│       ├── Cargo.toml · Cargo.lock       [KEEP]
│       └── src/lib.rs                    [KEEP]    584 LOC; 5 qv_* exports (prefix stays, AF-09)
│
│   ── Build/dev tools (excluded from app compilation) ──────────────────
├── tools/
│   ├── EmbedIcon/EmbedIcon.cs            [KEEP]    PE icon embedder
│   └── imggen/Program.cs                 [KEEP]    asset generator (AppIconNew.png source)
│
│   ── Installer ────────────────────────────────────────────────────────
├── installer/
│   ├── DocGlance.iss                     [KEEP]    Inno Setup script
│   ├── generate-winget-manifest.ps1      [KEEP]
│   ├── assets/                           [KEEP]    installer artwork
│   ├── winget/                           [KEEP]    generated manifests (policy: generated, refreshed by script)
│   └── output/                           [BUILD OUTPUT] local only, not committed
│
│   ── Assets (binary; enumerated inventory + policy) ──────────────────
├── Assets/
│   ├── AppIcon.ico                       [KEEP]    PE/association icon (csproj ApplicationIcon)
│   ├── AppIconNew.png                    [KEEP]    imggen source artifact — not shipped
│   ├── Preview.jpg                       [KEEP]    Store preview
│   ├── BadgeLogo.png + 7 scale variants  [KEEP]    manifest-referenced
│   ├── SmallTile.png + 7 scale variants  [KEEP]
│   ├── SplashScreen.png + 7 scale variants [KEEP]
│   ├── Square150x150Logo / Square310x310Logo /
│   │   Square44x44Logo (+7 scale + 36 targetsize/altform variants) /
│   │   Square71x71Logo / StoreLogo / Wide310x150Logo
│   │                                     [KEEP]    all manifest-referenced logo sets
│   ├── FileTypeIcons/                    [KEEP]    12 icon pairs (.ico+.png): CSV, Code, Excel, JSON, LOG,
│   │                                               Markdown, PDF, PowerPoint, RichText, Text, Word, XML
│   │                                               (match AppRegistration.GetIconName set)
│   └── Fonts/
│       └── FluentSystemIcons-Regular.ttf [KEEP]    icon font
│   # Note (AF-12): csproj exclusions for Assets\windows\**, Assets\SampleMedia\** and 5 legacy PNGs
│   # reference directories/files that no longer exist; stale Exclude entries may be dropped in Phase 2.
│
│   ── Documentation ────────────────────────────────────────────────────
└── docs/
    ├── architecture/
    │   ├── README.md                     [KEEP]    authoritative index
    │   ├── 01-repository-inventory.md    [KEEP]
    │   ├── 02-current-architecture.md    [KEEP]
    │   ├── 03-target-architecture.md     [KEEP]
    │   ├── 04-naming-conventions.md      [KEEP]
    │   ├── 05-proposed-file-tree.md      [KEEP]    concise planning view — defers to this file
    │   ├── 06-file-size-and-modularity.md [KEEP]
    │   ├── 07-shared-components-matrix.md [KEEP]
    │   ├── 08-dependency-and-interop-rules.md [KEEP]
    │   ├── 09-file-migration-map.md      [KEEP]    exhaustive per-file map → this tree
    │   ├── 10-refactoring-roadmap.md     [KEEP]    8 phases, each names the final paths it completes
    │   ├── tree.md                       [THIS FILE]
    │   ├── architecture-review-findings.md      [KEEP] contradiction register AF-01…AF-15
    │   └── architecture-validation-report.md    [KEEP] counts + acceptance checks
    ├── reliability/                      [KEEP]    historical (2026-10-13); headers gain a rename note in Phase 7
    ├── offline-translation.md            [KEEP]
    └── android-kotlin-integration.md     [KEEP]
```

Local (untracked) artifacts, not repository content: `DocGlance.pfx`, `build.log`, `bin/`, `obj/`, `native/*/target/`, `installer/output/`, `tools/*/obj/`.

---

## 2. SPLIT MAPS (source responsibility → destination)

### 2.1 `ViewerPage` class — 15 partials + main, 6,823 LOC → shell ~1,200 + viewers ~2,400–2,900
| Source partial (LOC) | Disposition | Destination(s) |
|---|---|---|
| `ViewerPage.xaml.cs` (280) | SPLIT-in-place → ~250 | keeps: construction, lifecycle (`OnNavigatedTo/From`, `CancelLoad` → generic `viewer.CleanupAsync`), public API, resize debounce |
| `ViewerPage.Pdf.cs` (1,439) | dissolve | ext dispatch → `DocumentViewerRegistry`; `LoadPdfAsync`/`ShowPasswordDialogAsync` → `PdfViewer.xaml.cs`; `LoadDocxAsync`/`LoadDocxThumbnailsAsync`/`RenderDocxViewportAsync` → `DocxViewer.xaml.cs`; `LoadPptxAsync`/`LoadSlideThumbnailsAsync`/`RenderCurrentSlideAsync`/`RenderPptxViewportAsync` → `PptxViewer.xaml.cs`; `LoadXlsxAsync` → Spreadsheet factory glue; `LoadViaRendererAsync`/`ShowInfoCard` → `FallbackViewer` |
| `ViewerPage.PdfAnnotations.cs` (859) | dissolve | → `Viewers/Pdf/PdfAnnotationLayer.cs` |
| `ViewerPage.PdfTextSelection.cs` (729) | dissolve | → `Viewers/Pdf/PdfSelectionLayer.cs` |
| `ViewerPage.Viewport.cs` (640) | SPLIT-in-place → ~150 | PDF loop → `PdfViewer`; docx/pptx loops → their viewers; viewport sizing stays |
| `ViewerPage.OfficeTextSelection.cs` (515) | dissolve | docx parts → `DocxViewer`; pptx parts → `PptxViewer` |
| `ViewerPage.Sidebar.cs` (504) | SPLIT-in-place → ~200 | hosting stays; `LoadDocxOutlineAsync` → `DocxViewer`; `LoadPptxOutlineAsync` → `PptxViewer` (via `ISupportsOutline`) |
| `ViewerPage.Search.cs` (478) | SPLIT-in-place → ~200 | panel stays; per-format search+highlight → viewers (via `ISupportsSearch`) |
| `ViewerPage.Zoom.cs` (315) | SPLIT-in-place → ~150 | `FitMode` enum → `Core/Enums/FitMode.cs`; coordination stays; math → viewers |
| `ViewerPage.Text.cs` (270) | dissolve | → `Viewers/Text/TextViewer.xaml.cs` (incl. `_textExts`, `IsTextFile`) |
| `ViewerPage.Presentation.cs` (250) | dissolve | → `Viewers/Pptx/PptxViewer.xaml.cs` |
| `ViewerPage.Toolbar.cs` (235) | KEEP | toolbar wiring becomes capability-driven |
| `ViewerPage.Spreadsheet.cs` (127) | dissolve | event glue merges into `ViewerPage.xaml.cs` construction |
| `ViewerPage.KeyboardShortcuts.cs` (105) | KEEP | forwards format keys to focused viewer |
| `ViewerPage.Translator.cs` (55) | KEEP | — |
| `ViewerPage.Helpers.cs` (22) | MERGE | helpers into `ViewerPage.xaml.cs` |

### 2.2 `App.xaml.cs` (392) → ~180 + `Services/Activation/ActivationService.cs` [NEW ~200]
Moves: `TryOpenFromActivationArgs`, `TryOpenFromCommandLine`, `OpenExternalFiles`, `ShowOpenError`. Stays: ctor + exception policy, `OnLaunched` wiring, `RegisterRenderers` **removed entirely** (registry replaces), `SetTheme`.

### 2.3 `UI/Shell/TitleBarWindow.xaml.cs` (1,331) → ~700 + two services
Moves: `SaveSession`/`RestoreSession` → `SessionService`; hotkey block (`TrySetQuickLookShortcutEnabled`, `TrySetQuickLookHotkey`, `Suspend/ResumeQuickLookHotkey`, `Become/ReleaseQuickLookHotkeyOwner`, `HandleGlobalFileOpenHotkey`, `TryGetSelectedExplorerFile`) → `QuickLookHotkeyService`. Stays: tabs, tear-out, drag-drop, picker, chrome/theme, acrylic.

---

## 3. DELETION LEDGER (13 files)

| # | Path | Evidence | Phase |
|---|---|---|---|
| 1 | `Rendering/RendererRegistry.cs` | superseded by `DocumentViewerRegistry`; only consumer is the legacy fallback branch + `App.RegisterRenderers` | 5 |
| 2 | `Core/Interfaces/IFileRenderer.cs` | implemented only by the three Office fallback renderers | 5 |
| 3 | `Rendering/Office/DocxRenderer.cs` | info card only; `.docx/.doc` never reach the registry (dispatch handles first) | 5 |
| 4 | `Rendering/Office/XlsxRenderer.cs` | info card only; `.csv` → text branch, `.xlsx` → dispatch; **`.xls` behavior moves to `FallbackViewer` (AF-06)** | 5 |
| 5 | `Rendering/Office/PptxRenderer.cs` | `RenderAsync` returns `null` after writing `%TEMP%\DocGlance_pptx_debug.txt`; pptx never reaches registry | 5 |
| 6 | `Core/Interfaces/IFileScanner.cs` | **zero consumers** — `FolderScanner` neither implements nor references it (AF-07) | 7 |
| 7 | `Rendering/Code/CodeRenderer.cs` | zero consumers; `IsCodeFile() => false` "No longer using separate code viewer" | 7 |
| 8 | `Services/FileSystem/Watcher/FileWatcherService.cs` | zero consumers | 7 |
| 9–11 | `Features/Browser/BrowserPage.xaml`, `BrowserPage.xaml.cs`, `BrowserViewModel.cs` | zero navigation references (77-LOC stub) | 7 |
| 12–13 | `UI/Controls/PageThumbnailSidebar.xaml`, `.xaml.cs` | zero external consumers; ViewerPage implements thumbnails inline | 7 |

Phase 5 deletions are verified dead by reachability at cutover; Phase 7 deletions each get a pre-delete grep + one manual run of the affected surface.

## 4. NEW-FILE LEDGER (21 code files + 3 docs/tests infra)

| Path | Justification (why a separate file) |
|---|---|
| `Core/Enums/FitMode.cs` | shared enum needed by `ISupportsZoom` before viewers exist |
| `Features/Viewer/DocumentViewerRegistry.cs` | one visible selection mechanism; replaces hidden if/else |
| `Contracts/` ×8 | each ≤40 LOC; shell discovers capabilities without format knowledge; no god interface |
| `Viewers/Pdf/PdfViewer.xaml` + `.cs` | PDF's own control boundary (SpreadsheetViewer precedent) |
| `Viewers/Pdf/PdfSelectionLayer.cs` | 729 LOC of PDF-only selection; independent change driver |
| `Viewers/Pdf/PdfAnnotationLayer.cs` | 859 LOC of PDF-only annotation UI; independent change driver |
| `Viewers/Docx/DocxViewer.xaml` + `.cs` | DOCX boundary |
| `Viewers/Text/TextViewer.xaml` + `.cs` | text boundary |
| `Services/Activation/ActivationService.cs` | testable activation decode/dispatch; App becomes wiring |
| `Services/Session/SessionService.cs` | session persistence; shell slims |
| `Services/Input/QuickLookHotkeyService.cs` | hotkey ownership/arbitration; unit-testable |
| *(tests)* `DocumentViewerRegistryTests.cs`, `Architecture/NamespaceDependencyTests.cs` | cover the new seam; enforce layering |
| *(docs)* `tree.md` (this file), `architecture-review-findings.md`, `architecture-validation-report.md` | required deliverables |

`FallbackViewer.cs` is NOT new — it is `ViewerInfoCard.cs` moved, renamed, and extended (counted as MOVE).

## 5. ACCEPTANCE CHECKS (verified against this tree)

1. Every maintained current file (139 app code files, inventoried in `01` and `09`) has exactly one disposition above — ✔ see `09` exhaustive table and validation report counts.
2. Every retained/moved file appears at its final destination — ✔ tree §1.
3. Every NEW file has a stated responsibility — ✔ §4.
4. Every deletion has evidence — ✔ §3.
5. No file listed twice under different final paths — ✔ (checked: SpreadsheetViewer, UpdateBanner, bindings, models each appear once; dissolved partials appear only in §2.1 sources).
6. Every XAML file has code-behind or documented reason — ✔ all paired; code-only by design: `FallbackViewer.cs`, `DocumentInfoDialog.cs`, `TranslationDialog.cs`, `TranslationModelsDialog.cs` (existing code-only pattern); resource dictionaries have no code-behind by definition.
7. Project/resource/test/native-build relationships valid — ✔ single csproj globs folders; native `Content` items unchanged; only 2 XAML URIs change (`SpreadsheetViewer` in `ViewerPage.xaml`, `UpdateBanner` in `TitleBarWindow.xaml`).
8. Tree agrees with 04 (naming), 08 (dependency), 09 (map), 10 (roadmap) — ✔ reconciled this pass; deviations logged as AF-xx.
9. No placeholders hide files — ✔ Assets enumerated by explicit inventory (§1); `vendor/` labeled third-party per scope rules; `installer/winget|output` labeled.
10. Feasible in existing structure — ✔ no new projects; folder moves only; Rust untouched.
