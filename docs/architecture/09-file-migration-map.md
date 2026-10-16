# 09 — File Migration Map (exhaustive)

Every maintained application source file (139) plus tests, mapped to the final tree in [`tree.md`](tree.md) (authoritative). Contradiction resolutions from [`architecture-review-findings.md`](architecture-review-findings.md) are referenced as AF-xx. Namespace changes apply to every MOVE (new namespace = final folder path); "usings" means consumer `using` updates found by the compiler.

Counting: KEEP 82 · MOVE 29 · SPLIT-in-place 7 · SPLIT→dissolve 7 · MERGE 1 · DELETE 13 · NEW 21 = final 139 app code files (coincidentally equal to current).

## 1. Root & Properties

| Current | Final | Action | Notes / consumers | Conf. |
|---|---|---|---|---|
| `DocGlance.csproj` | same | KEEP | optional: drop stale `Assets\windows`/`SampleMedia`/legacy-PNG `Exclude` entries (AF-12) | High |
| `Program.cs` | same | KEEP | — | High |
| `App.xaml` | same | KEEP | — | High |
| `App.xaml.cs` | same (→~180 LOC) | SPLIT-in-place | activation/cmdline/error logic → `ActivationService`; `RegisterRenderers` deleted with pipeline; usings: none external | High |
| `Properties/AssemblyInfo.cs` | same | KEEP | — | High |
| `Package.appxmanifest`, `app.manifest`, `.gitignore`, `DocGlance.cer`, `build.ps1`, `build-installer.ps1` | same | KEEP | — | High |

## 2. Common (5 — all KEEP)

`Formatters/DateFormatter.cs`, `Formatters/FileSizeFormatter.cs`, `Utilities/FileTypeDetector.cs`, `Validators/FileSizeValidator.cs`, `Validators/PathValidator.cs` — unchanged; tests already mirror.

## 3. Core (19 current → 11 final)

| Current | Final | Action | Consumers to update | Conf. |
|---|---|---|---|---|
| `Constants/FileExtensions.cs` | same | KEEP | — | High |
| `Constants/SupportedExtensions.cs` | same | KEEP | — | High |
| `Enums/FileCategory.cs` | same | KEEP | — | High |
| `Enums/ViewMode.cs` | same | KEEP | — | High |
| — | `Enums/FitMode.cs` | NEW | extracted from `ViewerPage.Zoom.cs` | — |
| `Interfaces/IFileScanner.cs` | — | DELETE (AF-07) | zero consumers | High |
| `Interfaces/IThumbnailProvider.cs` | same | KEEP | ThumbnailService + 3 providers | High |
| `Interfaces/IFileRenderer.cs` | — | DELETE | Office renderers + registry only | High |
| `Models/DocumentItem.cs` | same | KEEP | — | High |
| `Models/DocumentGroup.cs` | same | KEEP | — | High |
| `Models/OutlineItem.cs` | same | KEEP | — | High |
| `Models/BookmarkItem.cs` | same | KEEP | — | High |
| `Models/PageThumbnailItem.cs` | same | KEEP | — | High |
| `Models/PdfMetadata.cs` | `Rendering/Pdf/Models/` | MOVE | usings in Pdf partials/services (compiler-driven) | High |
| `Models/PdfPageTextMap.cs` | `Rendering/Pdf/Models/` | MOVE | `PdfTextService`, `PdfViewerService`, test moves with it | High |
| `Models/PdfSearchResult.cs` | `Rendering/Pdf/Models/` | MOVE | search partial → PdfViewer | High |
| `Models/PdfViewportState.cs` | `Rendering/Pdf/Models/` | MOVE | viewport partial | High |
| `Models/PdfLink.cs` | `Rendering/Pdf/Models/` | MOVE | PDF path only | High |
| `Models/PdfPermissions.cs` | `Rendering/Pdf/Models/` | MOVE | PDF path only | High |

## 4. Infrastructure (7 → 8)

All 7 current files KEEP (`Concurrency/NativeCallGate.cs`, `Configuration/AppSettings.cs`, `Diagnostics/DiagnosticsService.cs`, `Logging/Logger.cs`, `LogFileWriter.cs`, `LogScope.cs`, `TaskExtensions.cs`). `Core/Extensions/UIThreadHelper.cs` MOVES in as `Extensions/UIThreadHelper.cs` (usings at call sites).

## 5. Interop (NEW folder; 5 MOVE-ins + 1 NEW README)

| Current | Final | Action | Consumers | Conf. |
|---|---|---|---|---|
| `Rendering/Pdf/PdfCraftNative.cs` | `Interop/PdfCraft/` | MOVE + `DllImport`→`LibraryImport` | `PdfRenderer` only | High |
| `Rendering/Document/WordCraftNative.cs` | `Interop/WordCraft/` | MOVE | `DocxDocument` | High |
| `Rendering/Document/GridCraftNative.cs` | `Interop/GridCraft/` | MOVE | `SpreadsheetDocument` | High |
| `Rendering/Presentation/DeckCraftNative.cs` | `Interop/DeckCraft/` | MOVE | `PptxDocument` | High |
| `Services/Translation/TranslationEngineNative.cs` | `Interop/Translation/TranslationNative.cs` | MOVE+RENAME | `TranslationService` (permanent sanctioned exception, 08 §1 / AF-05) | High |
| — | `Interop/README.md` | NEW | ABI + ownership contract table | — |

## 6. Rendering (22 current → 20 final)

| Current | Final | Action | Consumers | Conf. |
|---|---|---|---|---|
| `Pdf/PdfRenderer.cs` | same | KEEP | — | High |
| `Pdf/PdfDocumentState.cs` | same | KEEP | — | High |
| `Pdf/PdfTextService.cs` | same | KEEP | — | High |
| `Pdf/PdfAnnotationService.cs` | same | KEEP | — | High |
| `Document/DocxDocument.cs` | `Rendering/Docx/` | MOVE | ViewerPage partials→DocxViewer, ToolsPage, tests | High |
| `Document/SpreadsheetDocument.cs` | `Rendering/Xlsx/` | MOVE | SpreadsheetViewer, ToolsPage, shell | High |
| `Document/SheetData.cs` | `Rendering/Xlsx/` | MOVE | SpreadsheetViewer | High |
| `Presentation/PptxDocument.cs` | `Rendering/Pptx/` | MOVE | partials→PptxViewer, ToolsPage | High |
| `Presentation/PptxViewerControl.xaml` | `Features/Viewer/Viewers/Pptx/PptxViewer.xaml` | MOVE+RENAME+EXTEND | absorbs slide loop; URI none (declared in code) | Med |
| `Presentation/PptxViewerControl.xaml.cs` | `Features/Viewer/Viewers/Pptx/PptxViewer.xaml.cs` | MOVE+RENAME+EXTEND | — | Med |
| `NativeBitmap.cs` | `Rendering/Shared/` | MOVE | renderers/services | High |
| `RenderBudget.cs` | `Rendering/Shared/` | MOVE | PdfViewerService | High |
| `Text/TextFileReader.cs` | `Rendering/Shared/` | MOVE | text partial→TextViewer | High |
| `RendererRegistry.cs` | — | DELETE (Phase 5) | fallback branch + App | High |
| `Office/DocxRenderer.cs` | — | DELETE (Phase 5) | App.RegisterRenderers | High |
| `Office/XlsxRenderer.cs` | — | DELETE (Phase 5) | App.RegisterRenderers; `.xls` → FallbackViewer (AF-06) | High |
| `Office/PptxRenderer.cs` | — | DELETE (Phase 5) | none | High |
| `Code/CodeRenderer.cs` | — | DELETE (Phase 7, REVIEW) | zero consumers; `IsCodeFile()=>false` | Med |

Plus MOVE-ins: `Services/Documents/{IPdfViewerService,PdfViewerService,PdfLoadResult}.cs` → `Rendering/Pdf/` (consumers: shell→PdfViewer, ToolsPage, tests move) and 6 `Core/Models/Pdf*.cs` → `Rendering/Pdf/Models/`.

## 7. Features (37 current → 46 final)

### Viewer (27 current)
| Current | Final | Action | Conf. |
|---|---|---|---|
| `ViewerPage.xaml` | same | KEEP (element decl for SpreadsheetViewer URI updates on move) | High |
| `ViewerPage.xaml.cs` | same → ~250 LOC | SPLIT-in-place (map: tree §2.1) | High |
| `ViewerViewModel.cs` | same, grows sole-state-owner | KEEP | High |
| `Partials/ViewerPage.Toolbar.cs` | same | KEEP | High |
| `Partials/ViewerPage.KeyboardShortcuts.cs` | same | KEEP | High |
| `Partials/ViewerPage.Translator.cs` | same | KEEP (AF-08) | High |
| `Partials/ViewerPage.Zoom.cs` | same → ~150 | SPLIT-in-place | High |
| `Partials/ViewerPage.Sidebar.cs` | same → ~200 | SPLIT-in-place | High |
| `Partials/ViewerPage.Search.cs` | same → ~200 | SPLIT-in-place | High |
| `Partials/ViewerPage.Viewport.cs` | same → ~150 | SPLIT-in-place | High |
| `Partials/ViewerPage.Pdf.cs` | — (dissolve) | SPLIT→dissolve to registry + 4 viewers | High |
| `Partials/ViewerPage.PdfAnnotations.cs` | — | SPLIT→dissolve → `PdfAnnotationLayer.cs` | High |
| `Partials/ViewerPage.PdfTextSelection.cs` | — | SPLIT→dissolve → `PdfSelectionLayer.cs` | High |
| `Partials/ViewerPage.OfficeTextSelection.cs` | — | SPLIT→dissolve → DocxViewer + PptxViewer | High |
| `Partials/ViewerPage.Text.cs` | — | SPLIT→dissolve → `TextViewer` | High |
| `Partials/ViewerPage.Presentation.cs` | — | SPLIT→dissolve → `PptxViewer` | High |
| `Partials/ViewerPage.Spreadsheet.cs` | — | SPLIT→dissolve → construction glue in main | High |
| `Partials/ViewerPage.Helpers.cs` | — | MERGE into `ViewerPage.xaml.cs` | High |
| `Components/ViewerToolbar.xaml` + `.xaml.cs` | same | KEEP ×2 | High |
| `Components/SearchHighlighter.cs` | same | KEEP | High |
| `Components/DocumentInfoDialog.cs` | same | KEEP | High |
| `Components/TranslationDialog.cs` | same | KEEP | High |
| `Components/TranslationModelsDialog.cs` | same | KEEP | High |
| `Components/ViewerPopupStyle.cs` | same | KEEP | High |
| `Spreadsheet/SpreadsheetViewer.xaml` + `.xaml.cs` | `Viewers/Spreadsheet/` | MOVE ×2 (URI in `ViewerPage.xaml`; wiring in main ctor) | High |

NEW in Viewer: `DocumentViewerRegistry.cs`, `Contracts/` ×8, `Viewers/Pdf/{PdfViewer.xaml,.cs,PdfSelectionLayer.cs,PdfAnnotationLayer.cs}`, `Viewers/Docx/DocxViewer.xaml`+`.cs`, `Viewers/Text/TextViewer.xaml`+`.cs` (11 code NEW). MOVE-ins: `PptxViewerControl` pair (Rendering), `ViewerInfoCard.cs` (UI).

### Home / Tools / Settings / Browser
| Current | Final | Action | Conf. |
|---|---|---|---|
| `Home/HomePage.xaml` + `.xaml.cs` + `HomeViewModel.cs` | same | KEEP ×3 | High |
| `Tools/ToolsPage.xaml` + `.xaml.cs` | same | KEEP ×2 (usings for moved engine namespaces) | High |
| `Settings/SettingsPage.xaml` + `.xaml.cs` | same | KEEP ×2 | High |
| `Browser/BrowserPage.xaml` | — | DELETE (Phase 7, REVIEW) | Med |
| `Browser/BrowserPage.xaml.cs` | — | DELETE (Phase 7, REVIEW) | Med |
| `Browser/BrowserViewModel.cs` | — | DELETE (Phase 7, REVIEW) | Med |

## 8. Services (33 current → 29 final)

| Current | Final | Action | Conf. |
|---|---|---|---|
| `Activation/ActivationIpc.cs` | same | KEEP | High |
| `Activation/ActivationPaths.cs` | same | KEEP | High |
| — | `Activation/ActivationService.cs` | NEW←App split | — |
| `FileSystem/Index/FileIndexService.cs` | same | KEEP | High |
| `FileSystem/Index/FileIndexContext.cs` | same | KEEP | High |
| `FileSystem/Index/FileIndexRepository.cs` | same | KEEP | High |
| `FileSystem/Scanner/FolderScanner.cs` | same | KEEP | High |
| `FileSystem/Watcher/FileWatcherService.cs` | — | DELETE (Phase 7, REVIEW; zero consumers) | Med |
| `Input/GlobalHotkeyRegistration.cs` | same | KEEP | High |
| — | `Input/QuickLookHotkeyService.cs` | NEW←TitleBarWindow split | — |
| `Registration/AppRegistration.cs` | same | KEEP (icon map stays, AF-11) | High |
| `Registration/RegistrationHelper.cs` | same | KEEP | High |
| — | `Session/SessionService.cs` | NEW←TitleBarWindow split | — |
| `Storage/SettingsStore.cs` | same | KEEP | High |
| `Storage/RecentFilesStore.cs` | same | KEEP | High |
| `Storage/BookmarkStore.cs` | same | KEEP | High |
| `Storage/SessionStateStore.cs` | same | KEEP (consumed by SessionService) | High |
| `Thumbnails/ThumbnailService.cs` | same | KEEP | High |
| `Thumbnails/ThumbnailCache.cs` | same | KEEP | High |
| `Thumbnails/Providers/GenericThumbnailProvider.cs` | same | KEEP | High |
| `Thumbnails/Providers/OfficeThumbnailProvider.cs` | same | KEEP | High |
| `Thumbnails/Providers/PdfThumbnailProvider.cs` | same | KEEP | High |
| `Translation/ITranslationService.cs` | same | KEEP | High |
| `Translation/TranslationService.cs` | same | KEEP (repoints to `TranslationNative`) | High |
| `Translation/TranslationModelManager.cs` | same | KEEP | High |
| `Translation/TranslationModelCatalog.cs` | same | KEEP | High |
| `Translation/TranslationLanguage.cs` | same | KEEP | High |
| `Translation/TranslationSettings.cs` | same | KEEP | High |
| `Translation/TranslationTextChunker.cs` | same | KEEP | High |
| `Translation/TranslationEngineNative.cs` | `Interop/Translation/TranslationNative.cs` | MOVE+RENAME | High |
| `Documents/IPdfViewerService.cs` | `Rendering/Pdf/` | MOVE | High |
| `Documents/PdfViewerService.cs` | `Rendering/Pdf/` | MOVE | High |
| `Documents/PdfLoadResult.cs` | `Rendering/Pdf/` | MOVE | High |
| `Updates/UpdateService.cs` | same | KEEP | High |
| `Updates/UpdateBanner.xaml` | `UI/Controls/` | MOVE (URI in `TitleBarWindow.xaml`) | High |
| `Updates/UpdateBanner.xaml.cs` | `UI/Controls/` | MOVE | High |

## 9. UI (12 current → 11 final)

| Current | Final | Action | Conf. |
|---|---|---|---|
| `Shell/TitleBarWindow.xaml` | same | KEEP (UpdateBanner URI update) | High |
| `Shell/TitleBarWindow.xaml.cs` | same → ~700 LOC | SPLIT-in-place (tree §2.3) | High |
| `Controls/DocumentCard.xaml` + `.xaml.cs` | same | KEEP ×2 | High |
| `Controls/PageThumbnailSidebar.xaml` + `.xaml.cs` | — | DELETE ×2 (Phase 7, REVIEW) | Med |
| `Controls/ViewerInfoCard.cs` | `Features/Viewer/Viewers/Fallback/FallbackViewer.cs` | MOVE+RENAME+EXTEND (implements `IDocumentViewer`; claims `.xls` + unclaimed exts) | Med |
| `Icons/FileTypeIcons.xaml` | same | KEEP | High |
| `Templates/FileTemplates.xaml` | same | KEEP | High |
| `Styles/Colors.xaml`, `Typography.xaml`, `Controls.xaml` | same | KEEP ×3 | High |

## 10. Tests (16 current .cs + csproj → 16 + 2 NEW)

| Current | Final | Action |
|---|---|---|
| `DocGlance.Tests.csproj` | same | KEEP |
| `Common/…` ×4, `Core/Constants/SupportedExtensionsTests.cs` | same | KEEP ×5 |
| `Core/Models/PdfPageTextMapTests.cs` | `Rendering/Pdf/Models/PdfPageTextMapTests.cs` | MOVE (namespace) |
| `Features/Home/HomeViewModelTests.cs` | same | KEEP |
| `Features/Viewer/ViewerViewModelTests.cs` | same | KEEP + extend (state ownership) |
| `Infrastructure/…` ×2 | same | KEEP |
| `Rendering/Document/DocxOutlineTests.cs` | `Rendering/Docx/DocxOutlineTests.cs` | MOVE |
| `Rendering/PdfEncryptionProbeTests.cs` | `Rendering/Pdf/PdfEncryptionProbeTests.cs` | MOVE |
| `Services/Documents/PdfViewerServiceTests.cs` | `Rendering/Pdf/PdfViewerServiceTests.cs` | MOVE |
| `Services/Translation/…` ×3 | same | KEEP ×3 |
| — | `Features/Viewer/DocumentViewerRegistryTests.cs` | NEW |
| — | `Architecture/NamespaceDependencyTests.cs` | NEW |

## 11. Rust, native, assets, tools, installer, docs

**Unchanged** (Rust intact decision, AF-09): all 5 crates (Cargo.toml + Cargo.lock + src/lib.rs), `vendor/` (third-party), `native/build-native.ps1`. Assets/tools/installer/docs per tree §1 retention policies. No namespace, build, or manifest implications on the native side; `DocGlance.csproj` native `Content` items unchanged.

## 12. Verification required before each deletion

1. Full-tree grep for type name (code + XAML + csproj + manifests).
2. One manual run of the affected surface: `.cs` file open (CodeRenderer), Home refresh (FileWatcher), full nav sweep (Browser), thumbnail sidebar across PDF/DOCX/PPTX (PageThumbnailSidebar).
3. `git log --oneline -- <path>` intent check for anything that looks recently added.
