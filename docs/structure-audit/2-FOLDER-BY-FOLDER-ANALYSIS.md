# QuickView — Folder-by-Folder Analysis

**Updated:** 2026-10-03 — reflects post-migration structure  
Every file analyzed: purpose, status, problems, dependencies.

---

## Root Files

### App.xaml
- **Purpose:** Application resource dictionary — registers XamlControlsResources for WinUI 3
- **Status:** ✅ No issues

### App.xaml.cs
- **Class:** `App`
- **Purpose:** Application entry point, file activation handler, renderer registration, global exception handlers
- **Note:** `RegisterRenderers()` registers Office + HTML renderers only. Code, Text, Markdown renderers are called directly from ViewerPage (by design — they don't need registry lookup).
- **Used by:** `Program.cs`

### Program.cs
- **Class:** `Program` (static, non-partial)
- **Purpose:** Custom entry point — sets DPI awareness before WinUI initialization
- **Status:** ✅ Critical for 4K display support. Non-partial to avoid CS0017 entry-point conflict.

### QuickView.csproj
- **Purpose:** MSBuild project file
- **Status:** ✅ Well-configured with PACKAGED symbol, security settings

---

## Common/

**Purpose:** Shared utilities — formatters, validators, helpers, collections  
**Namespace:** `QuickView.Common.*`  
**Replaces:** Old `Core/Extensions/` which mixed unrelated classes

### Common/Formatters/

#### DateFormatter.cs
- **Class:** `DateFormatter` (static)
- **Purpose:** Relative date strings ("2 days ago", "Yesterday", "Just now")
- **Namespace:** `QuickView.Common.Formatters`
- **Status:** ✅ Active — 15 unit tests in `Tests/Common/Formatters/DateFormatterTests.cs`
- **Used by:** `DocumentItem.cs`, tests

#### FileSizeFormatter.cs
- **Class:** `FileSizeFormatter` (static)
- **Purpose:** Formats bytes as human-readable strings ("1.5 MB", "320 KB")
- **Namespace:** `QuickView.Common.Formatters`
- **Status:** ✅ Active — 20 unit tests in `Tests/Common/Formatters/FileSizeFormatterTests.cs`
- **Used by:** `DocumentItem.cs`, `HomePage.xaml.cs`, `HomeViewModel.cs`, tests

### Common/Validators/

#### FileSizeValidator.cs
- **Class:** `FileSizeValidator` (static)
- **Purpose:** Validates file size against DoS limits (from `AppSettings.MaxFileSizeBytes`)
- **Namespace:** `QuickView.Common.Validators`
- **Status:** ✅ Active — 12 unit tests in `Tests/Common/Validators/FileSizeValidatorTests.cs`
- **Used by:** `PdfRenderer.OpenDocumentAsync`, `TextFileReader`

#### PathValidator.cs
- **Class:** `PathValidator` (static)
- **Purpose:** **SEC-05** — Rejects symlinks, junctions, UNC paths, invalid characters
- **Namespace:** `QuickView.Common.Validators`
- **Status:** ✅ Active — security critical
- **Used by:** `FolderScanner.cs`, `FileSizeValidator.cs`

### Common/Utilities/

#### FileTypeDetector.cs
- **Class:** `FileTypeDetector` (static)
- **Purpose:** Maps file extension → `FileCategory` enum, icon glyph, MIME type
- **Namespace:** `QuickView.Common.Utilities`
- **Status:** ✅ Active — 20+ call sites across the codebase
- **Used by:** `DocumentItem`, `ThumbnailService`, `ViewerPage`, renderers

### Common/Collections/

#### IncrementalLoadingCollection.cs
- **Class:** `IncrementalLoadingCollection<T>`
- **Purpose:** ObservableCollection with `ISupportIncrementalLoading` for virtualized lists
- **Namespace:** `QuickView.Common.Collections`
- **Status:** ⚠️ Implemented but not used anywhere (dead code candidate)
- **Used by:** Nothing currently

### Common/Extensions/

#### UIThreadHelper.cs
- **Class:** `UIThreadHelper` (static extension methods)
- **Purpose:** `DispatcherQueue` extensions for UI thread marshalling (`RunOnUIThread`)
- **Namespace:** `QuickView.Core.Extensions` ← kept in Core.Extensions (true extension methods)
- **Status:** ✅ Active but underused — only ~5 call sites, should be 30+
- **Used by:** A few places; most code still uses raw `DispatcherQueue.TryEnqueue`

---

## Core/

**Purpose:** Pure domain models, interfaces, enums, constants — no dependencies on other layers  
**Namespace:** `QuickView.Core.*`

### Core/Constants/

#### FileExtensions.cs
- **Class:** `FileExtensions` (static)
- **Purpose:** Canonical extension constants (`.pdf`, `.docx`, etc.)
- **Status:** ⚠️ Incomplete — only 15 extensions defined, app supports 50+. Most code uses hardcoded strings.
- **Used by:** Rarely used

### Core/Enums/

#### FileCategory.cs — ✅ `FileCategory` enum (Document, Text, Image, Code, etc.)
#### ViewMode.cs — ✅ `ViewMode` enum (Grid vs List)

### Core/Interfaces/

#### IFileRenderer.cs — ✅ Contract for file format renderers. 8 implementations.
#### IFileScanner.cs — ⚠️ Contract for folder scanning. `FolderScanner` is called directly as static — interface unused.
#### IThumbnailProvider.cs — ✅ Contract for thumbnail providers. 3 implementations.

### Core/Models/

#### DocumentItem.cs
- **Class:** `DocumentItem` (INotifyPropertyChanged)
- **Purpose:** Core domain model — file path, size, type, thumbnail, metadata
- **Status:** ✅ Active — used everywhere
- **Note:** Contains some UI concerns (TypeColor brush, FileGlyph) — acceptable for a domain model in WinUI

#### BookmarkItem.cs — ⚠️ Defined, `BookmarkStore` implemented, but no UI to create/view bookmarks yet
#### OutlineItem.cs — ✅ PDF outline/bookmark tree node. Used by `PdfOutlineReader` + sidebar
#### PageThumbnailItem.cs — ✅ Sidebar thumbnail item. Used by `ViewerPage.Pdf.cs`
#### PdfLink.cs — ⚠️ Defined, `PdfLinkService` extracts links, but no link overlay UI
#### PdfSearchResult.cs — ✅ Search hit (page, char index, rects). Used by `PdfTextService` + `ViewerPage.Search.cs`
#### FileTypeInfo.cs — ❌ Dead code — defined but referenced nowhere

### Core/Helpers/
- **Status:** Empty folder — safe to delete

---

## Features/

**Purpose:** UI pages and feature-specific logic  
**Namespace:** `QuickView.Features.*`

### Features/Browser/

#### BrowserPage.xaml / .cs / BrowserViewModel.cs
- **Status:** ⚠️ Not wired into navigation — exists for future file browser feature
- **Used by:** Nothing currently

### Features/Home/

#### HomePage.xaml.cs
- **Class:** `HomePage` : Page
- **Purpose:** Landing page — recent files, folder scanning, search, thumbnail loading
- **Status:** ✅ Active — 550 LOC, acceptable given its breadth of responsibility
- **Used by:** `TitleBarWindow` (permanent home tab)

#### HomeViewModel.cs
- **Class:** `HomeViewModel` (ObservableObject)
- **Purpose:** Non-UI state — documents, filters, sort order, scan cancellation tokens
- **Status:** ✅ Active — 38 unit tests in `Tests/Features/Home/HomeViewModelTests.cs`
- **Used by:** `HomePage`

### Features/Settings/

#### SettingsPage.xaml / .cs — ✅ Theme, file associations, about. No issues.

### Features/Viewer/

#### ViewerPage.xaml.cs
- **Class:** `ViewerPage` : Page (partial — main shell)
- **Purpose:** Document viewer lifecycle, public API, format routing
- **Status:** ✅ Well-organized at ~124 LOC
- **Used by:** `TitleBarWindow` (document tabs)

#### ViewerViewModel.cs
- **Class:** `ViewerViewModel` (ObservableObject)
- **Purpose:** Format-agnostic viewer state — page number, zoom, rotation, commands
- **Status:** ✅ Active — 38 unit tests in `Tests/Features/Viewer/ViewerViewModelTests.cs`
- **Used by:** `ViewerPage` (data bound to toolbar/UI)

### Features/Viewer/Partials/

All files are partial classes of `ViewerPage`. Namespace: `QuickView.Features.Viewer`.

#### ViewerPage.Pdf.cs
- **Purpose:** PDF-specific UI — loads PDF via `PdfViewerService`, renders pages, manages placeholders/thumbnails
- **Status:** 🚧 571 LOC — Sprint 5 will reduce to ~200 LOC (phases 3–6 pending)
- **Note:** After Sprint 5 this file should only contain XAML manipulation and UI callbacks

#### ViewerPage.Text.cs
- **Purpose:** Text, code, and Markdown viewers with in-page search
- **Status:** ✅ ~300 LOC — handles 3 formats, acceptable

#### ViewerPage.Search.cs
- **Purpose:** PDF search UI — debounced search, highlight rectangle drawing, result navigation
- **Status:** ✅ ~250 LOC — clean, well-organized

#### ViewerPage.Toolbar.cs
- **Purpose:** Toolbar button event handlers (sidebar toggle, fit modes, rotate)
- **Status:** ✅ ~180 LOC

#### ViewerPage.Sidebar.cs
- **Purpose:** Page navigation, sidebar tab switching, thumbnail click handling
- **Status:** ✅ ~200 LOC

#### ViewerPage.Zoom.cs
- **Purpose:** Zoom presets, fit modes, re-render on zoom change
- **Status:** ✅ ~150 LOC

#### ViewerPage.Helpers.cs
- **Purpose:** Resource lookup helpers (`ResBrush`, `ResStyle`)
- **Status:** ✅ 20 LOC — small, useful

### Features/Viewer/Components/

#### ViewerToolbar.xaml / .cs
- **Class:** `ViewerToolbar` : UserControl
- **Purpose:** Viewer toolbar (page nav, zoom, fit, search toggle, sidebar toggle)
- **Status:** ✅ Active — used in `ViewerPage.xaml`

#### DocumentInfoDialog.cs
- **Class:** `DocumentInfoDialog` (static factory)
- **Purpose:** Builds file info `ContentDialog`
- **Status:** ✅ Active

#### SearchHighlighter.cs
- **Class:** `SearchHighlighter` (static), `SearchHit`, `SearchState`
- **Purpose:** Text search highlighting for `TextBox` and `RichTextBlock`
- **Namespace:** `QuickView.Features.Viewer.Components` (moved from `Features.Search`)
- **Status:** ✅ Active — used by `ViewerPage.Text.cs` and `ViewerPage.Search.cs`

#### ViewerSidebar.xaml / .cs
- **Class:** `ViewerSidebar` : UserControl
- **Status:** ⚠️ Dead code — `ViewerPage` builds its sidebar inline; this control is orphaned
- **Used by:** Nothing

---

## Rendering/

**Purpose:** File format rendering engines — independent of UI  
**Namespace:** `QuickView.Rendering.*`

### RendererRegistry.cs
- **Class:** `RendererRegistry` (static)
- **Purpose:** Maps extensions → `IFileRenderer` via `ConcurrentDictionary`
- **Status:** ✅ Active
- **Registered renderers:** DocxRenderer, XlsxRenderer, PptxRenderer, HtmlRenderer

### Rendering/Code/

#### CodeRenderer.cs
- **Purpose:** Syntax-highlighted code viewer (C#, JS, Python, 20+ languages)
- **Status:** ✅ Active — called directly from `ViewerPage.Text.cs` (not via registry)

### Rendering/Data/
- **Status:** Empty placeholder — CSV/JSON rendering not yet implemented

### Rendering/Office/

#### DocxRenderer.cs / XlsxRenderer.cs / PptxRenderer.cs
- **Purpose:** Office document "renderer" — returns info card only (no actual rendering)
- **Status:** ⚠️ Placeholder — full rendering requires Office interop or third-party library
- **Used by:** `RendererRegistry`

### Rendering/Pdf/

**8 files, ~1,500 LOC — Core PDF rendering engine via PDFium**

#### Pdfium.cs
- **Purpose:** P/Invoke wrapper — 355+ extern methods for the PDFium native library
- **Status:** ✅ **SEC-04 CRITICAL — NEVER MODIFY**
- **Security:** Static constructor explicitly loads `pdfium.dll` from `AppContext.BaseDirectory` only, preventing DLL hijacking via PATH search order

#### PdfDocumentState.cs
- **Class:** `PdfDocumentState` : IDisposable
- **Purpose:** Wraps native PDFium document handle — stores page count, pre-cached page sizes, generation counter
- **Status:** ✅ Active — stable interface

#### PdfRenderer.cs
- **Class:** `PdfRenderer` (static)
- **Purpose:** `OpenDocumentAsync`, `RenderPageAsync` → returns `WriteableBitmap`
- **Status:** ✅ Active — used by `PdfViewerService`

#### PdfOperationBase.cs
- **Class:** `PdfOperationBase` (static)
- **Purpose:** Encapsulates PDFium locking pattern (`lock (Pdfium.Gate)` + page load/cleanup)
- **Status:** ✅ Active — used by `PdfTextService`, `PdfLinkService`
- **Note:** `PdfRenderer`, `PdfAnnotationService`, `PdfOutlineReader` still use raw locking — should adopt this base

#### PdfTextService.cs
- **Class:** `PdfTextService` (static)
- **Purpose:** Text extraction and full-document search
- **API:** `SearchAsync`, `ExtractTextAsync`, `GetCharIndexAtPositionAsync`, `ExtractTextRangeAsync`
- **Status:** ✅ Active — used by `ViewerPage.Search.cs`

#### PdfOutlineReader.cs
- **Class:** `PdfOutlineReader` (static)
- **Purpose:** Reads PDF outline/bookmarks → `List<OutlineItem>` tree
- **Status:** ✅ Active — used by `ViewerPage.Pdf.cs`

#### PdfLinkService.cs
- **Class:** `PdfLinkService` (static)
- **Purpose:** Extracts clickable links (web URLs, email addresses, internal page refs)
- **Status:** ⚠️ Implemented — no UI overlay to display or click links yet
- **Used by:** Nothing (future sprint)

#### PdfAnnotationService.cs
- **Class:** `PdfAnnotationService` (static)
- **Purpose:** Adds annotations — highlight, underline, freehand ink drawing
- **Status:** ⚠️ Implemented — no annotation toolbar or save functionality yet
- **Used by:** Nothing (future sprint)

### Rendering/Text/

#### TextFileReader.cs
- **Class:** `TextFileReader` (static)
- **Purpose:** Reads text files with character limit (prevents memory DoS)
- **Status:** ✅ Active — used by `ViewerPage.Text.cs`, `CodeRenderer`

### Rendering/Unsupported/

#### UnsupportedRenderer.cs — ✅ Fallback — shows info card for unknown types

### Rendering/Web/

#### HtmlRenderer.cs
- **Purpose:** Renders HTML in WebView2 — **SEC-01: JavaScript disabled**
- **Status:** ✅ Active — security hardened

#### MarkdownRenderer.cs
- **Purpose:** Renders Markdown via Markdig → WebView2 — **SEC-02: URL sanitization**
- **Status:** ✅ Active — 15 security tests in `Tests/Rendering/Web/WebSecurityTests.cs`

---

## Services/

**Purpose:** Reusable application services (business logic layer)  
**Namespace:** `QuickView.Services.*`

### Services/Documents/

#### PdfViewerService.cs
- **Class:** `PdfViewerService` : IPdfViewerService
- **Purpose:** PDF document lifecycle orchestration — load, render, prefetch, thumbnail
- **Status:** 🚧 Sprint 5 — Phase 2 complete (LoadDocument), Phases 3–6 pending
- **Known issue:** `_prefetchGeneration` field declared but never read (CS0169 warning) — remove or use in Phase 4

#### IPdfViewerService.cs — ✅ Service contract
#### PdfLoadResult.cs — ✅ Result type: `Ok(document, pageCount)` / `Error(message)`

### Services/FileSystem/

**Moved from root-level `FileSystem/` — it's a service, not a top-level concern**

#### Scanner/FolderScanner.cs
- **Class:** `FolderScanner` (static)
- **Purpose:** Parallel folder scan with **SEC-05** path validation
- **Namespace:** `QuickView.Services.FileSystem.Scanner`
- **Status:** ✅ Active — well optimized with `Parallel.ForEach`, `MaxDOP = CPU/2`
- **Used by:** `HomePage`

#### Scanner/ScanOptions.cs
- **Status:** ⚠️ Defined but not used — `FolderScanner` uses hardcoded options

#### Index/FileIndexService.cs
- **Purpose:** CRUD for SQLite file index
- **Namespace:** `QuickView.Services.FileSystem.Index`
- **Status:** ✅ Active — used by `HomePage`

#### Index/FileIndexContext.cs — ✅ EF Core DbContext
#### Index/FileIndexRepository.cs — ✅ `IndexedFile` entity

#### Watcher/FileWatcherService.cs
- **Purpose:** Monitors file system changes via `FileSystemWatcher`
- **Namespace:** `QuickView.Services.FileSystem.Watcher`
- **Status:** ⚠️ Implemented but not wired into the app

### Services/Registration/

#### AppRegistration.cs — ✅ File association registry (HKCU, unpackaged builds)
#### RegistrationHelper.cs — ✅ Async wrapper with status checks. Used by `SettingsPage`, `App.xaml.cs`

### Services/Storage/

#### RecentFilesStore.cs — ✅ JSON persistence for recent files. Used by `HomePage`, `TitleBarWindow`
#### SettingsStore.cs — ✅ Generic JSON settings. Used by `AppSettings`, `UserPreferences`
#### BookmarkStore.cs — ⚠️ Implemented but no bookmark UI exists yet

### Services/Thumbnails/

#### ThumbnailService.cs — ✅ Orchestrates thumbnail generation. Semaphore limits to 6 concurrent.
#### ThumbnailCache.cs — ✅ Disk PNG cache with 500 MB auto-eviction.

#### Providers/PdfThumbnailProvider.cs
- **Purpose:** PDF thumbnails via `Windows.Data.Pdf` (not PDFium)
- **Note:** Uses different PDF library than main renderer intentionally — WinRT API is faster for small thumbnails
- **Status:** ✅ Active

#### Providers/OfficeThumbnailProvider.cs — ✅ Office thumbnails via Windows Shell
#### Providers/GenericThumbnailProvider.cs — ✅ Fallback via Windows Shell

### Services/Updates/

#### UpdateService.cs — ✅ GitHub Releases check + **SEC-03** SHA-256 verification before install
#### UpdateBanner.xaml / .cs — ✅ In-app notification banner. Used by `TitleBarWindow`

---

## Infrastructure/

**Purpose:** Configuration, logging  
**Namespace:** `QuickView.Infrastructure.*`

### Infrastructure/Configuration/

#### AppSettings.cs — ✅ App-wide settings (file size limits, theme, view mode). Used by `FileSizeValidator`, `SettingsPage`
#### UserPreferences.cs — ⚠️ Defined but never referenced anywhere (dead code)

### Infrastructure/Logging/

#### Logger.cs — ✅ File logger → `LocalAppData/QuickView/Logs/app.log`. 60+ call sites.

---

## UI/

**Purpose:** Shared visual components and styling  
**Namespace:** `QuickView.UI.*`

### UI/Controls/

#### DocumentCard.xaml / .cs — ✅ File card for grid view. Used in `HomePage` DataTemplate
#### PageThumbnailSidebar.xaml / .cs — ✅ PDF thumbnail sidebar. Used by `ViewerPage.xaml`
#### ViewerInfoCard.cs — ✅ Info card for unsupported formats. Code-only control (no XAML needed)

#### PdfActionBar.xaml / .cs
- **Status:** ⚠️ Dead code — `ViewerPage` uses `ViewerToolbar` instead. `PdfActionBar` is not referenced anywhere.

#### PdfTextSelectionOverlay.xaml / .cs
- **Status:** ⚠️ XAML exists, codebehind is an empty stub
- **Future:** Implement with `PdfTextService.GetCharIndexAtPositionAsync` + `ExtractTextRangeAsync`

### UI/Shell/

#### TitleBarWindow.xaml / .cs
- **Class:** `TitleBarWindow` : Window
- **Purpose:** Main window — Chrome-style tabs, drag-drop, file opening, icon setup
- **Status:** ✅ Active at ~450 LOC. Acceptable given it owns the entire window shell.

### UI/Styles/ — ✅ Colors.xaml, Controls.xaml, Typography.xaml
### UI/Templates/ — ✅ FileTemplates.xaml
### UI/Icons/ — ✅ FileTypeIcons.xaml

---

## QuickView.Tests/

**Purpose:** Automated tests (xUnit) — mirrors source structure exactly  
**Namespace:** `QuickView.Tests.*`

### Tests/Common/Formatters/
- `DateFormatterTests.cs` — 15 tests ✅
- `FileSizeFormatterTests.cs` — 20 tests ✅

### Tests/Common/Validators/
- `FileSizeValidatorTests.cs` — 12 tests ✅

### Tests/Features/Home/
- `HomeViewModelTests.cs` — 38 tests ✅

### Tests/Features/Viewer/
- `ViewerViewModelTests.cs` — 38 tests ✅

### Tests/Rendering/Web/
- `WebSecurityTests.cs` — 15 tests (SEC-02 URL sanitization) ✅

### Tests/Services/
- Empty — future tests for `PdfViewerService` (Sprint 5 Phase 6), `ThumbnailService`, `FolderScanner`

---

## Summary Statistics

| Folder | Files | LOC | Issues |
|--------|-------|-----|--------|
| **Common/** | 6 files | ~500 | `IncrementalLoadingCollection` unused |
| **Core/** | 18 files | ~1,200 | `FileTypeInfo` dead, `Core/Helpers/` empty, `IFileScanner` unused |
| **Features/** | 18 files | ~3,200 | `ViewerPage.Pdf.cs` oversize (Sprint 5), `BrowserPage` unwired, `ViewerSidebar` orphaned |
| **Rendering/** | 17 files | ~2,000 | `PdfAnnotationService`, `PdfLinkService` no UI; Office renderers placeholder only |
| **Services/** | 19 files | ~1,800 | `PdfViewerService` incomplete (Sprint 5), `BookmarkStore`/`FileWatcherService`/`ScanOptions`/`UserPreferences` unused |
| **UI/** | 9 files | ~800 | `PdfActionBar` dead, `PdfTextSelectionOverlay` stub |
| **Infrastructure/** | 3 files | ~200 | `UserPreferences` unused |
| **Tests/** | 7 files | ~1,000 | Missing tests for Rendering/Pdf, Services/Documents, Services/FileSystem |
| **TOTAL** | ~160 files | ~15,000 | 14 known issues (down from 27 before restructure) |
