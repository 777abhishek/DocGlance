# QuickView — Architecture Recommendation

> Audit date: 2026-10-01  
> Read-only analysis. No source files were modified.

---

## 1. Current Architecture Assessment

### Pattern in use
The project follows a **feature-based structure** with partial MVVM, combined with a flat service-oriented approach. There is no formal layering or dependency injection container.

```
Shell/          — window chrome (correct layer)
Features/       — pages + partial ViewModels (correct concept, incomplete execution)
Rendering/      — rendering implementations (correct)
Infrastructure/ — persistence, logging, shell integration (correct)
Controls/       — reusable UI controls (correct)
Models/         — data models (mixed: domain + DTOs + view models)
Core/           — interfaces + enums + constants (partially correct)
Shared/         — utilities (correct)
Thumbnails/     — thumbnail system (correct)
FileSystem/     — scanning + indexing (correct)
```

### What is currently good

- **Feature folders exist**: `Features/Home`, `Features/Viewer`, `Features/Browser`, `Features/Settings` align with feature-based organisation.
- **Rendering is isolated**: `Rendering/Pdf`, `Rendering/Code`, `Rendering/Text`, `Rendering/Web` are well-separated by file type.
- **Infrastructure is named correctly**: logging, storage, configuration, registration, update checker live in `Infrastructure/`.
- **Security validators exist**: `PathValidator`, `FileSizeValidator` are dedicated classes.
- **Provider pattern**: `IThumbnailProvider` and `IFileRenderer` exist as clean interfaces.
- **PDFium encapsulation**: `Pdfium.cs` concentrates all P/Invoke declarations; `PdfDocumentState` correctly implements `IDisposable`.
- **Partial class decomposition**: ViewerPage is split into 8 partial files, keeping individual files readable.
- **`CommunityToolkit.Mvvm`** is used for the ViewModel, providing correct observable property generation.

### What is problematic

#### 1. ViewerPage is a god class
`ViewerPage` and its 8 partial files form one very large class (~1,500+ lines of combined code) that:
- Owns all PDF rendering logic
- Owns all text/code/markdown loading logic
- Owns all search state and highlight drawing
- Owns all sidebar, bookmark, and outline management
- Owns all zoom and fit-mode logic
- Calls services directly (no abstraction)

This makes unit testing impossible without a full XAML host.

Evidence:  
File: `Features/Viewer/ViewerPage.xaml.cs` + 8 partial files  
The `_pdfState`, `_doc`, `_page`, `_total`, `_zoom`, `_rotation` fields are all declared `internal` and shared across partials — they function as a single large mutable state object.

#### 2. No dependency injection container
All services are static classes (`Logger`, `BookmarkStore`, `ThumbnailService`, `FileIndexService`, `AppSettings`). There is no DI container registering or resolving dependencies. This:
- Makes testing difficult (cannot inject mocks)
- Creates hidden coupling between components
- Makes configuration injection impossible

#### 3. ViewerViewModel is not used
`ViewerViewModel` defines `Document`, `CurrentPage`, `TotalPages`, `Zoom` — but `ViewerPage` maintains its own parallel `_doc`, `_page`, `_total`, `_zoom` fields and does not bind to the ViewModel. The ViewModel is functionally dead.

Evidence:  
File: `Features/Viewer/ViewerViewModel.cs` — defines the properties.  
File: `Features/Viewer/ViewerPage.xaml.cs` — declares `internal DocumentItem? _doc`, `internal int _page`, `internal double _zoom` directly on the partial class without referencing the ViewModel.

#### 4. Business logic in UI code
`ViewerPage.Pdf.cs`, `ViewerPage.Sidebar.cs`, `ViewerPage.Search.cs`, `ViewerPage.Zoom.cs` contain significant business/application logic that cannot be tested without a WinUI XAML environment:
- Page prefetch strategy (which pages to render)
- Zoom computation and anchor anchoring
- Search debounce and result navigation
- Bookmark management
- Outline construction

These should live in service or ViewModel layers.

#### 5. HomePage.xaml.cs is a large procedural class
`HomePage.xaml.cs` directly:
- Manages scan state (`_scanTokens` dictionary)
- Manages filter/sort state
- Calls `FileIndexService`, `FolderScanner`, `ThumbnailService`
- Contains file-type extension sets (`ImageExts`, `VideoExts`, etc.)
- Has no ViewModel at all

Evidence: `Features/Home/HomePage.xaml.cs` (~500+ lines observed)

#### 6. Models contain PDF-specific types
`Models/PdfLink.cs` and `Models/PdfSearchResult.cs` are PDF rendering details that have leaked into the shared models namespace, creating a dependency from `Models/` on PDF-specific concerns.

#### 7. `IFileRenderer` returns `UIElement`
The renderer interface (`Core/Interfaces/IFileRenderer.cs`) returns `UIElement?` — a WinUI presentation type — from what should be an application-layer contract. This couples the rendering infrastructure directly to the UI framework.

#### 8. HtmlRenderer loads local file:// URIs directly in WebView2
`HtmlRenderer.cs` sets `wv.Source = new Uri(filePath)` — a local file URI. This bypasses validation and could render arbitrary HTML/JS from the filesystem. There is no sandbox check.

#### 9. MarkdownRenderer uses a hand-rolled parser
`MarkdownRenderer.cs` is a 300+ line custom Markdown-to-HTML converter with manual string manipulation. It is prone to XSS in rendered HTML and edge-case parsing failures.

#### 10. Static singleton anti-pattern
`TitleBarWindow.Current` is a static mutable singleton. This is a common WinUI pattern but makes testing and multi-window support impossible.

---

## 2. Proposed Target Architecture

### Design philosophy
The project is a **single-process desktop viewer**, not a distributed system. A full Clean Architecture with separate assemblies would be over-engineered. The recommendation is a **pragmatic layered feature architecture** within a single project, enforced by folder convention and namespace discipline rather than separate `.csproj` files.

### Proposed dependency direction

```
Presentation (Views, ViewModels, Controls)
      ↓
Application (Services, Use-cases, Interfaces)
      ↓
Domain (Models, pure business rules)

Infrastructure → Application interfaces (never upward to Presentation)
Shared         → used by any layer (no upward dependencies)
```

### Proposed folder structure

```
QuickView/
│
├── App.xaml[.cs]               — entry point, DI composition root
│
├── Shell/                      — window chrome only (no document logic)
│   └── TitleBarWindow.xaml[.cs]
│
├── Features/
│   ├── Home/
│   │   ├── HomePage.xaml[.cs]  — UI only; binds to HomeViewModel
│   │   └── HomeViewModel.cs    — scan state, filter, sort, thumbnail loading
│   ├── Viewer/
│   │   ├── ViewerPage.xaml[.cs] — UI only; binds to ViewerViewModel
│   │   ├── ViewerViewModel.cs   — owns page/zoom/doc state, commands
│   │   ├── PdfViewerService.cs  — rendering orchestration, prefetch, eviction
│   │   ├── SearchService.cs     — search state, navigation, debounce
│   │   └── Components/         — partial views (sidebar, toolbar, zoom pill)
│   ├── Browser/
│   │   ├── BrowserPage.xaml[.cs]
│   │   └── BrowserViewModel.cs
│   ├── Search/
│   │   └── SearchHighlighter.cs
│   └── Settings/
│       ├── SettingsPage.xaml[.cs]
│       └── SettingsViewModel.cs
│
├── Application/
│   ├── Interfaces/
│   │   ├── IDocumentRenderer.cs    — returns byte[]/stream, NOT UIElement
│   │   ├── IFileScanner.cs
│   │   ├── IThumbnailProvider.cs
│   │   └── IBookmarkService.cs
│   └── Services/
│       ├── DocumentService.cs      — open/validate/track documents
│       ├── RecentFilesService.cs
│       └── BookmarkService.cs
│
├── Domain/
│   ├── Models/
│   │   ├── DocumentItem.cs
│   │   ├── BookmarkItem.cs
│   │   ├── OutlineItem.cs
│   │   └── PageThumbnailItem.cs
│   └── Enums/
│       ├── FileCategory.cs
│       └── ViewMode.cs
│
├── Rendering/                  — all rendering stays here
│   ├── Pdf/
│   ├── Code/
│   ├── Text/
│   ├── Web/
│   ├── Office/
│   └── Unsupported/
│
├── Infrastructure/
│   ├── Configuration/
│   ├── FileSystem/
│   ├── Logging/
│   ├── Registration/
│   ├── Storage/
│   ├── Thumbnails/
│   └── Update/
│
├── Controls/                   — reusable WinUI controls only
│
└── Shared/                     — zero-dependency utilities
    ├── Collections/
    ├── Detection/
    ├── Formatters/
    ├── Threading/
    └── Validation/
```

### Dependency rules

| Layer | May reference | Must NOT reference |
|---|---|---|
| Domain | nothing | everything |
| Application | Domain, Shared | Rendering, Infrastructure, Presentation |
| Rendering | Domain, Shared | Application, Infrastructure (except Logging) |
| Infrastructure | Domain, Application interfaces, Shared | Presentation, Rendering |
| Features (Presentation) | Application, Domain, Rendering, Controls | Infrastructure (except via Application interfaces) |
| Controls | Domain, Shared | Features, Application, Infrastructure |
| Shared | nothing | everything |

---

## 3. Migration Strategy

### Phase 0 — No-op (already done in recent refactoring)
- Shared/ folder with formatters, validators, threading helpers ✓
- Thumbnails/ with provider pattern ✓
- Rendering/ folder separation ✓

### Phase 1 — Extract HomeViewModel (low risk)
- Move scan state, filter/sort, extension sets out of `HomePage.xaml.cs` into `HomeViewModel`
- `HomePage` binds to ViewModel; logic becomes testable

### Phase 2 — Activate ViewerViewModel (medium risk)
- `ViewerViewModel` already exists but is unused
- Move `_doc`, `_page`, `_total`, `_zoom` into it
- ViewerPage reads from/commands to ViewModel
- Enables unit tests for page navigation, zoom math

### Phase 3 — Extract PdfViewerService (medium risk)
- Move prefetch, eviction, render-at-zoom logic out of `ViewerPage.Pdf.cs` into a dedicated service
- ViewerPage calls the service and applies the returned bitmaps

### Phase 4 — Fix IFileRenderer interface (low risk)
- Change `IFileRenderer.RenderAsync` to return a DTO (`RenderResult`) instead of `UIElement?`
- ViewerPage is responsible for turning the DTO into UI elements

### Phase 5 — Introduce minimal DI (medium risk)
- Use `Microsoft.Extensions.DependencyInjection` as composition root in `App.xaml.cs`
- Register services as singletons/transients; inject via constructor

---

## 4. Single vs Multiple Projects

**Recommendation: remain a single project.**

The application is a desktop viewer with no reusable library surface. Splitting into multiple `.csproj` files would add significant build complexity for no tangible benefit. Enforce layering via namespace conventions and folder organisation. If in the future a NuGet package is desired (e.g., `QuickView.Rendering.Pdf`), extract at that point.

---

## 5. Alternatives Considered

| Alternative | Reason not recommended |
|---|---|
| Full Clean Architecture with 4+ assemblies | Over-engineered for a single-exe desktop viewer |
| Reactive (Rx.NET) approach | High learning curve, no existing Rx usage in codebase |
| MAUI migration | Not a cross-platform app; WinUI 3 is correct choice |
| Blazor Hybrid | No web team; WinUI 3 provides better Windows integration |
