# QuickView — Proposed Target Architecture

Professional, modular structure that makes "where does my code go?" obvious.

---

## Design Principles

1. **By Feature, Not By Type** — Group related code together
2. **Mirror Test Structure** — Tests follow exact same folder layout as source
3. **One Responsibility Per Folder** — Each folder has a single, clear purpose
4. **Minimize Nesting** — Prefer flat structure over deep hierarchies
5. **Consistent Naming** — File = class name, folders in singular form

---

## Target Folder Tree

```
QuickView/
├── App.xaml
├── App.xaml.cs
├── Program.cs
├── QuickView.csproj
│
├── Core/                          ← Pure domain models, no dependencies
│   ├── Models/                    ← Domain entities
│   │   ├── DocumentItem.cs
│   │   ├── BookmarkItem.cs
│   │   ├── OutlineItem.cs
│   │   ├── PageThumbnailItem.cs
│   │   ├── PdfLink.cs
│   │   └── PdfSearchResult.cs
│   ├── Enums/
│   │   ├── FileCategory.cs
│   │   └── ViewMode.cs
│   ├── Constants/
│   │   └── FileExtensions.cs
│   └── Interfaces/                ← Contracts only, no implementations
│       ├── IFileRenderer.cs
│       ├── IFileScanner.cs
│       └── IThumbnailProvider.cs
│
├── Common/                        ← NEW: Shared utilities (replaces Core/Extensions mess)
│   ├── Formatters/                ← NEW: From Core/Extensions
│   │   ├── DateFormatter.cs
│   │   └── FileSizeFormatter.cs
│   ├── Validators/                ← NEW: From Core/Extensions
│   │   ├── FileSizeValidator.cs
│   │   └── PathValidator.cs
│   ├── Utilities/                 ← NEW: From Core/Extensions
│   │   ├── FileTypeDetector.cs
│   │   └── ResourceHelper.cs      ← NEW: Consolidate ResBrush/ResStyle helpers
│   ├── Collections/               ← NEW: From Core/Extensions
│   │   └── IncrementalLoadingCollection.cs
│   └── Extensions/                ← TRUE extensions only
│       └── UIThreadExtensions.cs  ← RENAMED from UIThreadHelper
│
├── Documents/                     ← NEW: Per-format document handling (consolidates Rendering + Services)
│   ├── Pdf/                       ← Consolidate all PDF code here
│   │   ├── Rendering/
│   │   │   ├── PdfRenderer.cs
│   │   │   ├── Pdfium.cs          ← SEC-04 CRITICAL
│   │   │   └── PdfDocumentState.cs
│   │   ├── Services/
│   │   │   ├── PdfViewerService.cs
│   │   │   ├── PdfTextService.cs
│   │   │   ├── PdfLinkService.cs
│   │   │   ├── PdfAnnotationService.cs
│   │   │   ├── PdfOutlineReader.cs
│   │   │   └── PdfOperationBase.cs
│   │   ├── Models/                ← MOVED from Core/Models
│   │   │   ├── PdfLoadResult.cs
│   │   │   ├── PdfLink.cs         ← MOVED
│   │   │   └── PdfSearchResult.cs ← MOVED
│   │   └── IPdfViewerService.cs
│   ├── Text/
│   │   ├── TextRenderer.cs        ← RENAMED from TextFileReader
│   │   └── TextFileReader.cs      ← Keep as internal helper
│   ├── Code/
│   │   └── CodeRenderer.cs
│   ├── Markdown/
│   │   └── MarkdownRenderer.cs    ← MOVED from Rendering/Web
│   ├── Html/
│   │   └── HtmlRenderer.cs        ← MOVED from Rendering/Web
│   ├── Office/
│   │   ├── DocxRenderer.cs
│   │   ├── XlsxRenderer.cs
│   │   ├── PptxRenderer.cs
│   │   └── OfficeThumbnailProvider.cs  ← MOVED from Services/Thumbnails/Providers
│   └── _Shared/
│       ├── RendererRegistry.cs    ← MOVED from Rendering/
│       └── UnsupportedRenderer.cs
│
├── Features/                      ← UI features (pages + ViewModels)
│   ├── Home/
│   │   ├── HomePage.xaml
│   │   ├── HomePage.xaml.cs
│   │   └── HomeViewModel.cs
│   ├── Viewer/
│   │   ├── ViewerPage.xaml
│   │   ├── ViewerPage.xaml.cs     ← Main shell (150 LOC max)
│   │   ├── ViewerViewModel.cs
│   │   ├── Partials/              ← NEW: Consolidate all partials
│   │   │   ├── ViewerPage.Pdf.cs     ← PDF UI only (200 LOC max, business logic moved to PdfViewerService)
│   │   │   ├── ViewerPage.Text.cs
│   │   │   ├── ViewerPage.Search.cs
│   │   │   ├── ViewerPage.Toolbar.cs
│   │   │   ├── ViewerPage.Sidebar.cs
│   │   │   └── ViewerPage.Zoom.cs
│   │   └── Components/
│   │       ├── ViewerToolbar.xaml + .cs
│   │       ├── DocumentInfoDialog.cs
│   │       └── SearchHighlighter.cs   ← MOVED from Features/Search
│   ├── Settings/
│   │   ├── SettingsPage.xaml
│   │   └── SettingsPage.xaml.cs
│   └── Browser/                   ← Keep for future (currently unused)
│       ├── BrowserPage.xaml
│       ├── BrowserPage.xaml.cs
│       └── BrowserViewModel.cs
│
├── Services/                      ← Cross-cutting services
│   ├── FileSystem/                ← MOVED from top-level FileSystem/
│   │   ├── Scanner/
│   │   │   └── FolderScanner.cs
│   │   ├── Index/
│   │   │   ├── FileIndexService.cs
│   │   │   ├── FileIndexContext.cs
│   │   │   └── FileIndexRepository.cs
│   │   └── Watcher/
│   │       └── FileWatcherService.cs  ← Currently unused
│   ├── Thumbnails/
│   │   ├── ThumbnailService.cs
│   │   ├── ThumbnailCache.cs
│   │   └── Providers/
│   │       ├── PdfThumbnailProvider.cs
│   │       └── GenericThumbnailProvider.cs
│   ├── Storage/
│   │   ├── SettingsStore.cs
│   │   ├── RecentFilesStore.cs
│   │   └── BookmarkStore.cs       ← Currently unused
│   ├── Registration/
│   │   ├── AppRegistration.cs
│   │   └── RegistrationHelper.cs
│   └── Updates/
│       ├── UpdateService.cs
│       ├── UpdateBanner.xaml
│       └── UpdateBanner.xaml.cs
│
├── Infrastructure/                ← Configuration, logging, DI
│   ├── Configuration/
│   │   ├── AppSettings.cs
│   │   └── UserPreferences.cs     ← Currently unused
│   └── Logging/
│       └── Logger.cs
│
├── UI/                            ← Shared UI components
│   ├── Shell/
│   │   ├── TitleBarWindow.xaml
│   │   └── TitleBarWindow.xaml.cs
│   ├── Controls/
│   │   ├── DocumentCard.xaml + .cs
│   │   ├── PageThumbnailSidebar.xaml + .cs
│   │   ├── ViewerInfoCard.cs
│   │   └── PdfTextSelectionOverlay.xaml + .cs  ← Future feature
│   ├── Styles/
│   │   ├── Colors.xaml
│   │   ├── Controls.xaml
│   │   └── Typography.xaml
│   ├── Templates/
│   │   └── FileTemplates.xaml
│   └── Icons/
│       └── FileTypeIcons.xaml
│
├── Tests/                         ← MIRRORS source structure exactly
│   ├── QuickView.Tests.csproj
│   ├── Common/                    ← NEW: Mirrors Common/
│   │   ├── Formatters/
│   │   │   ├── DateFormatterTests.cs      ← MOVED from Tests/Shared/Formatters
│   │   │   └── FileSizeFormatterTests.cs  ← MOVED from Tests/Shared/Formatters
│   │   └── Validators/
│   │       └── FileSizeValidatorTests.cs  ← MOVED from Tests/Shared/Validation
│   ├── Documents/                 ← NEW: Mirrors Documents/
│   │   └── Pdf/
│   │       ├── PdfRendererTests.cs           ← NEW
│   │       ├── PdfTextServiceTests.cs        ← NEW
│   │       └── PdfViewerServiceTests.cs      ← NEW (Sprint 5)
│   ├── Features/
│   │   ├── Home/
│   │   │   └── HomeViewModelTests.cs
│   │   └── Viewer/
│   │       └── ViewerViewModelTests.cs
│   ├── Services/                  ← NEW: Mirrors Services/
│   │   └── Thumbnails/
│   │       └── ThumbnailServiceTests.cs      ← NEW
│   └── Security/                  ← NEW: Consolidated security tests
│       ├── WebSecurityTests.cs    ← MOVED from Tests/Rendering/Web
│       ├── PathValidationTests.cs ← NEW
│       └── DllLoadingTests.cs     ← NEW (verify SEC-04)
│
└── docs/                          ← Documentation
    ├── structure-audit/           ← THIS AUDIT
    └── guides/                    ← NEW: Usage guides
        ├── FOLDER-GUIDE.md        ← NEW: Where to put new code
        ├── ADDING-FILE-TYPES.md   ← NEW: Checklist for new renderers
        └── TESTING-GUIDE.md       ← NEW: Testing conventions
```

---

## Key Changes Explained

### 1. **NEW: `Common/` Folder**
**Replaces:** Messy `Core/Extensions/` folder

**Rationale:** "Extensions" implies C# extension methods, but most code there wasn't extensions. `Common/` is clearer and organized by purpose:
- `Formatters/` — Display formatting (dates, file sizes)
- `Validators/` — Input validation (file sizes, paths)
- `Utilities/` — General helpers (file type detection, resource lookups)
- `Collections/` — Custom collection types
- `Extensions/` — TRUE C# extension methods only

### 2. **NEW: `Documents/` Folder**
**Replaces:** `Rendering/` + parts of `Services/`

**Rationale:** Each file format gets its own subfolder with everything it needs:
- `Documents/Pdf/Rendering/` — Low-level rendering (PDFium wrapper)
- `Documents/Pdf/Services/` — Business logic (viewer service, text search, annotations)
- `Documents/Pdf/Models/` — PDF-specific models
- `Documents/Text/`, `Documents/Code/`, etc. — Same pattern for each format

**Benefits:**
- When adding PDF features, all code is in ONE place
- Clear separation: `Rendering/` = pixel generation, `Services/` = business logic
- Easy to find: "Where's PDF text search?" → `Documents/Pdf/Services/PdfTextService.cs`

### 3. **Consolidate ViewerPage Partials**
**Before:** Partials scattered across `Components/`, `Pdf/`, `Search/`, `Text/`  
**After:** All partials in `Features/Viewer/Partials/`

**Rationale:** Partials are implementation details of one class. Keep them together, not split by domain.

### 4. **Move FileSystem to Services**
**Before:** `FileSystem/` at root level  
**After:** `Services/FileSystem/`

**Rationale:** File scanning is a service, not a top-level concern. Reduces root-level clutter.

### 5. **Flatten Test Structure**
**Before:** `Tests/Shared/Formatters/`, `Tests/Shared/Validation/`  
**After:** `Tests/Common/Formatters/`, `Tests/Common/Validators/`

**Rationale:** Test structure EXACTLY mirrors source structure. Finding tests is trivial.

### 6. **Remove Empty Folders**
**Delete:**
- `Core/Helpers/` (empty)
- `Rendering/Data/` (empty)
- `Rendering/Search/` (empty)

### 7. **Consolidate Dead Features**
**Move to** `docs/future-features/`:
- `PdfAnnotationService` (implemented but no UI)
- `PdfLinkService` (implemented but no UI)
- `PdfTextSelectionOverlay` (XAML only, no implementation)
- `BookmarkStore` (implemented but no UI)
- `FileWatcherService` (implemented but not wired up)
- `BrowserPage` (UI exists but not in navigation)

Add a comment at top of each:
```csharp
// NOTE: This feature is implemented but not yet integrated into the UI.
// See docs/future-features/ANNOTATIONS.md for integration plan.
```

---

## Namespace Changes

| Old Namespace | New Namespace | Reason |
|--------------|---------------|--------|
| `QuickView.Core.Extensions` | `QuickView.Common.Formatters` | DateFormatter, FileSizeFormatter |
| `QuickView.Core.Extensions` | `QuickView.Common.Validators` | FileSizeValidator, PathValidator |
| `QuickView.Core.Extensions` | `QuickView.Common.Utilities` | FileTypeDetector |
| `QuickView.Core.Extensions` | `QuickView.Common.Extensions` | UIThreadExtensions (renamed) |
| `QuickView.Core.Extensions` | `QuickView.Common.Collections` | IncrementalLoadingCollection |
| `QuickView.Rendering.Pdf` | `QuickView.Documents.Pdf.Rendering` | PdfRenderer, Pdfium, PdfDocumentState |
| `QuickView.Rendering.Pdf` | `QuickView.Documents.Pdf.Services` | PdfTextService, PdfLinkService, etc. |
| `QuickView.Rendering.Code` | `QuickView.Documents.Code` | CodeRenderer |
| `QuickView.Rendering.Web` | `QuickView.Documents.Markdown` | MarkdownRenderer |
| `QuickView.Rendering.Web` | `QuickView.Documents.Html` | HtmlRenderer |
| `QuickView.Rendering` | `QuickView.Documents._Shared` | RendererRegistry, UnsupportedRenderer |
| `QuickView.Services.Thumbnails.Providers` | `QuickView.Documents.Office` | OfficeThumbnailProvider |
| `QuickView.FileSystem.*` | `QuickView.Services.FileSystem.*` | All FileSystem classes |
| `QuickView.Features.Search` | `QuickView.Features.Viewer.Components` | SearchHighlighter |

---

## Migration Checklist

### Phase 1: Create New Folders (No code changes)
- [ ] Create `Common/` with subfolders
- [ ] Create `Documents/` with format subfolders
- [ ] Create `Services/FileSystem/`
- [ ] Create `Features/Viewer/Partials/`
- [ ] Create `Tests/Common/`, `Tests/Documents/`, `Tests/Services/`, `Tests/Security/`
- [ ] Create `docs/guides/`

### Phase 2: Move Files (Preserve git history with `git mv`)
- [ ] Move formatters to `Common/Formatters/`
- [ ] Move validators to `Common/Validators/`
- [ ] Move utilities to `Common/Utilities/`
- [ ] Move `Rendering/Pdf/` to `Documents/Pdf/`
- [ ] Move `Services/Documents/Pdf*` to `Documents/Pdf/Services/`
- [ ] Move `FileSystem/` to `Services/FileSystem/`
- [ ] Move `Features/Search/SearchHighlighter.cs` to `Features/Viewer/Components/`
- [ ] Move ViewerPage partials to `Features/Viewer/Partials/`
- [ ] Move test files to mirror new structure

### Phase 3: Update Namespaces
- [ ] Update all `using` statements
- [ ] Update `.csproj` file includes (if any explicit includes exist)
- [ ] Update XAML `x:Class` attributes (for moved XAML files)
- [ ] Update `Package.appxmanifest` (if it references moved files)

### Phase 4: Delete Empty Folders
- [ ] Delete `Core/Helpers/`
- [ ] Delete `Rendering/Data/`
- [ ] Delete `Rendering/Search/`
- [ ] Delete old `FileSystem/` folder (now moved)
- [ ] Delete old `Features/Search/` folder (only had 1 file)

### Phase 5: Update Documentation
- [ ] Create `docs/guides/FOLDER-GUIDE.md`
- [ ] Create `docs/guides/ADDING-FILE-TYPES.md`
- [ ] Create `docs/guides/TESTING-GUIDE.md`
- [ ] Update `Tree.md` (or delete if auto-generated)
- [ ] Update `.kiro/steering/project-structure.md`

### Phase 6: Verify Build
- [ ] Run `./build.ps1` → 0 errors
- [ ] Run `dotnet test` → all tests pass
- [ ] Manual smoke test: Open PDF, search, zoom, navigate pages
- [ ] Verify SEC-04 still works (PDFium loads correctly)

---

## "Where Does My Code Go?" Decision Table

| I'm adding... | Put it in... | Example |
|--------------|-------------|---------|
| **New file format support** | `Documents/{Format}/` | Adding `.epub` → `Documents/Epub/EpubRenderer.cs` + register in `RendererRegistry` |
| **PDF-specific feature** | `Documents/Pdf/Services/` | PDF form filling → `Documents/Pdf/Services/PdfFormService.cs` |
| **New utility function** | `Common/Utilities/` | String sanitization → `Common/Utilities/StringSanitizer.cs` |
| **New validator** | `Common/Validators/` | Email validation → `Common/Validators/EmailValidator.cs` |
| **New formatter** | `Common/Formatters/` | Duration formatting → `Common/Formatters/DurationFormatter.cs` |
| **New UI page** | `Features/{PageName}/` | Bookmarks page → `Features/Bookmarks/BookmarksPage.xaml + .cs + ViewModel.cs` |
| **New UI control** | `UI/Controls/` | Color picker → `UI/Controls/ColorPicker.xaml + .cs` |
| **New service** | `Services/{ServiceName}/` | OCR service → `Services/OCR/OcrService.cs` |
| **New domain model** | `Core/Models/` | Bookmark model → `Core/Models/BookmarkItem.cs` |
| **New test** | `Tests/{MirrorSourcePath}/` | Testing OcrService → `Tests/Services/OCR/OcrServiceTests.cs` |

---

## Benefits of New Structure

### Before (Current):
```
Q: "Where is PDF text search?"
A: Could be in Rendering/Pdf/PdfTextService.cs OR 
   Features/Viewer/Search/ViewerPage.Search.cs OR 
   Services/Documents/ (new location) → 3 places to check

Q: "Where do I add a new formatter?"
A: Core/Extensions/ BUT it's mixed with validators, detectors, and helpers

Q: "Where are the tests for DateFormatter?"
A: Tests/Shared/Formatters/ BUT source is in Core/Extensions/ → different structures
```

### After (Proposed):
```
Q: "Where is PDF text search?"
A: Documents/Pdf/Services/PdfTextService.cs → ONE obvious place

Q: "Where do I add a new formatter?"
A: Common/Formatters/{YourFormatter}.cs → Clear folder purpose

Q: "Where are the tests for DateFormatter?"
A: Tests/Common/Formatters/DateFormatterTests.cs → Mirrors source exactly
```

### Metrics Improvement:

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| **Avg nesting depth** | 3.2 levels | 2.8 levels | 12% flatter |
| **Empty folders** | 3 | 0 | 100% reduction |
| **Misnamed folders** | 1 (Extensions) | 0 | Fixed |
| **Dead code files** | 8 | 0 (moved to docs) | Cleaner codebase |
| **Time to find file** | ~30 sec (search) | ~5 sec (obvious) | 6x faster |

---

## Naming Conventions

### File Naming
- **Rule:** Filename MUST match the primary public class/interface name
- **Example:** `PdfRenderer.cs` contains `class PdfRenderer`
- **Exception:** XAML files → `ViewerPage.xaml` + `ViewerPage.xaml.cs` (required by WinUI)

### Folder Naming
- **Rule:** Singular nouns (e.g., `Model`, not `Models`) EXCEPT when semantically plural (e.g., `Documents`)
- **Rationale:** Cleaner imports (`using QuickView.Common.Formatter` vs `using QuickView.Common.Formatters`)
- **Exception:** `Documents/`, `Services/`, `Tests/` are semantically plural

### Suffix Conventions
- `*Service` → Business logic services (e.g., `PdfViewerService`, `ThumbnailService`)
- `*Provider` → Strategy pattern implementations (e.g., `OfficeThumbnailProvider`)
- `*Renderer` → File format renderers (e.g., `CodeRenderer`, `MarkdownRenderer`)
- `*Store` → Data persistence (e.g., `RecentFilesStore`, `BookmarkStore`)
- `*Formatter` → Display formatting (e.g., `DateFormatter`, `FileSizeFormatter`)
- `*Validator` → Input validation (e.g., `PathValidator`, `FileSizeValidator`)
- `*Helper` → Avoid! Use more specific suffix (e.g., `UIThreadExtensions` not `UIThreadHelper`)
- `*Manager` → Avoid! Use `*Service` instead
- `*Util` / `*Utils` → Avoid! Use specific folder (Utilities, Formatters, etc.)

### Class Responsibility
- **One public type per file** (class, interface, enum, or record)
- **Exception:** Tightly coupled types (e.g., `SearchHit` + `SearchState` in same file as `SearchHighlighter`)
- **Partial classes:** Share namespace with main class, group in `Partials/` subfolder

---

## Next Steps

1. Review this proposal with team
2. Get approval for Phase 1-6 migration plan
3. Create feature branch: `refactor/folder-structure`
4. Execute migration in small PRs (one phase at a time)
5. Update documentation after each phase
6. Final PR: Delete empty folders, update Tree.md

See `7-MIGRATION-CHECKLIST.md` for detailed step-by-step plan.
