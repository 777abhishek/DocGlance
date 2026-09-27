# QuickView — Project Structure

**Status:** ✅ **Fully Implemented** — All files reorganized, namespaces updated, 0 build errors.

Last updated: 2026-09-09

```text
QuickView/
│
├── QuickView.csproj
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── app.manifest
│
├── Assets/
│
├── Core/
│   ├── Interfaces/
│   │   ├── IFileRenderer.cs
│   │   ├── IThumbnailProvider.cs
│   │   └── IFileScanner.cs
│   ├── Constants/
│   │   └── FileExtensions.cs
│   ├── Enums/
│   │   ├── FileCategory.cs
│   │   └── ViewMode.cs
│   └── Helpers/
│       ├── FileTypeHelper.cs
│       ├── FileSizeHelper.cs
│       └── DateHelper.cs
│
├── Models/
│   ├── DocumentItem.cs
│   ├── FileTypeInfo.cs
│   └── PageThumbnailItem.cs
│
├── Features/
│   ├── Home/
│   │   ├── HomePage.xaml
│   │   └── HomePage.xaml.cs
│   ├── Browser/
│   │   ├── BrowserPage.xaml
│   │   ├── BrowserPage.xaml.cs
│   │   └── BrowserViewModel.cs
│   ├── Viewer/
│   │   ├── ViewerPage.xaml
│   │   ├── ViewerPage.xaml.cs
│   │   ├── ViewerViewModel.cs
│   │   └── Components/
│   │       ├── ViewerToolbar.xaml
│   │       ├── ViewerToolbar.xaml.cs
│   │       ├── ViewerSidebar.xaml
│   │       └── ViewerSidebar.xaml.cs
│   └── Settings/
│       ├── SettingsPage.xaml
│       └── SettingsPage.xaml.cs
│
├── Controls/
│   ├── DocumentCard.xaml
│   ├── DocumentCard.xaml.cs
│   ├── DocumentListItem.xaml
│   ├── DocumentListItem.xaml.cs
│   ├── PageThumbnailSidebar.xaml
│   ├── PageThumbnailSidebar.xaml.cs
│   ├── PdfActionBar.xaml
│   ├── PdfActionBar.xaml.cs
│   └── ViewerInfoCard.cs
│
├── Rendering/
│   ├── RendererRegistry.cs
│   ├── Text/
│   │   └── TextRenderer.cs
│   ├── Code/
│   │   └── CodeRenderer.cs
│   ├── Data/
│   │   ├── JsonRenderer.cs
│   │   ├── XmlRenderer.cs
│   │   └── CsvRenderer.cs
│   ├── Web/
│   │   ├── HtmlRenderer.cs
│   │   └── MarkdownRenderer.cs
│   ├── RichText/
│   │   └── RtfRenderer.cs
│   ├── Pdf/
│   │   └── PdfRenderer.cs
│   ├── Images/
│   │   └── ImageRenderer.cs
│   ├── Media/
│   │   └── MediaRenderer.cs
│   ├── Office/
│   │   ├── DocxRenderer.cs
│   │   ├── XlsxRenderer.cs
│   │   └── PptxRenderer.cs
│   └── Unsupported/
│       └── UnsupportedRenderer.cs
│
├── Thumbnails/
│   ├── ThumbnailService.cs
│   ├── ThumbnailCache.cs
│   └── Providers/
│       ├── ImageThumbnailProvider.cs
│       ├── PdfThumbnailProvider.cs
│       ├── OfficeThumbnailProvider.cs
│       └── GenericThumbnailProvider.cs
│
├── FileSystem/
│   ├── Scanner/
│   │   ├── FolderScanner.cs
│   │   └── ScanOptions.cs
│   ├── Index/
│   │   ├── FileIndexService.cs
│   │   ├── FileIndexRepository.cs
│   │   └── FileIndexContext.cs
│   └── Watcher/
│       └── FileWatcherService.cs
│
├── Infrastructure/
│   ├── Logging/
│   │   └── Logger.cs
│   ├── Storage/
│   │   ├── SettingsStore.cs
│   │   └── RecentFilesStore.cs
│   └── Configuration/
│       ├── AppSettings.cs
│       └── UserPreferences.cs
│
├── Resources/
│   ├── Styles/
│   │   ├── Colors.xaml
│   │   ├── Typography.xaml
│   │   └── Controls.xaml
│   ├── Icons/
│   │   └── FileTypeIcons.xaml
│   └── Templates/
│       └── FileTemplates.xaml
│
├── Collections/
│   └── IncrementalLoadingCollection.cs
│
├── Shell/
│   ├── TitleBarWindow.xaml
│   └── TitleBarWindow.xaml.cs
│
└── .github/
    ├── agents/
    │   └── agent.md
    └── plans/
        ├── Redesign-ThreeScreenLayout.md
        └── Slice2-FileBrowser.md
```

## Namespace Convention

All namespaces follow the `QuickView.<FolderPath>` pattern:

```csharp
QuickView.Core.Interfaces
QuickView.Core.Constants
QuickView.Core.Enums
QuickView.Core.Helpers
QuickView.Models
QuickView.Features.Home
QuickView.Features.Browser
QuickView.Features.Viewer
QuickView.Features.Settings
QuickView.Controls
QuickView.Rendering.Pdf
QuickView.Rendering.Text
QuickView.Rendering.Code
QuickView.Rendering.Data
QuickView.Rendering.Web
QuickView.Rendering.RichText
QuickView.Rendering.Images
QuickView.Rendering.Media
QuickView.Rendering.Office
QuickView.Rendering.Unsupported
QuickView.Thumbnails
QuickView.Thumbnails.Providers
QuickView.FileSystem.Scanner
QuickView.FileSystem.Index
QuickView.FileSystem.Watcher
QuickView.Infrastructure.Logging
QuickView.Infrastructure.Storage
QuickView.Infrastructure.Configuration
QuickView.Collections
QuickView.Shell
```

## Existing → New

```text
HomePage.*                 → Features/Home/
SettingsPage.*             → Features/Settings/

Pages/Viewer/*             → Features/Viewer/
                              (PDF rendering → Rendering/Pdf/)

Data/FileIndexContext.cs   → FileSystem/Index/

Services/FileIndexService  → FileSystem/Index/
Services/FolderScanner     → FileSystem/Scanner/
Services/ThumbnailService  → Thumbnails/
Services/PdfRenderService  → Rendering/Pdf/
Services/Logger            → Infrastructure/Logging/

Controls/*                 → Controls/
Models/*                   → Models/
Collections/*              → Collections/
Shell/*                    → Shell/
Assets/*                   → Assets/
```

## Implementation Notes

### Completed (2026-09-09)
- ✅ All files moved to Structure.md locations
- ✅ All namespaces updated from `yViewer.*` to `QuickView.*`
- ✅ `Document.csproj` renamed to `QuickView.csproj`
- ✅ All stub files created (Core, Rendering, Thumbnails, FileSystem, Infrastructure, Resources)
- ✅ Build succeeds with 0 errors
- ✅ App runs successfully

### Architecture Patterns
- **Features/** = Full-screen pages with view + viewmodel
- **Controls/** = Reusable UI components
- **Rendering/** = File-type specific viewer implementations (implement `IFileRenderer`)
- **Thumbnails/** = Preview generation (implement `IThumbnailProvider`)
- **FileSystem/** = Scan, index, watch filesystem operations
- **Infrastructure/** = Cross-cutting concerns (logging, storage, settings)
- **Core/** = Shared contracts, enums, constants, helpers
- **Models/** = Data transfer objects, view models
- **Resources/** = Styles, icons, templates (XAML resource dictionaries)

### Key Interfaces
- `IFileRenderer` — Renders a file type into a UIElement
- `IThumbnailProvider` — Generates thumbnails for a file type
- `IFileScanner` — Scans folders and returns DocumentItems

### Registry Pattern
- `RendererRegistry` — Maps file extensions to their IFileRenderer implementation
- Register renderers at app startup via `RendererRegistry.Register(renderer)`

## Simple Rule

```text
Features/       = screens
Controls/       = reusable UI
Rendering/      = file viewers
Thumbnails/     = previews/thumbnails
FileSystem/     = scan/index/watch
Infrastructure/ = storage/logging/settings
Core/           = shared contracts/helpers
Models/         = data
Resources/      = styles/icons/templates
```

Keep files small and single-purpose.

**Do not rewrite the application from scratch. Move existing working code into the appropriate folders incrementally, then extract responsibilities only where needed.**
