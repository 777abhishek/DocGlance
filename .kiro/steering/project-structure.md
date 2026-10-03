---
inclusion: always
---

# QuickView Project Structure

## Architecture Overview

QuickView follows a clean architecture pattern defined in `Tree.md` at the workspace root.

## Key Directories (per Tree.md)

### Core/
**Purpose:** Fundamental models, interfaces, enums, constants, and shared utilities.
**Namespace:** `QuickView.Core.*`

- `Core/Models/` - Domain models (DocumentItem, BookmarkItem, PdfLink, etc.)
- `Core/Interfaces/` - Service contracts (IFileRenderer, IFileScanner, IThumbnailProvider)
- `Core/Enums/` - Enumerations (FileCategory, ViewMode)
- `Core/Constants/` - Constants (FileExtensions)
- `Core/Extensions/` - Utility extensions (formatters, validators, helpers)

### Features/
**Purpose:** UI pages and feature-specific logic.
**Namespace:** `QuickView.Features.*`

Structure per feature:
```
Features/{FeatureName}/
├── {FeatureName}Page.xaml
├── {FeatureName}Page.xaml.cs
├── {FeatureName}ViewModel.cs
└── Components/          (optional)
```

Current features:
- `Features/Home/` - Home page with recent files
- `Features/Browser/` - File browser
- `Features/Viewer/` - Document viewer (main feature)
  - `Viewer/Pdf/` - PDF-specific partials
  - `Viewer/Text/` - Text-specific partials  
  - `Viewer/Search/` - Search-specific partials
  - `Viewer/Components/` - Shared viewer components
- `Features/Settings/` - Settings page

### Rendering/
**Purpose:** File format rendering engines (independent of UI).
**Namespace:** `QuickView.Rendering.*`

Each renderer subdirectory:
- `Rendering/Pdf/` - **PDF rendering via PDFium**
  - `PdfRenderer.cs` - Core rendering (OpenDocumentAsync, RenderPageAsync)
  - `PdfDocumentState.cs` - Document handle wrapper
  - `Pdfium.cs` - Native P/Invoke (SEC-04: secure DLL loading)
  - `PdfTextService.cs` - Text extraction and search
  - `PdfLinkService.cs` - Hyperlink extraction
  - `PdfOutlineReader.cs` - Bookmark extraction
- `Rendering/Text/` - Plain text rendering
- `Rendering/Code/` - Code file rendering (syntax highlighting)
- `Rendering/Web/` - HTML/Markdown rendering (WebView2)
- `Rendering/Office/` - Office document rendering
- `Rendering/Data/` - CSV/JSON rendering
- `Rendering/Common/` - Shared rendering utilities
- `Rendering/Unsupported/` - Unsupported format handler
- `RendererRegistry.cs` - Renderer factory

### Services/
**Purpose:** Reusable application services (business logic).
**Namespace:** `QuickView.Services.*`

- `Services/Documents/` - Document services
  - `PdfViewerService.cs` - PDF viewer orchestration (Sprint 5)
  - `IPdfViewerService.cs` - Service contract
  - `PdfLoadResult.cs` - Load operation result
- `Services/Thumbnails/` - Thumbnail generation
  - `ThumbnailService.cs`
  - `ThumbnailCache.cs`
  - `Providers/` - Format-specific providers
- `Services/Storage/` - Data persistence
  - `BookmarkStore.cs`
  - `RecentFilesStore.cs`
  - `SettingsStore.cs`
- `Services/Updates/` - Update management
  - `UpdateService.cs` (SEC-03: SHA-256 verification)
  - `UpdateBanner.xaml`
- `Services/Registration/` - File association registration

### UI/
**Purpose:** Shared visual components and styling.
**Namespace:** `QuickView.UI.*`

- `UI/Controls/` - Reusable controls
  - `DocumentCard.xaml`
  - `PageThumbnailSidebar.xaml`
  - `PdfActionBar.xaml`
  - `PdfTextSelectionOverlay.xaml`
  - `ViewerInfoCard.cs`
- `UI/Shell/` - Application shell
  - `TitleBarWindow.xaml`
- `UI/Styles/` - XAML styles
  - `Colors.xaml`
  - `Controls.xaml`
  - `Typography.xaml`
- `UI/Templates/` - Data templates
  - `FileTemplates.xaml`
- `UI/Icons/` - Icon resources
  - `FileTypeIcons.xaml`

### FileSystem/
**Purpose:** File scanning, indexing, and monitoring.
**Namespace:** `QuickView.FileSystem.*`

- `FileSystem/Scanner/` - Folder scanning (SEC-05: path validation)
- `FileSystem/Index/` - File indexing
- `FileSystem/Watcher/` - File system monitoring

### Infrastructure/
**Purpose:** Configuration, logging, and application infrastructure.
**Namespace:** `QuickView.Infrastructure.*`

- `Infrastructure/Configuration/` - App configuration
  - `AppSettings.cs`
  - `UserPreferences.cs`
- `Infrastructure/Logging/` - Logging infrastructure
- `Infrastructure/DependencyInjection/` - DI container (future - Sprint 6)

### Tests/
**Purpose:** Automated tests (xUnit).
**Namespace:** `QuickView.Tests.*`

Current structure:
- `QuickView.Tests/Features/` - Feature tests
- `QuickView.Tests/Rendering/` - Renderer tests (future)
- `QuickView.Tests/Services/` - Service tests (future)
- `QuickView.Tests/Shared/` - Core utility tests (formatters, validators)

## Critical File Relationships

### PDF Rendering Flow
1. **UI Layer:** `Features/Viewer/Pdf/ViewerPage.Pdf.cs`
   - User-facing PDF operations
   - Calls `Services/Documents/PdfViewerService`
   
2. **Service Layer:** `Services/Documents/PdfViewerService.cs`
   - PDF document lifecycle management
   - Calls `Rendering/Pdf/PdfRenderer`
   
3. **Rendering Layer:** `Rendering/Pdf/PdfRenderer.cs`
   - Low-level PDFium operations
   - Calls `Rendering/Pdf/Pdfium.cs`
   
4. **Native Layer:** `Rendering/Pdf/Pdfium.cs`
   - P/Invoke to pdfium.dll
   - **SEC-04:** Secure DLL loading in static constructor

### Security Architecture

**SEC-01:** JavaScript disabled in WebView2
- Location: `Rendering/Web/HtmlRenderer.cs`, `Rendering/Web/MarkdownRenderer.cs`

**SEC-02:** Markdig + URL sanitization
- Location: `Rendering/Web/MarkdownRenderer.cs`

**SEC-03:** SHA-256 checksum verification
- Location: `Services/Updates/UpdateService.cs`

**SEC-04:** Secure pdfium.dll loading
- Location: `Rendering/Pdf/Pdfium.cs` (static constructor)

**SEC-05:** Path validation
- Location: `Core/Extensions/PathValidator.cs`
- Used in: `FileSystem/Scanner/FolderScanner.cs`

## Namespace Rules

1. **Always match directory structure:**
   - `Services/Documents/PdfViewerService.cs` → `namespace QuickView.Services.Documents;`
   - `Rendering/Pdf/PdfRenderer.cs` → `namespace QuickView.Rendering.Pdf;`
   - `Features/Viewer/Pdf/ViewerPage.Pdf.cs` → `namespace QuickView.Features.Viewer;` (partial class)

2. **Partial classes stay in parent namespace:**
   - All ViewerPage partials use `namespace QuickView.Features.Viewer;`
   - Even though located in subdirectories (Pdf/, Text/, Search/)

3. **No circular dependencies:**
   - Core → (nothing)
   - Rendering → Core
   - Services → Core, Rendering
   - Features → Core, Rendering, Services, UI
   - UI → Core

## File Creation Rules

1. **Never create files outside Tree.md structure**
2. **Always update namespace to match location**
3. **Always check for existing files before creating new ones**
4. **Partial classes must share exact namespace with main class**
5. **XAML files require matching .xaml.cs codebehind with x:Class attribute**

## Current Sprint Work

**Sprint 5: PdfViewerService Extraction**
- Phase 1: ✓ Created service skeleton in `Services/Documents/`
- Phase 2: In progress - Extract LoadDocument logic
- Goal: Move PDF orchestration from ViewerPage to service layer
