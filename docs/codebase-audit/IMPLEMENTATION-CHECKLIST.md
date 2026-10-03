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

## 🚧 Sprint 5: Extract PdfViewerService — IN PROGRESS

### Phase 1: Service Skeleton ✅
- [x] `Services/Documents/PdfViewerService.cs` created
- [x] `Services/Documents/IPdfViewerService.cs` interface defined
- [x] `Services/Documents/PdfLoadResult.cs` result type
- [x] All methods stub (`throw new NotImplementedException`)

### Phase 2: Extract LoadDocument ✅
- [x] `LoadDocumentAsync` implemented — opens PDF, handles password, returns `PdfLoadResult`
- [x] Password prompt pattern: UI callback (stays in ViewerPage), logic in service
- [x] Cancellation token support throughout
- [x] `CloseDocument()` disposes state, cancels prefetch
- [x] `ViewerPage.Pdf.cs` calls `_pdfService.LoadDocumentAsync()`

### Phase 3: Progressive Rendering 🚧
- [x] `RenderPageProgressiveAsync` implemented in `PdfViewerService`
  - [x] Phase 1: 96 DPI preview returned immediately
  - [x] Phase 2: Full `RenderDpi` quality rendered in background, returned via `onFullQuality` callback
  - [x] `_renderedPages` HashSet tracks rendered pages (skip if already done)
  - [x] Error handling — removes from HashSet on failure
- [ ] Update `ViewerPage.Pdf.cs` — replace `RenderPageAsync` body with `_pdfService.RenderPageProgressiveAsync()` call
- [ ] Keep `ApplyPageVisual` and `DrawHighlights` in ViewerPage (UI-only)
- [ ] Manual test: open large PDF, verify preview → full quality transition
- [ ] Build passes, 126 tests still pass

### Phase 4: Prefetch & Eviction ⏳
- [ ] `PrefetchPagesAround(centerPage)` — ±2 pages, prioritized order
- [ ] `PrefetchThumbnailsAround(centerPage)` — ±4 pages
- [ ] Generation counter for cancelling stale prefetch
- [ ] Eviction: ±6 page window (main), ±10 (thumbnails)
- [ ] Update `ViewerPage.Pdf.cs` — replace `PrefetchPagesAroundAsync` with service call
- [ ] Manual test: 50-page PDF, scroll entire document, verify memory bounded

### Phase 5: Thumbnail Rendering ⏳
- [ ] `RenderThumbnailAsync(pageIndex)` — 96 DPI, tracked in `_renderedThumbs`
- [ ] Update `ViewerPage.Pdf.cs` — replace thumbnail render with service call
- [ ] Manual test: sidebar thumbnails load correctly

### Phase 6: Service Unit Tests ⏳
- [ ] `QuickView.Tests/Services/Documents/PdfViewerServiceTests.cs`
- [ ] Test: `LoadDocumentAsync` with valid PDF → Success
- [ ] Test: `LoadDocumentAsync` with invalid path → Error
- [ ] Test: `LoadDocumentAsync` with password PDF → NeedsPassword
- [ ] Test: `CloseDocument` releases state
- [ ] Test: `RenderPageProgressiveAsync` skips already-rendered pages
- [ ] Target: 126 + ~15 new tests = ~141 total

---

## 🔮 Future Sprints (Backlog)

### Sprint 6: Dependency Injection
- [ ] Introduce DI container (`Microsoft.Extensions.DependencyInjection`)
- [ ] Register `PdfViewerService`, `ThumbnailService`, `RecentFilesStore`
- [ ] `ViewerPage` receives `IPdfViewerService` via constructor
- [ ] See: `docs/codebase-audit/refactoring-roadmap.md#phase-6`

### Sprint 7: Fix IFileRenderer Interface
- [ ] `IFileRenderer.RenderAsync` returns `UIElement` not `object`
- [ ] `CodeRenderer` aligned with `IFileRenderer` pattern (currently bypasses registry)
- [ ] `MarkdownRenderer` registered in `RendererRegistry`
- [ ] See: `docs/codebase-audit/refactoring-roadmap.md#phase-4`

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
- [ ] Wire to existing `PdfAnnotationService` (rendering code already done)
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
| Sprint 5: PdfViewerService | 🚧 Phase 3/6 | 126 |
| Sprint 6: DI Container | ⏳ Backlog | — |
| Sprint 7: IFileRenderer | ⏳ Backlog | — |
| Sprint 8: Text Selection | ⏳ Backlog | — |

---

**Last Updated:** 2026-10-03  
**Current:** Sprint 5 Phase 3 — `RenderPageProgressiveAsync` implemented, wiring ViewerPage.Pdf.cs next
