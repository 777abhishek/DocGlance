# tree.md — The Complete, Authoritative Final Repository Tree (re-audit 2026-10-16)

**This file is the single source of truth for the repository structure after the architecture refactoring.** Docs `01–10` provide rationale; where any document disagrees with this tree, this tree wins. Deviations and evidence live in `architecture-review-findings.md` (register `RA-xx`; superseded register `AF-xx` from the 2026-10-16 pre-implementation audit is carried forward where still relevant).

Scope decisions fixed by this re-audit:
- **Rust is intact** — no crate, export, prefix, lockfile, or vendored file changes. The only native diff since the baseline commit `25d7fa1` is a test temp-dir string rename in `translation_ffi` (`quickview-translation-test-*` → `DocGlance-translation-test-*`, commit `10db6a8`) — no ABI impact (RA-08).
- One app csproj + one test csproj. No new projects, no DI container.
- **Implementation status:** Phases 1–4 of the original roadmap and half of Phase 5 shipped in commit `10db6a8`. This tree describes the *remaining delta*; what is already at its final path is marked `[FINAL]`.

Counting rules: `[FINAL]` = already at final path with final responsibility; no further structural work. `[EDIT]` = path is final; in-place scope/content work still pending (named). `[SPLIT]` = file survives at this path with reduced scope while named parts move out. `[DISSOLVE]` = file ceases to exist; every responsibility moves to named destinations. `[MERGE]` = absorbed. `[MOVE]` = relocates (possibly renamed), responsibility intact. `[DELETE]` = removed (see deletion ledger §3). `[NEW]` = did not exist at re-audit time.

---

## 1. THE FINAL TREE

```text
DocGlance/
│
│   ── Root ─────────────────────────────────────────────────────────────
├── DocGlance.csproj                     [FINAL]   native-DLL Content items, tools/tests excludes
├── Program.cs                           [FINAL]   232 LOC — DPI awareness, single-instance redirect
├── App.xaml                             [FINAL]   22 LOC — merged resource dictionaries
├── App.xaml.cs                          [FINAL]   208 LOC — wiring + exception policy only;
│                                                  activation/session logic lives in Services (done)
├── Package.appxmanifest                 [FINAL]   MSIX identity + file associations
├── app.manifest                         [FINAL]   PerMonitorV2 DPI
├── .gitignore                           [FINAL]   whitelists docs/architecture/ + offline-translation.md
├── DocGlance.cer                        [FINAL]   public signing cert (git-tracked; used by installer)
├── build.ps1                            [FINAL]   managed build/publish
├── build-installer.ps1                  [FINAL]   MSIX + installer pipeline
├── README.md                            [FINAL]   project README (added in 10db6a8)
├── LICENSE                              [FINAL]   MIT (added in 10db6a8)
│
│   ── Pure helpers ────────────────────────────────────────────────────
├── Common/
│   ├── Formatters/
│   │   ├── DateFormatter.cs             [FINAL]   relative/absolute date formatting (106 LOC)
│   │   └── FileSizeFormatter.cs         [FINAL]   byte-size formatting (86 LOC)
│   ├── Utilities/
│   │   └── FileTypeDetector.cs          [FINAL]   extension → FileCategory mapping (168 LOC)
│   └── Validators/
│       ├── FileSizeValidator.cs         [FINAL]   open-request size/safety validation (101 LOC)
│       └── PathValidator.cs             [FINAL]   path safety validation (134 LOC)
│
│   ── Format-neutral core ─────────────────────────────────────────────
├── Core/
│   ├── Constants/
│   │   ├── FileExtensions.cs            [FINAL]   extension literals (24 LOC)
│   │   └── SupportedExtensions.cs       [FINAL]   canonical open-with list (47 LOC)
│   ├── Enums/
│   │   ├── FileCategory.cs              [FINAL]
│   │   ├── ViewMode.cs                  [FINAL]
│   │   └── FitMode.cs                   [FINAL]   extracted from ViewerPage.Zoom.cs (done in 10db6a8)
│   ├── Interfaces/
│   │   └── IThumbnailProvider.cs        [FINAL]   thumbnail provider contract (4 implementations)
│   └── Models/
│       ├── DocumentItem.cs              [FINAL]   openable file descriptor (113 LOC)
│       ├── DocumentGroup.cs             [FINAL]   Home grouping
│       ├── OutlineItem.cs               [FINAL]   outline/TOC node (Pdf, Docx, Pptx)
│       ├── BookmarkItem.cs              [FINAL]   user bookmark
│       └── PageThumbnailItem.cs         [FINAL]   sidebar thumbnail VM
│
│   ── Cross-cutting infrastructure ────────────────────────────────────
├── Infrastructure/
│   ├── Concurrency/NativeCallGate.cs    [FINAL]   serialize native calls vs dispose (93 LOC)
│   ├── Configuration/AppSettings.cs     [FINAL]   typed settings facade + SettingsChanged (176 LOC)
│   ├── Diagnostics/DiagnosticsService.cs [FINAL]  milestones/memory checkpoints/env log (109 LOC)
│   ├── Extensions/UIThreadHelper.cs     [FINAL]   moved out of Core in 10db6a8 (151 LOC)
│   └── Logging/
│       ├── Logger.cs                    [FINAL]   queued file logger + operation scopes (338 LOC)
│       ├── LogFileWriter.cs             [FINAL]   batched file writer
│       ├── LogScope.cs                  [FINAL]   operation correlation
│       └── TaskExtensions.cs            [FINAL]   SafeFireAndForget
│
│   ── FFI boundary (one engine per folder, LibraryImport) ─────────────
├── Interop/
│   ├── README.md                        [FINAL]   ABI table: exports, structs, Rust-allocates/C#-frees
│   ├── PdfCraft/PdfCraftNative.cs       [FINAL]   pc_* ×27; 25×DllImport→[LibraryImport] done
│   ├── WordCraft/WordCraftNative.cs     [FINAL]   wc_* ×14
│   ├── GridCraft/GridCraftNative.cs     [FINAL]   gc_* ×11
│   ├── DeckCraft/DeckCraftNative.cs     [FINAL]   dc_* ×12
│   └── Translation/TranslationNative.cs [FINAL]   qv_* ×5 (renamed from TranslationEngineNative.cs)
│
│   ── Rendering engines, one folder per format ────────────────────────
├── Rendering/
│   ├── Pdf/
│   │   ├── IPdfViewerService.cs         [FINAL]   PDF orchestration contract (viewer + Tools)
│   │   ├── PdfViewerService.cs          [FINAL]   lifecycle, passwords, progressive render (283 LOC)
│   │   ├── PdfLoadResult.cs             [FINAL]   load outcome
│   │   ├── PdfRenderer.cs               [FINAL]   safe wrapper over PdfCraftNative (256 LOC)
│   │   ├── PdfDocumentState.cs          [FINAL]   owns PcDocument* + page bookkeeping (131 LOC)
│   │   ├── PdfTextService.cs            [FINAL]   text extract, char maps, hit-test (233 LOC)
│   │   ├── PdfAnnotationService.cs      [FINAL]   annotation CRUD + history via FFI (529 LOC)
│   │   └── Models/
│   │       ├── PdfMetadata.cs           [FINAL]   (moved out of Core in 10db6a8)
│   │       ├── PdfPageTextMap.cs        [FINAL]
│   │       ├── PdfSearchResult.cs       [FINAL]
│   │       ├── PdfViewportState.cs      [FINAL]
│   │       ├── PdfLink.cs               [FINAL]
│   │       └── PdfPermissions.cs        [FINAL]
│   ├── Docx/DocxDocument.cs             [FINAL]   DOCX engine: open/render/text/search/export (538 LOC)
│   ├── Xlsx/
│   │   ├── SpreadsheetDocument.cs       [FINAL]   XLSX engine: open/sheets/export-pdf (288 LOC)
│   │   └── SheetData.cs                 [FINAL]   managed sheet model (from gc JSON)
│   ├── Pptx/PptxDocument.cs             [FINAL]   PPTX engine: open/slides/text/search (364 LOC)
│   └── Shared/
│       ├── NativeBitmap.cs              [FINAL]   RGBA→WriteableBitmap on UI dispatcher (149 LOC)
│       ├── RenderBudget.cs              [FINAL]   page-cache memory budget (87 LOC)
│       └── TextFileReader.cs            [FINAL]   capped encoding-aware text read (39 LOC)
│
│   ── Features (pages + viewers) ──────────────────────────────────────
├── Features/
│   ├── Viewer/
│   │   │   # ── Shell ──
│   │   ├── ViewerPage.xaml              [EDIT]    host layout final scope: one ContentControl per
│   │   │                                          registered viewer + overlays; shared PDF canvas
│   │   │                                          folds into PdfViewer when Phase 1 lands
│   │   ├── ViewerPage.xaml.cs           [SPLIT]   352 → ~300 LOC coordinator: registry-first dispatch,
│   │   │                                          lifecycle, generic loading/error states, public API;
│   │   │                                          absorbs LoadDocumentAsync/CoreAsync + Helpers.cs (22);
│   │   │                                          drops _page/_zoom/_total/_rotation mirrors (RA-05/06)
│   │   ├── ViewerViewModel.cs           [EDIT]    131 → sole owner of page/zoom/rotation/fit/sidebar
│   │   │                                          state; SyncToViewModel() and its 8 call sites removed
│   │   ├── DocumentViewerRegistry.cs    [FINAL]   ext→factory priority map, always resolves (58 LOC)
│   │   ├── Partials/
│   │   │   ├── ViewerPage.Toolbar.cs        [FINAL]     230 LOC toolbar/annotation event wiring
│   │   │   ├── ViewerPage.Sidebar.cs        [SPLIT]     504 → ~250: hosting + bookmark/notes stay;
│   │   │   │                                            LoadDocx/LoadPptxOutlineAsync → viewers
│   │   │   │                                            (ISupportsOutline) in Phase 2
│   │   │   ├── ViewerPage.Search.cs         [SPLIT]     534 → ~250: panel + ISupportsSearch path stay;
│   │   │   │                                            PDF/DOCX/PPTX highlight+scroll parts → viewers
│   │   │   ├── ViewerPage.Zoom.cs           [SPLIT]     302 → ~120: fit/zoom coordination stays;
│   │   │   │                                            ApplyFitWidth/ApplyFitPage math → PdfViewer
│   │   │   ├── ViewerPage.Viewport.cs       [SPLIT]     642 → ~100: sizing/debounce stay;
│   │   │   │                                            RenderPagesAsync + page-cache/budget → PdfViewer
│   │   │   ├── ViewerPage.KeyboardShortcuts.cs [FINAL]  105 LOC shared key routing
│   │   │   └── ViewerPage.Translator.cs     [FINAL]     55 LOC translation dialog launch
│   │   ├── Contracts/                            (8 files, all FINAL)
│   │   │   ├── IDocumentViewer.cs            [FINAL]  Control, LoadAsync, CleanupAsync — mandatory
│   │   │   ├── IDocumentViewerFactory.cs     [FINAL]  SupportedExtensions, Create(ViewerViewModel)
│   │   │   ├── ISupportsZoom.cs              [FINAL]  implemented by SpreadsheetViewer; Pdf in Phase 1
│   │   │   ├── ISupportsSearch.cs            [FINAL]  + SearchResult/SearchLocation records
│   │   │   ├── ISupportsTextSelection.cs     [FINAL]  implemented by Text + Spreadsheet
│   │   │   ├── ISupportsOutline.cs           [FINAL]  no implementer until Phase 2 (wired then)
│   │   │   ├── ISupportsAnnotations.cs       [FINAL]  implemented by PdfViewer in Phase 1
│   │   │   └── ISupportsThumbnails.cs        [FINAL]  implemented by Pdf/Docx/Pptx viewers
│   │   ├── Components/
│   │   │   ├── ViewerToolbar.xaml            [EDIT]   345 LOC shared toolbar visual; per-format
│   │   │   │                                          conditionals drop as viewers register capabilities
│   │   │   ├── ViewerToolbar.xaml.cs         [EDIT]   193 LOC; imperative proxies (UpdatePageInfo,
│   │   │   │                                          SetMarkupToolActive…) → ViewModel bindings;
│   │   │   │                                          hosts PdfAnnotationTool enum (accepted, RA-03)
│   │   │   ├── ViewerToolbarIcons.xaml       [FINAL]  33 LOC centralized toolbar glyph resources
│   │   │   ├── PdfMarkupToolbar.xaml         [FINAL]  160 LOC PDF markup tool strip (extracted 10db6a8)
│   │   │   ├── PdfMarkupToolbar.xaml.cs      [FINAL]  100 LOC tool state/color/opacity/undo-redo events
│   │   │   ├── SearchHighlighter.cs          [FINAL]  340 LOC text-run highlight helper (Text + viewers)
│   │   │   ├── DocumentInfoDialog.cs         [FINAL]  123 LOC metadata dialog (code-only)
│   │   │   ├── TranslationDialog.cs          [FINAL]  329 LOC translate dialog (code-only)
│   │   │   ├── TranslationModelsDialog.cs    [FINAL]  332 LOC model picker dialog (code-only)
│   │   │   └── ViewerPopupStyle.cs           [FINAL]  49 LOC shared popup styling
│   │   └── Formats/                          (one format, one folder)
│   │       ├── Pdf/                          [NEW folder — Phase 1]
│   │       │   ├── PdfViewer.xaml            [NEW]    PDF page stack: ScrollViewer + per-page canvases
│   │       │   │                                      (viewport image, selection, annotation, highlight)
│   │       │   ├── PdfViewer.xaml.cs         [NEW]    load/password/progressive render/prefetch/thumbnails;
│   │       │   │                                      from ViewerPage.Pdf.cs PDF block + Viewport.cs PDF
│   │       │   │                                      loop + Zoom.cs fit math; implements IDocumentViewer,
│   │       │   │                                      ISupportsZoom, ISupportsSearch, ISupportsTextSelection,
│   │       │   │                                      ISupportsAnnotations, ISupportsThumbnails,
│   │       │   │                                      ISupportsOutline
│   │       │   ├── PdfSelectionLayer.cs      [NEW]    730 LOC selection rects/hit-test/copy
│   │       │   │                                      ← ViewerPage.PdfTextSelection.cs (dissolves)
│   │       │   └── PdfAnnotationLayer.cs     [NEW]    859 LOC markup tools/history/deferred flush
│   │       │                                          ← ViewerPage.PdfAnnotations.cs (dissolves)
│   │       ├── Docx/                         [NEW folder — Phase 2]
│   │       │   ├── DocxViewer.xaml           [NEW]    paginated DOCX canvas
│   │       │   └── DocxViewer.xaml.cs        [NEW]    load/render-viewport/thumbs/outline/selection;
│   │       │                                          from ViewerPage.Pdf.cs DOCX block (985–1365),
│   │       │                                          OfficeTextSelection docx parts, Sidebar outline
│   │       │                                          docx part; implements IDocumentViewer,
│   │       │                                          ISupportsSearch, ISupportsTextSelection,
│   │       │                                          ISupportsOutline, ISupportsThumbnails
│   │       ├── Pptx/
│   │       │   ├── PptxViewer.xaml           [FINAL]  94 LOC (moved+renamed from Rendering/Presentation)
│   │       │   └── PptxViewer.xaml.cs        [EDIT]   179 → ~450: control exists but is UNWIRED (RA-04);
│   │       │                                          Phase 2 wires it in and absorbs the shell's parallel
│   │       │                                          slide loop (ViewerPage.Pdf.cs PPTX block 438–919),
│   │       │                                          Presentation.cs presentation parts, OfficeTextSelection
│   │       │                                          pptx parts, Sidebar pptx outline
│   │       ├── Spreadsheet/
│   │       │   ├── SpreadsheetViewer.xaml    [FINAL]  97 LOC (moved from Features/Viewer root)
│   │       │   └── SpreadsheetViewer.xaml.cs [FINAL]  1,382 LOC Win2D grid + SpreadsheetViewerFactory
│   │       │                                          (.xlsx/.xlsm/.xltx/.xltm); implements IDocumentViewer,
│   │       │                                          ISupportsZoom, ISupportsSearch, ISupportsTextSelection
│   │       ├── Text/
│   │       │   ├── TextViewer.xaml           [FINAL]  45 LOC
│   │       │   └── TextViewer.xaml.cs        [FINAL]  186 LOC + TextViewerFactory (75 text/code exts)
│   │       └── Fallback/
│   │           └── FallbackViewer.cs         [FINAL]  115 LOC info card + FallbackViewerFactory claiming
│   │                                                    .xls (pinned by DocumentViewerRegistryTests);
│   │                                                    unknown exts fall through Resolve() to fallback
│   ├── Home/
│   │   ├── HomePage.xaml                 [FINAL]  652 LOC
│   │   ├── HomePage.xaml.cs              [FINAL]  480 LOC recents/all-docs
│   │   └── HomeViewModel.cs              [FINAL]  287 LOC
│   ├── Tools/
│   │   ├── ToolsPage.xaml                [FINAL]  591 LOC
│   │   └── ToolsPage.xaml.cs             [FINAL]  1,176 LOC convert/merge/split — calls engines
│   │                                            directly (Rendering.{Docx,Pptx,Xlsx,Pdf}; sanctioned)
│   └── Settings/
│       ├── SettingsPage.xaml             [FINAL]  529 LOC
│       └── SettingsPage.xaml.cs          [FINAL]  532 LOC
│
│   ── App services ─────────────────────────────────────────────────────
├── Services/
│   ├── Activation/
│   │   ├── ActivationIpc.cs              [FINAL]  159 LOC named-pipe handover server
│   │   ├── ActivationPaths.cs            [FINAL]  157 LOC activation-arg decoding
│   │   └── ActivationService.cs          [FINAL]  155 LOC TryOpenFromActivationArgs/TryOpenFromCommandLine/
│   │                                              OpenExternalFiles (extracted from App.xaml.cs)
│   ├── FileSystem/
│   │   ├── Index/
│   │   │   ├── FileIndexService.cs       [FINAL]  129 LOC SQLite index facade
│   │   │   ├── FileIndexContext.cs       [FINAL]  56 LOC EF Core context
│   │   │   └── FileIndexRepository.cs    [FINAL]  54 LOC queries
│   │   └── Scanner/FolderScanner.cs      [FINAL]  134 LOC async folder enumeration
│   ├── Input/
│   │   ├── GlobalHotkeyRegistration.cs   [FINAL]  182 LOC RegisterHotKey wrapper (Win32 P/Invoke — sanctioned)
│   │   └── QuickLookHotkeyService.cs     [FINAL]  231 LOC register/release/owner arbitration/
│   │                                              Explorer-selection fetch (extracted from TitleBarWindow)
│   ├── Registration/
│   │   ├── AppRegistration.cs            [FINAL]  270 LOC HKCU file associations (unpackaged) / shim (packaged)
│   │   └── RegistrationHelper.cs         [FINAL]  145 LOC startup ensure-registered
│   ├── Session/SessionService.cs         [FINAL]  119 LOC SaveSession/RestoreSession via SessionStateStore;
│   │                                              TitleBarWindow keeps 2-line call-throughs
│   ├── Storage/
│   │   ├── SettingsStore.cs              [FINAL]  110 LOC JSON per-key persistence
│   │   ├── RecentFilesStore.cs           [FINAL]  32 LOC
│   │   ├── BookmarkStore.cs              [FINAL]  108 LOC
│   │   └── SessionStateStore.cs          [FINAL]  42 LOC
│   ├── Thumbnails/
│   │   ├── ThumbnailService.cs           [FINAL]  108 LOC provider dispatch
│   │   ├── ThumbnailCache.cs             [FINAL]  197 LOC disk cache, size-bounded
│   │   └── Providers/
│   │       ├── GenericThumbnailProvider.cs  [FINAL]  31 LOC — IThumbnailProvider
│   │       ├── OfficeThumbnailProvider.cs   [FINAL]  31 LOC — IThumbnailProvider
│   │       └── PdfThumbnailProvider.cs      [FINAL]  58 LOC — IThumbnailProvider
│   ├── Translation/
│   │   ├── ITranslationService.cs        [FINAL]  20 LOC translate contract
│   │   ├── TranslationService.cs         [FINAL]  120 LOC orchestrates detect/translate via TranslationNative
│   │   ├── TranslationModelManager.cs    [FINAL]  472 LOC model download/deploy/release
│   │   ├── TranslationModelCatalog.cs    [FINAL]  216 LOC catalog metadata
│   │   ├── TranslationLanguage.cs        [FINAL]  115 LOC language model
│   │   ├── TranslationSettings.cs        [FINAL]  45 LOC per-feature settings (documented pattern)
│   │   └── TranslationTextChunker.cs     [FINAL]  94 LOC chunking
│   └── Updates/
│       └── UpdateService.cs              [FINAL]  600 LOC release check + wintrust verification
│
│   ── Shared UI ────────────────────────────────────────────────────────
├── UI/
│   ├── Shell/
│   │   ├── TitleBarWindow.xaml           [FINAL]  120 LOC shell layout (UpdateBanner URI updates with MOVE)
│   │   └── TitleBarWindow.xaml.cs        [FINAL]  1,052 LOC tabs/tear-out/drop/picker/chrome/presentation;
│   │                                              session + hotkey are call-throughs (revised target, RA-15)
│   ├── Controls/
│   │   ├── DocumentCard.xaml             [FINAL]  213 LOC Home document card
│   │   ├── DocumentCard.xaml.cs          [FINAL]  214 LOC
│   │   ├── UpdateBanner.xaml             [MOVE ← Services/Updates]  24 LOC — UI out of Services (Phase 3)
│   │   └── UpdateBanner.xaml.cs          [MOVE ← Services/Updates]  77 LOC — + TitleBarWindow.xaml URI update
│   ├── Icons/FileTypeIcons.xaml          [FINAL]  20 LOC file-type icon resources
│   ├── Templates/FileTemplates.xaml      [FINAL]  32 LOC file list templates
│   └── Styles/
│       ├── Colors.xaml                   [FINAL]  17 LOC
│       ├── Typography.xaml               [FINAL]  16 LOC
│       └── Controls.xaml                 [FINAL]  54 LOC
│
│   ── Tests (mirror the main tree) ─────────────────────────────────────
├── DocGlance.Tests/                      (17 files present, 2,146 LOC)
│   ├── DocGlance.Tests.csproj            [FINAL]
│   ├── Common/Formatters/DateFormatterTests.cs            [FINAL]  102
│   ├── Common/Formatters/FileSizeFormatterTests.cs        [FINAL]  78
│   ├── Common/Validators/FileSizeValidatorTests.cs        [FINAL]  59
│   ├── Common/Validators/PathValidatorTests.cs            [FINAL]  70
│   ├── Core/Constants/SupportedExtensionsTests.cs         [FINAL]  42
│   ├── Features/Home/HomeViewModelTests.cs                [FINAL]  343
│   ├── Features/Viewer/ViewerViewModelTests.cs            [FINAL]  351 (extends with sole-owner behavior in Phase 3)
│   ├── Features/Viewer/DocumentViewerRegistryTests.cs     [FINAL]  71 — pins .xls→FallbackViewer + fallback-for-unclaimed
│   ├── Infrastructure/Concurrency/NativeCallGateTests.cs  [FINAL]  173
│   ├── Infrastructure/Logging/LoggerTests.cs              [FINAL]  146
│   ├── Rendering/Docx/DocxOutlineTests.cs                 [FINAL]  88
│   ├── Rendering/Pdf/PdfViewerServiceTests.cs             [FINAL]  323
│   ├── Rendering/Pdf/PdfEncryptionProbeTests.cs           [FINAL]  43
│   ├── Rendering/Pdf/Models/PdfPageTextMapTests.cs        [FINAL]  57
│   ├── Services/Translation/LanguageCatalogTests.cs       [FINAL]  39
│   ├── Services/Translation/TranslationModelCatalogTests.cs [FINAL] 109
│   ├── Services/Translation/TranslationTextChunkerTests.cs [FINAL]  52
│   └── Architecture/NamespaceDependencyTests.cs           [NEW]    encodes 08 §1 layer table as assertions (Phase 5)
│
│   ── Native engines: INTACT, listed for completeness ─────────────────
├── native/                               (4,984 LOC Rust; only diff since 25d7fa1 is a test-string rename)
│   ├── build-native.ps1                  [FINAL]  cargo build --release for the 5 crates
│   ├── pdfcraft_ffi/
│   │   ├── Cargo.toml · Cargo.lock       [FINAL]
│   │   ├── src/lib.rs                    [FINAL]  2,184 LOC; 27 pc_* exports (catch_unwind)
│   │   └── vendor/                       [THIRD-PARTY] hayro, hayro-interpret, hayro-syntax,
│   │                                              hayro-jbig2, lopdf (4.1 MB) — do not modify
│   ├── wordcraft_ffi/
│   │   ├── Cargo.toml · Cargo.lock       [FINAL]
│   │   └── src/lib.rs                    [FINAL]  895 LOC; 14 wc_* exports
│   ├── gridcraft_ffi/
│   │   ├── Cargo.toml · Cargo.lock       [FINAL]
│   │   └── src/lib.rs                    [FINAL]  746 LOC; 11 gc_* exports
│   ├── deckcraft_ffi/
│   │   ├── Cargo.toml · Cargo.lock       [FINAL]
│   │   └── src/lib.rs                    [FINAL]  575 LOC; 12 dc_* exports
│   └── translation_ffi/
│       ├── Cargo.toml · Cargo.lock       [FINAL]
│       └── src/lib.rs                    [FINAL]  584 LOC; 5 qv_* exports (prefix stays — carried decision)
│
│   ── Build/dev tools (excluded from app compilation) ──────────────────
├── tools/
│   ├── EmbedIcon/EmbedIcon.cs            [FINAL]  PE icon embedder (+ own csproj)
│   └── imggen/Program.cs                 [FINAL]  asset generator (+ own csproj)
│
│   ── Installer ────────────────────────────────────────────────────────
├── installer/
│   ├── DocGlance.iss                     [FINAL]  Inno Setup script
│   ├── generate-winget-manifest.ps1      [FINAL]
│   ├── assets/                           [FINAL]  installer artwork
│   ├── winget/                           [FINAL]  generated manifests (refreshed by script)
│   └── output/                           [BUILD OUTPUT] local only, not committed
│
│   ── Assets (binary; enumerated inventory + policy) ──────────────────
├── Assets/
│   ├── AppIcon.ico                       [FINAL]  PE/association icon (csproj ApplicationIcon)
│   ├── AppIconNew.png                    [FINAL]  imggen source artifact — not shipped
│   ├── Preview.jpg                       [FINAL]  Store preview
│   ├── BadgeLogo.png + 7 scale variants  [FINAL]  manifest-referenced
│   ├── SmallTile.png + 7 scale variants  [FINAL]
│   ├── SplashScreen.png + 7 scale variants [FINAL]
│   ├── Square150x150Logo / Square310x310Logo / Square44x44Logo /
│   │   Square71x71Logo / StoreLogo / Wide310x150Logo (+scale/targetsize variants)
│   │                                     [FINAL]  all manifest-referenced logo sets
│   ├── FileTypeIcons/                    [FINAL]  24 files = 12 icon pairs (.ico+.png): CSV, Code, Excel,
│   │                                              JSON, LOG, Markdown, PDF, PowerPoint, RichText, Text,
│   │                                              Word, XML (match AppRegistration.GetIconName set)
│   └── Fonts/
│       └── FluentSystemIcons-Regular.ttf [FINAL]  icon font
│   # csproj stale Exclude entries for Assets\windows|SampleMedia|legacy PNGs were
│   # removed in 10db6a8 (supersedes AF-12).
│
│   ── Documentation ────────────────────────────────────────────────────
└── docs/
    ├── architecture/                     [FINAL]  this 14-document set (re-audit 2026-10-16)
    └── offline-translation.md            [DELETE-DECISION] removed at user request in 10db6a8;
        recoverable from git history (`git show 6fa6515:docs/offline-translation.md`);
        .gitignore whitelist line retained in case it is restored
```

Local (untracked) artifacts, not repository content: `DocGlance.pfx`, `build.log`, `bin/`, `obj/`, `native/*/target/`, `installer/output/`, `tools/*/obj/`.

---

## 2. SPLIT / DISSOLVE MAPS (current source → destination)

### 2.1 `ViewerPage` — 13 partials + main + XAML (6,104 + 352 + 501 LOC) → shell ~1,100 + viewers ~2,900

| Source (LOC) | Disposition | Destination(s) |
|---|---|---|
| `ViewerPage.xaml.cs` (352) | SPLIT | keeps coordinator: registry-first dispatch (absorbs `LoadDocumentAsync`/`LoadDocumentCoreAsync` from Pdf.cs 48–157), lifecycle, generic loading/error, public API; absorbs `ViewerPage.Helpers.cs` (22, MERGE); drops `_page/_zoom/_total/_rotation` mirrors + `SyncToViewModel()` when ViewerViewModel takes ownership |
| `ViewerPage.Pdf.cs` (1,365) | DISSOLVE | dispatch 48–157 → `ViewerPage.xaml.cs`; PDF block 158–437 (`LoadPdfAsync`, `ShowPasswordDialogAsync`, `CreatePasswordDialogContent`, `PrefetchThumbnailsAroundAsync`, `RenderThumbnailAsync`) → `Formats/Pdf/PdfViewer.xaml.cs`; PPTX block 438–919 → `Formats/Pptx/PptxViewer.xaml.cs`; XLSX glue 920–984 (`LoadXlsxAsync`) → generic registry host path in `ViewerPage.xaml.cs`; DOCX block 985–1365 → `Formats/Docx/DocxViewer.xaml.cs`; `ShowErrorCard`/`ShowFallbackViewerAsync` → shell (generic) |
| `ViewerPage.PdfTextSelection.cs` (730) | DISSOLVE | → `Formats/Pdf/PdfSelectionLayer.cs` |
| `ViewerPage.PdfAnnotations.cs` (859) | DISSOLVE | → `Formats/Pdf/PdfAnnotationLayer.cs` |
| `ViewerPage.Viewport.cs` (642) | SPLIT | PDF loop (`RenderPagesAsync`, `RenderSinglePageAsync`, page cache, budget, `_pageTopCache`, `GetVisiblePageRange`) → `PdfViewer`; `UpdateCanvasSize`, resize/debounce, `CleanupViewportRendering` host side stays (~100) |
| `ViewerPage.OfficeTextSelection.cs` (516) | DISSOLVE | docx parts (`FindDocxPage`, `AddDocxSelectionOverlay`, docx hit-test) → `DocxViewer`; pptx parts (`GetPptxTextMap`, `AddPptxSelectionOverlay`) → `PptxViewer`; shared drag-select driver moves with each |
| `ViewerPage.Sidebar.cs` (504) | SPLIT | `LoadDocxOutlineAsync` → `DocxViewer`; `LoadPptxOutlineAsync` → `PptxViewer` (via `ISupportsOutline`); tab hosting, thumbnails sync, bookmarks/notes stay (~250) |
| `ViewerPage.Search.cs` (534) | SPLIT | panel, `SearchInViewer(ISupportsSearch)` path, `FindBox_TextChanged` stay (~250); PDF `_searchResults` + `DrawAllHighlights`/`GetPageOffsetInDocument` → `PdfViewer`; `DrawDocxSearchHighlights`/`ScrollToDocxSearchHit` → `DocxViewer`; `_pptxSearchResults`/`DrawPptxSearchHighlights` → `PptxViewer` |
| `ViewerPage.Zoom.cs` (302) | SPLIT | `ApplyFitWidth`/`ApplyFitPage`/zoom math → `PdfViewer` (via `ISupportsZoom`); `SetFitMode`/`SetZoom` coordination + presets stay (~120) |
| `ViewerPage.Presentation.cs` (240) | DISSOLVE | presentation enter/exit/navigate/fit parts → `PptxViewer`; `InitializeViewerShortcuts`/`AddViewerShortcut`/shortcut handlers → `ViewerPage.KeyboardShortcuts.cs` |
| `ViewerPage.Toolbar.cs` (230) | FINAL | annotation/toolbar wiring; becomes capability-driven as viewers register |
| `ViewerPage.KeyboardShortcuts.cs` (105) | FINAL | absorbs shortcut scaffolding from Presentation.cs |
| `ViewerPage.Translator.cs` (55) | FINAL | — |
| `ViewerPage.Helpers.cs` (22) | MERGE | → `ViewerPage.xaml.cs` |
| `ViewerPage.xaml` (501) | EDIT | per-viewer ContentControls; PDF/DOCX canvases move into their viewers; overlays/find/zoom-pill stay |

### 2.2 `App.xaml.cs` (208) — DONE in 10db6a8
Activation → `Services/Activation/ActivationService.cs`; session wiring → `Services/Session/SessionService.cs`. Remaining: wiring + exception policy + `SetTheme`. No further work.

### 2.3 `UI/Shell/TitleBarWindow.xaml.cs` (1,052) — extraction DONE in 10db6a8
`SessionService`/`QuickLookHotkeyService` hold the extracted logic; TitleBarWindow keeps call-throughs (lines 113–130, 1028–1042). Revised target: **~1,050 (final)** — the remaining bulk is tabs, tear-out, drag-drop, picker, presentation chrome, Win32 icon loading, all of which is genuinely shell responsibility (RA-15).

---

## 3. DELETION LEDGER (8 files, all verified zero-consumer by grep 2026-10-16)

| # | Path | LOC | Evidence | Phase |
|---|---|---|---|---|
| 1 | `Core/Interfaces/IFileScanner.cs` | 12 | zero references outside its own file (AF-07) | 4 |
| 2 | `Rendering/Code/CodeRenderer.cs` | 191 | zero consumers; `IsCodeFile() => false`; text extensions route to TextViewer | 4 |
| 3 | `Services/FileSystem/Watcher/FileWatcherService.cs` | 41 | zero consumers | 4 |
| 4 | `Features/Browser/BrowserPage.xaml` | 12 | zero navigation references (stub) | 4 |
| 5 | `Features/Browser/BrowserPage.xaml.cs` | 10 | zero navigation references | 4 |
| 6 | `Features/Browser/BrowserViewModel.cs` | 65 | zero navigation references | 4 |
| 7 | `UI/Controls/PageThumbnailSidebar.xaml` | 127 | zero external consumers; ViewerPage implements thumbnails inline | 4 |
| 8 | `UI/Controls/PageThumbnailSidebar.xaml.cs` | 53 | zero external consumers | 4 |

Already deleted in `10db6a8` (original ledger rows 1–5, verified absent): `Rendering/RendererRegistry.cs`, `Core/Interfaces/IFileRenderer.cs`, `Rendering/Office/{Docx,Xlsx,Pptx}Renderer.cs`, `UI/Controls/ViewerInfoCard.cs`, `ViewerPage.Text.cs`, `ViewerPage.Spreadsheet.cs`.

## 4. NEW-FILE LEDGER (6 app files + 1 test)

| Path | Justification |
|---|---|
| `Formats/Pdf/PdfViewer.xaml` + `.cs` | PDF's own control boundary (SpreadsheetViewer precedent); absorbs 4 shell partials' PDF responsibilities |
| `Formats/Pdf/PdfSelectionLayer.cs` | 730 LOC of PDF-only selection; independent change driver |
| `Formats/Pdf/PdfAnnotationLayer.cs` | 859 LOC of PDF-only annotation UI; independent change driver |
| `Formats/Docx/DocxViewer.xaml` + `.cs` | DOCX boundary; today DOCX rendering lives inside `ViewerPage.Pdf.cs` — the largest misplacement left |
| `DocGlance.Tests/Architecture/NamespaceDependencyTests.cs` | encodes 08 §1 layering as failing tests |

`PptxViewer` is NOT new — it exists but is **unwired** (RA-04); Phase 2 wires it.

## 5. ACCEPTANCE CHECKS (verified against this tree, 2026-10-16)

1. Every current maintained file (150 app + 17 test) has exactly one disposition — ✔ §1 + `09-file-migration-map.md`.
2. Every retained/moved file appears at its final destination — ✔ §1.
3. Every NEW file has a stated responsibility — ✔ §4.
4. Every deletion has evidence — ✔ §3 (grep-verified zero consumers).
5. No file listed twice under different final paths — ✔ (checked: UpdateBanner, bindings, models each appear once; dissolved partials appear only in §2 sources).
6. Every XAML file has code-behind or documented reason — ✔ all paired; code-only by design: `FallbackViewer.cs`, `DocumentInfoDialog.cs`, `TranslationDialog.cs`, `TranslationModelsDialog.cs`; resource dictionaries (`ViewerToolbarIcons.xaml`, styles/icons/templates) have none by definition.
7. Project/resource/test/native-build relationships valid — ✔ single csproj, SDK-style globbing; native `Content` items unchanged; XAML URI updates in scope: `UpdateBanner` in `TitleBarWindow.xaml` only (SpreadsheetViewer URI already fixed in 10db6a8).
8. Tree agrees with 03 (target), 04 (naming), 07 (matrix), 08 (dependency), 09 (map), 10 (roadmap) — ✔ reconciled in this pass.
9. No placeholders hide files — ✔ Assets enumerated; `vendor/` labeled third-party; installer folders labeled.
10. Feasible in existing structure — ✔ no new projects; folder moves only; Rust untouched.
