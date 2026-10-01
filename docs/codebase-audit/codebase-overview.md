# QuickView — Codebase Overview

> Audit date: 2026-10-01  
> Scope: full static analysis, no source files modified.

---

## 1. Project Identity

| Property | Value |
|---|---|
| Name | QuickView |
| Type | WinUI 3 unpackaged desktop application |
| Target framework | `net8.0-windows10.0.19041.0` |
| Architecture | x64-only, self-contained |
| Deployment | Inno Setup installer; no MSIX packaging |
| Assembly version | 1.0.0.0 |
| Informational version | 2026.09.28 |

---

## 2. Project Structure

```
QuickView/
├── App.xaml / App.xaml.cs          — Application entry point, activation, renderer registration
├── app.manifest                    — DPI awareness, long-path, scrolling settings
├── QuickView.csproj                — Single project, x64, no trimming, AllowUnsafeBlocks=true
│
├── Shell/                          — Chrome-level window host
│   └── TitleBarWindow.xaml[.cs]   — TabView shell, drag-and-drop, file picker, icon loading
│
├── Features/
│   ├── Home/
│   │   └── HomePage.xaml[.cs]     — Recent/All documents view, scan management, thumbnail loading
│   ├── Viewer/
│   │   ├── ViewerPage.xaml[.cs]   — Viewer shell, state, lifecycle
│   │   ├── ViewerViewModel.cs     — CommunityToolkit.Mvvm ObservableObject (thin, data-binding only)
│   │   └── Components/
│   │       ├── ViewerPage.Pdf.cs      — PDF loading, rendering, placeholder layout, prefetch
│   │       ├── ViewerPage.Text.cs     — Text/code/markdown viewers, visibility, search dispatch
│   │       ├── ViewerPage.Search.cs   — PDF search UI, highlight canvas, scroll-to-hit
│   │       ├── ViewerPage.Sidebar.cs  — Page navigation, outline TOC, bookmarks
│   │       ├── ViewerPage.Toolbar.cs  — All toolbar button handlers
│   │       ├── ViewerPage.Zoom.cs     — Zoom, fit-width/page, re-render at zoom, scroll anchoring
│   │       ├── ViewerPage.Helpers.cs  — Static resource lookups
│   │       └── DocumentInfoDialog.cs  — Builds the file info ContentDialog
│   ├── Browser/
│   │   ├── BrowserPage.xaml[.cs]  — Simple page wrapper
│   │   └── BrowserViewModel.cs    — Folder scan via FolderScanner, cancel-on-navigate
│   ├── Search/
│   │   └── SearchHighlighter.cs   — Text/code search engine and highlight overlay
│   └── Settings/
│       └── SettingsPage.xaml[.cs] — Theme/preferences UI
│
├── Rendering/
│   ├── RendererRegistry.cs         — ConcurrentDictionary ext → IFileRenderer
│   ├── Pdf/
│   │   ├── Pdfium.cs              — Full P/Invoke surface (~80 extern methods)
│   │   ├── PdfDocumentState.cs    — IDisposable wrapper for native handle + page sizes
│   │   ├── PdfRenderer.cs         — RasterBitmap rendering via ArrayPool
│   │   ├── PdfOperationBase.cs    — Internal static helper (lock/load/cleanup patterns)
│   │   ├── PdfTextService.cs      — Text extraction, search, char-index hit-testing
│   │   ├── PdfLinkService.cs      — Web and annotation link extraction
│   │   ├── PdfOutlineReader.cs    — PDF bookmark/TOC reader
│   │   └── PdfAnnotationService.cs — Markup/ink annotation writing (PDFium)
│   ├── Code/
│   │   └── CodeRenderer.cs        — Syntax-coloured RichTextBlock paragraphs (single-color per line)
│   ├── Text/
│   │   └── TextFileReader.cs      — Capped streaming reader (1 MB default)
│   ├── Web/
│   │   ├── MarkdownRenderer.cs    — Custom MD→HTML converter, displays in WebView2
│   │   └── HtmlRenderer.cs        — WebView2 file:// navigation for HTML files
│   ├── Office/
│   │   ├── DocxRenderer.cs        — DOCX info card (no full rendering)
│   │   ├── XlsxRenderer.cs        — XLSX info card
│   │   └── PptxRenderer.cs        — PPTX info card
│   ├── Data/                       — (folder exists, not fully populated)
│   └── Unsupported/
│       └── UnsupportedRenderer.cs — Fallback "unsupported" card
│
├── Controls/
│   ├── PdfTextSelectionOverlay.xaml[.cs] — Drag-select/copy overlay on PDF pages
│   ├── PageThumbnailSidebar.xaml[.cs]    — Thumbnail list panel
│   ├── DocumentCard.xaml[.cs]            — Grid-view card for home page
│   ├── DocumentListItem.xaml[.cs]        — List-view row
│   ├── ViewerInfoCard.cs                  — Unsupported file info card (code-only)
│   └── PdfActionBar.xaml[.cs]            — (exists, extent unknown)
│
├── Models/
│   ├── DocumentItem.cs     — Primary file representation, INotifyPropertyChanged
│   ├── BookmarkItem.cs
│   ├── OutlineItem.cs
│   ├── PageThumbnailItem.cs
│   ├── PdfLink.cs          — Link type/rect model
│   ├── PdfSearchResult.cs  — Search hit + rect model
│   └── FileTypeInfo.cs
│
├── Core/
│   ├── Interfaces/
│   │   ├── IFileRenderer.cs      — Renderer contract (returns UIElement)
│   │   ├── IFileScanner.cs       — Scanner contract
│   │   └── IThumbnailProvider.cs — Thumbnail provider contract
│   ├── Constants/
│   │   └── FileExtensions.cs
│   └── Enums/
│       ├── FileCategory.cs
│       └── ViewMode.cs
│
├── FileSystem/
│   ├── Index/
│   │   ├── FileIndexContext.cs    — EF Core DbContext (SQLite, LocalAppData)
│   │   ├── FileIndexRepository.cs
│   │   └── FileIndexService.cs   — CRUD over indexed files / scanned folders
│   ├── Scanner/
│   │   ├── FolderScanner.cs      — Parallel directory scan, extension filter
│   │   └── ScanOptions.cs
│   └── Watcher/
│       └── FileWatcherService.cs
│
├── Thumbnails/
│   ├── ThumbnailService.cs        — Provider-routing orchestrator + semaphore
│   ├── ThumbnailCache.cs          — Disk PNG cache with LRU-style eviction
│   └── Providers/
│       ├── PdfThumbnailProvider.cs    — WinRT Windows.Data.Pdf + Shell fallback
│       ├── OfficeThumbnailProvider.cs — Shell thumbnail
│       └── GenericThumbnailProvider.cs — Shell fallback for all types
│
├── Infrastructure/
│   ├── Configuration/
│   │   ├── AppSettings.cs        — Typed settings with auto-persistence via SettingsStore
│   │   └── UserPreferences.cs
│   ├── Logging/
│   │   └── Logger.cs             — File-based logger, 5 MB rotation, path sanitisation
│   ├── Registration/
│   │   ├── AppRegistration.cs    — HKCU registry file-association writer (HKCU, no elevation)
│   │   └── RegistrationHelper.cs — Async wrapper for AppRegistration
│   ├── Storage/
│   │   ├── SettingsStore.cs      — Atomic JSON settings (write-to-temp → rename)
│   │   ├── BookmarkStore.cs      — In-memory + JSON bookmark persistence
│   │   └── RecentFilesStore.cs   — Recent file paths list via SettingsStore
│   └── Update/
│       ├── UpdateService.cs      — GitHub Releases API checker + WinVerifyTrust installer verify
│       └── UpdateBanner.xaml[.cs] — UI banner for update notifications
│
├── Shared/
│   ├── Collections/
│   │   └── IncrementalLoadingCollection.cs — ISupportIncrementalLoading wrapper
│   ├── Detection/
│   │   └── FileTypeDetector.cs   — MIME type, category, icon glyph
│   ├── Formatters/
│   │   ├── FileSizeFormatter.cs
│   │   └── DateFormatter.cs
│   ├── Threading/
│   │   └── UIThreadHelper.cs     — DispatcherQueue extension methods
│   └── Validation/
│       ├── FileSizeValidator.cs  — Extension-specific size limits, calls PathValidator
│       └── PathValidator.cs      — Symlink/junction/UNC rejection via GetFileAttributes
│
├── Resources/                    — XAML resource dictionaries
├── Assets/                       — Icons, file-type icons
├── installer/                    — Inno Setup script, manifest generator
├── tools/                        — EmbedIcon build tool (excluded from compilation)
├── build.ps1                     — Stop → build → embed icon → launch
└── build-installer.ps1           — Produce installable .exe via Inno Setup
```

---

## 3. Dependency Graph (actual, verified from imports)

```
App.xaml.cs
  └─→ Shell.TitleBarWindow
        └─→ Features.Home.HomePage
              └─→ FileSystem.Index.FileIndexService  (EF Core SQLite)
              └─→ FileSystem.Scanner.FolderScanner
              └─→ Thumbnails.ThumbnailService
              └─→ Infrastructure.Storage.RecentFilesStore
        └─→ Features.Viewer.ViewerPage (partial)
              └─→ Rendering.Pdf.*        (PDFium native)
              └─→ Rendering.Code.CodeRenderer
              └─→ Rendering.Text.TextFileReader
              └─→ Rendering.Web.MarkdownRenderer  (WebView2)
              └─→ Rendering.RendererRegistry
              └─→ Features.Search.SearchHighlighter
              └─→ Controls.PdfTextSelectionOverlay
              └─→ Infrastructure.Storage.BookmarkStore
        └─→ Infrastructure.Update.UpdateService (HttpClient → GitHub API)
  └─→ Infrastructure.Registration.RegistrationHelper (HKCU registry)
  └─→ Shared.Validation.FileSizeValidator
        └─→ Shared.Validation.PathValidator
              └─→ kernel32.dll (GetFileAttributes P/Invoke)
```

---

## 4. Third-Party Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.WindowsAppSDK` | 2.4.0 | WinUI 3 framework + WinRT APIs |
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.11 | File index persistence |
| `Microsoft.Graphics.Win2D` | 1.4.0 | Declared but usage not confirmed in reviewed files |
| `CommunityToolkit.Mvvm` | 8.4.2 | `ObservableObject`, `[ObservableProperty]` |
| `PDFiumCore` | 97.0.4690 | Native pdfium.dll (PDF rendering) |

**Native DLLs loaded at runtime:**
- `pdfium.dll` — loaded by P/Invoke in `Pdfium.cs` (DLL name only, no explicit path)
- `kernel32.dll` — `GetFileAttributes` in `PathValidator.cs`
- `shell32.dll` — `SHChangeNotify` in `AppRegistration.cs`
- `user32.dll` — `LoadImage`, `SendMessage` in `TitleBarWindow.xaml.cs`
- `wintrust.dll` — `WinVerifyTrust` in `UpdateService.cs`
- WebView2 runtime — used by `MarkdownRenderer` and `HtmlRenderer`

---

## 5. Application Flow

### Launch sequence
1. `App()` constructor: initialise global exception handlers, subscribe to re-activation.
2. `OnLaunched()`: register renderers → create `TitleBarWindow` → start update check (background) → handle activation args or command-line path.
3. `TitleBarWindow()`: add Home tab → navigate `HomeFrame` to `HomePage` → start `UpdateBanner`.
4. `HomePage.Page_Loaded()`: load recent files → initialise SQLite index → load cached scan.

### File open sequence
1. Path validated by `FileSizeValidator.IsFileValidAndSafe` (path security + size limit).
2. `DocumentItem.FromFileInfo` creates the model.
3. `TitleBarWindow.OpenDocument` adds a new `TabViewItem` + `ViewerPage`.
4. `ViewerPage.LoadDocumentAsync` dispatches to the correct renderer (PDF / text / code / MD / registry).

### PDF rendering sequence
1. `PdfRenderer.OpenDocumentAsync` → PDFium lock → `FPDF_LoadDocument` → pre-cache page sizes.
2. Placeholder `Border` nodes created for all pages (instant).
3. First page rendered at 96 DPI (preview) then 192 DPI (full quality).
4. `PrefetchPagesAroundAsync` renders ±2 pages in background.
5. Eviction evicts pages >±6 from viewport.
6. On zoom: `ReRenderVisiblePagesAsync` re-rasterises at `effectiveDpi = RenderDpi × (zoom/NaturalZoom)`.

---

## 6. Key Design Decisions Observed

- **Partial class decomposition of ViewerPage**: the viewer is split into 8 partial files by concern (Pdf, Text, Search, Sidebar, Toolbar, Zoom, Helpers). This reduces individual file sizes but ViewerPage still has one large shared state scope.
- **No DI container**: all dependencies resolved via static classes. `App.xaml.cs` manually registers renderers.
- **Provider pattern for thumbnails**: `ThumbnailService` routes through `IThumbnailProvider` implementations. Good extensibility point.
- **Renderer registry**: `RendererRegistry` maps extensions to `IFileRenderer`. Renderers registered at startup in `App.xaml.cs`.
- **Security-first path handling**: `PathValidator` rejects symlinks, junctions, and UNC paths. `FileSizeValidator` enforces per-type size limits.
- **Log path sanitisation**: `Logger` regex-strips `C:\Users\<username>` from log lines.
- **Authenticode verification on installer download**: `UpdateService` calls `WinVerifyTrust` before executing a downloaded installer.
- **HKCU-only registry writes**: file associations registered without elevation.
- **SQLite index**: `FileIndexContext` (EF Core) stores indexed files and scanned folders in LocalAppData.
