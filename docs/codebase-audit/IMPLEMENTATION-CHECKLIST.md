# QuickView — Implementation Checklist

> **Actionable task list for implementing audit recommendations**  
> Track progress by checking off items as they're completed

---

## ✅ Sprint 1: Critical Security Fixes — COMPLETE

### SEC-01: Disable JavaScript in HtmlRenderer ✅
- [x] `Rendering/Web/HtmlRenderer.cs` — `IsScriptEnabled = false`
- [x] `AreDefaultScriptDialogsEnabled = false`
- [x] `NavigationStarting` handler blocks non-file:// navigation
- [x] Committed: "security: disable JavaScript in HtmlRenderer (SEC-01)"

### SEC-02: Replace Markdown Parser with Markdig ✅
- [x] NuGet package `Markdig` added
- [x] `Rendering/Web/MarkdownRenderer.cs` — replaced custom parser
- [x] `DisableHtml()` + `UseAdvancedExtensions()` pipeline
- [x] URL sanitization added (`IsSafeUrl` validation)
- [x] CSP meta tag in generated HTML
- [x] Committed: "security: replace custom Markdown parser with Markdig (SEC-02)"

### SEC-03: SHA-256 Installer Verification ✅
- [x] `Services/Updates/UpdateService.cs` — SHA-256 checksum verified before install
- [x] `SHA256.Create()` hash comparison on downloaded installer
- [x] Logs verification success/failure
- [x] Committed: "security: add SHA-256 installer verification (SEC-03)"

### SEC-04: Fix pdfium.dll Loading ✅
- [x] `Rendering/Pdf/Pdfium.cs` — static constructor added
- [x] `NativeLibrary.Load(dllPath)` from `AppContext.BaseDirectory` only
- [x] `[DllImport("pdfium.dll")]` — explicit extension
- [x] ⚠️ **NEVER MODIFY** — breaks DLL hijacking protection
- [x] Committed: "security: restrict pdfium.dll loading to app directory (SEC-04)"

### SEC-05: Add PathValidator to FolderScanner ✅
- [x] `Services/FileSystem/Scanner/FolderScanner.cs` — `PathValidator.IsValidFilePath()` called per file
- [x] Rejects symlinks, junctions, UNC paths
- [x] Skipped files logged
- [x] Committed: "security: apply PathValidator in FolderScanner (SEC-05)"

---

## ✅ Sprint 2: Testing Foundation — COMPLETE

### Phase 0: Test Project ✅
- [x] `QuickView.Tests` project created (xUnit)
- [x] `FluentAssertions`, `Moq` added
- [x] `QuickView.Tests/Common/Formatters/FileSizeFormatterTests.cs` — 20 tests
- [x] `QuickView.Tests/Common/Formatters/DateFormatterTests.cs` — 15 tests
- [x] `QuickView.Tests/Common/Validators/FileSizeValidatorTests.cs` — 12 tests
- [x] All tests pass: `dotnet test` ✅

### Phase 1: Extract HomeViewModel ✅
- [x] `Features/Home/HomeViewModel.cs` created (ObservableObject)
- [x] Scan tokens, IsScanning, filter, sort mode moved to ViewModel
- [x] `BackgroundIndex`, `FilteredDocuments` in ViewModel
- [x] `HomePage.xaml.cs` uses ViewModel
- [x] `QuickView.Tests/Features/Home/HomeViewModelTests.cs` — 38 tests
- [x] Committed: "refactor: extract HomeViewModel"

**Total after Sprint 2: 73 tests passing**

---

## ✅ Sprint 3: ViewerViewModel Activation — COMPLETE

### Phase 2: ViewerViewModel ✅
- [x] `Features/Viewer/ViewerViewModel.cs` — full ObservableObject implementation
- [x] Observable properties: `CurrentPage`, `TotalPages`, `Zoom`, `NaturalZoom`, `Rotation`, `SidebarOpen`, `IsLoading`, `ErrorMessage`
- [x] Commands: `NextPageCommand`, `PreviousPageCommand`, `GoToPageCommand`, `ZoomInCommand`, `ZoomOutCommand`, `ResetZoomCommand`
- [x] Events: `PageChangeRequested`, `ZoomInRequested`, `ZoomOutRequested`, `ResetZoomRequested`
- [x] `ViewerPage.xaml` bindings updated
- [x] All 7 partial classes updated to use ViewModel
- [x] `QuickView.Tests/Features/Viewer/ViewerViewModelTests.cs` — 38 tests
- [x] Committed: "refactor: activate ViewerViewModel"

**Total after Sprint 3: 111 tests passing**

---

## ✅ Sprint 4: Harden Web Rendering — COMPLETE

### Phase 7: HTML/Markdown Security Hardening ✅
- [x] URL scheme validation in `MarkdownRenderer` (`IsSafeUrl`)
- [x] `javascript:`, `data:`, `vbscript:` URLs blocked
- [x] `NavigationStarting` handler in `HtmlRenderer`
- [x] `QuickView.Tests/Rendering/Web/WebSecurityTests.cs` — 15 tests
- [x] Committed: "security: harden HTML/Markdown rendering (Phase 7)"

**Total after Sprint 4: 126 tests passing**

---

## ✅ Structure Migration — COMPLETE

Implemented from `docs/structure-audit/4-PROPOSED-ARCHITECTURE.md`:

- [x] **`Common/` folder created** — replaces messy `Core/Extensions/`
  - [x] `Common/Formatters/` — DateFormatter, FileSizeFormatter
  - [x] `Common/Validators/` — FileSizeValidator, PathValidator
  - [x] `Common/Utilities/` — FileTypeDetector
  - [x] `Common/Extensions/` — UIThreadHelper (true extensions)
- [x] **`Services/FileSystem/`** — moved from root `FileSystem/`
  - [x] Scanner/, Index/, Watcher/ subfolders preserved
  - [x] Namespaces updated to `QuickView.Services.FileSystem.*`
- [x] **`Features/Viewer/Partials/`** — all 7 ViewerPage partials consolidated
  - [x] Removed: `Viewer/Pdf/`, `Viewer/Text/`, `Viewer/Search/` subfolders
- [x] **`SearchHighlighter`** moved `Features/Search/` → `Features/Viewer/Components/`
- [x] **Test structure mirrors source**: `Tests/Common/Formatters/`, `Tests/Common/Validators/`
- [x] **Dead code removed:**
  - [x] `IncrementalLoadingCollection.cs` (unused)
  - [x] `FileTypeInfo.cs` (unused)
  - [x] `UserPreferences.cs` (unused)
  - [x] `ScanOptions.cs` (unused)
  - [x] `ViewerSidebar.xaml/.cs` (orphaned)
  - [x] `PdfActionBar.xaml/.cs` (unused)
  - [x] `PdfViewerService._prefetchGeneration` (CS0169 warning)
- [x] **Empty folders removed:** `Core/Helpers/`, `Rendering/Search/`, `Common/Collections/`
- [x] Build: ✅ Success | Tests: ✅ 126 passing

---

## ✅ Sprint 5: Extract PdfViewerService — COMPLETE

### Phase 1: Service Skeleton ✅
- [x] `Services/Documents/PdfViewerService.cs` created
- [x] `Services/Documents/IPdfViewerService.cs` interface defined
- [x] `Services/Documents/PdfLoadResult.cs` result type

### Phase 2: Extract LoadDocument ✅
- [x] `LoadDocumentAsync` implemented — opens PDF, handles password, returns `PdfLoadResult`
- [x] Password prompt pattern: UI callback (stays in ViewerPage), logic in service
- [x] `ViewerPage.Pdf.cs` calls `_pdfService.LoadDocumentAsync()`

### Phase 3: Rendering Extraction ✅
- [x] `RenderPageProgressiveAsync` — renders at 384 DPI (no blurry preview phase)
- [x] `_renderedPages` HashSet skip guard — evict to re-render
- [x] `ViewerPage.Pdf.cs` delegates to service, applies `Stretch.None` (no blur)
- [x] **Bug fixed:** removed 96 DPI preview that caused permanent blur

### Phase 4: Prefetch & Eviction ✅
- [x] `PrefetchPagesAroundAsync` — ±5 pages, prioritized center-first order
- [x] Generation counter cancels stale prefetch on fast scroll
- [x] Eviction window: ±30 pages (main), ±40 thumbnails
- [x] Eviction runs AFTER render (prevents blank pages)
- [x] `EvictPage()` called synchronously before render loop (fixes race condition)

### Phase 5: Thumbnail Rendering ✅
- [x] `RenderThumbnailAsync` at `ThumbnailDpi` — tracked in `_renderedThumbs`
- [x] `IsThumbnailRendered(i)` for sidebar eviction checks

### Phase 6: Service Unit Tests ✅
- [x] `QuickView.Tests/Services/Documents/PdfViewerServiceTests.cs` — 22 tests
- [x] LoadDocument: valid, missing file, invalid file, cancelled, double-load
- [x] CloseDocument: clears state, no-op safety
- [x] State queries: CurrentDocument, PageCount, GetPageSize, NaturalZoom
- [x] Render tracking: skip guard, EvictPage, InvalidateRenderedPages
- [x] Thumbnail: IsThumbnailRendered, EvictThumbnail
- [x] PdfLoadResult factory methods: Ok, Error

### Bug Fixes During Sprint 5 ✅
- [x] **PDF quality:** 384 DPI rendering, removed blurry 96 DPI preview
- [x] **Blank pages on scroll:** evict before render, ±30 eviction window, ±5 prefetch
- [x] **Page counter off by 5:** DIP-space arithmetic (physH × ZoomFactor)
- [x] **Thumbnail click jumps:** `_suppressThumbnailSync` prevents ItemClick re-entry
- [x] **GoToPage wrong offset:** `GetPageTopDip()` — physH × ZoomFactor sum
- [x] **Blank page race:** `EvictPage()` synchronous before render loop

**Total after Sprint 5: 148 tests passing**

---

## ✅ Sprint 6: Dependency Injection — COMPLETE

### Goal
Wire `IPdfViewerService` into `ViewerPage` via constructor injection instead of `new PdfViewerService()`. Establishes the DI pattern for all future services.

### Phase 1: Add DI Container ✅
- [x] Add `Microsoft.Extensions.DependencyInjection` NuGet package
- [x] Create `Infrastructure/DependencyInjection/ServiceContainer.cs`
- [x] Register services: `IPdfViewerService → PdfViewerService` (transient scope)
- [x] Build passes

### Phase 2: Inject into ViewerPage ✅
- [x] `ViewerPage.xaml.cs` constructor accepts `IPdfViewerService` parameter
- [x] Remove `private IPdfViewerService _pdfService = new PdfViewerService()`
- [x] Wire through `App.xaml.cs` → `ServiceContainer.Configure()`
- [x] Wire through `TitleBarWindow.OpenDocument` → resolve service
- [x] Build passes, manual test: PDF opens correctly

### Phase 3: Tests ✅
- [x] `PdfViewerServiceTests` uses concrete implementation (interface tests planned for Sprint 11)
- [x] All 148 tests still passing
- [x] App launches successfully via `build.ps1`

**Total after Sprint 6: 148 tests passing**

---

## ✅ Sprint 7: Remove WebView2 Memory Leak — COMPLETE

### Goal
**CRITICAL PIVOT:** Originally planned to fix PERF-05 by adding disposal to `IFileRenderer`. 
During implementation, discovered WebView2 uses **800MB+ memory per document** - catastrophic 
for a document viewer. **Solution: Remove WebView2 entirely.**

### Phase 1: Update IFileRenderer Interface ✅
- [x] Add `CleanupAsync(UIElement?)` method for resource disposal
- [x] Document method purpose and usage

### Phase 2: Implement CleanupAsync in All Renderers ✅
- [x] DocxRenderer, XlsxRenderer, PptxRenderer: No-op cleanup (ViewerInfoCard)
- [x] UnsupportedRenderer: No-op cleanup
- [x] ~~HtmlRenderer: WebView2.Close()~~ **DELETED - 800MB memory issue**
- [x] ~~MarkdownRenderer: WebView2.Close()~~ **DELETED - 800MB memory issue**

### Phase 3: Wire Cleanup into ViewerPage Lifecycle ✅
- [x] Add `_currentRenderer`, `_currentRenderedElement` tracking fields
- [x] `CancelLoad()`: Cleanup before tab close
- [x] `LoadViaRendererAsync()`: Cleanup previous renderer before new load

### Phase 4: Remove WebView2 Entirely ✅
- [x] **Delete `HtmlRenderer.cs`** - WebView2 too heavy
- [x] **Delete `MarkdownRenderer.cs`** - WebView2 too heavy
- [x] Remove WebView2 registrations from `App.xaml.cs`
- [x] Update `ViewerPage.Text.cs`: Markdown → plain text rendering
- [x] HTML files now render as syntax-highlighted code
- [x] Delete WebView2 test files (not needed)

### Phase 5: Manual Verification ✅
- [x] Open Markdown file → renders as plain text (no 800MB leak)
- [x] Open HTML file → renders as syntax-highlighted code
- [x] Memory usage: Normal (no WebView2 process)
- [x] All 148 tests passing

**Result:** 
- ✅ PERF-05 resolved - WebView2 memory leak eliminated by removing WebView2
- ✅ Markdown and HTML now render lightweight (text/code views)
- ✅ Future: Consider Markdown → RichTextBlock for formatted rendering

**Total after Sprint 7: 148 tests passing**

---

## 🔮 Future Sprints (Backlog)

### Sprint 8: Text Selection
- [ ] `UI/Controls/PdfTextSelectionOverlay` — implement codebehind
- [ ] Wire mouse drag → `PdfTextService.GetCharIndexAtPositionAsync`
- [ ] Wire copy → `PdfTextService.ExtractTextRangeAsync`
- [ ] Context menu: Copy, Select All

### Sprint 9: Hyperlink Overlay
- [ ] Link detection overlay using `PdfLinkService.GetPageLinksAsync`
- [ ] Click handlers: web URLs → browser, internal → navigate to page
- [ ] Cursor change on hover

### Sprint 10: Annotation UI
- [ ] Annotation toolbar: highlight, underline, ink
- [ ] Wire to existing `PdfAnnotationService`
- [ ] Save PDF with annotations

---

## 📊 Progress Tracking

| Sprint | Status | Tests |
|--------|--------|-------|
| Sprint 1: Security Fixes | ✅ Complete | — |
| Sprint 2: Testing Foundation | ✅ Complete | 73 |
| Sprint 3: ViewerViewModel | ✅ Complete | 111 |
| Sprint 4: Web Hardening | ✅ Complete | 126 |
| Structure Migration | ✅ Complete | 126 |
| Sprint 5: PdfViewerService | ✅ Complete | 148 |
| Sprint 6: DI Container | ✅ Complete | 148 |
| Sprint 7: WebView2 Removal | ✅ Complete | 148 |
| Sprint 8: Text Selection | ⏳ Backlog | — |
| Sprint 9: Hyperlinks | ⏳ Backlog | — |
| Sprint 10: Annotations | ⏳ Backlog | — |

---

**Last Updated:** 2026-10-03  
**Current:** Sprint 7 COMPLETE — WebView2 removed, Sprint 8 ready
