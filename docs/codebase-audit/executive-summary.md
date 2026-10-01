# QuickView — Executive Summary

> Audit date: 2026-10-01  
> Read-only static analysis. No source files were modified.

---

## Current State

QuickView is a **WinUI 3 desktop document viewer** for PDF, code, text, Markdown, HTML, and Office files. It uses PDFium (native library via P/Invoke) for PDF rendering, WebView2 for HTML/Markdown, and custom syntax-colored RichTextBlock for code.

The project is a **single-project solution** (~15,000 lines of C# code) with a feature-based folder structure. The architecture combines partial MVVM (ViewModels exist but are mostly unused), procedural UI code-behind, and static service classes. There is **no dependency injection container**.

**Current architecture:**
```
Shell/ → Features/ → Rendering/ + Infrastructure/ (via static classes)
```

**Key technical decisions observed:**
- Feature-based organisation: `Features/Home`, `Features/Viewer`, `Features/Browser`, `Features/Settings`
- Partial class decomposition: `ViewerPage` split into 8 partials by concern (Pdf, Text, Search, Sidebar, Toolbar, Zoom, Helpers)
- Security-first path handling: `PathValidator` rejects symlinks, junctions, UNC paths
- Provider pattern for thumbnails: `IThumbnailProvider` implementations routed through `ThumbnailService`
- Authenticode verification on installer downloads via `WinVerifyTrust`
- HKCU-only registry writes (no elevation required for file associations)
- SQLite index (EF Core) for scanned folders
- Log path sanitisation to remove usernames

**Build status:** 0 errors, 0 warnings (verified)  
**Deployment:** Inno Setup installer; unpackaged self-contained x64-only desktop app

---

## Main Architectural Problems

### 1. ViewerPage is a god class
`ViewerPage` and its 8 partial files (~1,500+ lines combined) contain all PDF rendering logic, text/code loading, search state, sidebar management, zoom computation, and bookmark handling. This:
- Makes unit testing impossible without a full XAML host
- Violates single responsibility principle
- Couples UI and business logic tightly

**Evidence:** `Features/Viewer/ViewerPage.xaml.cs` + 8 partials declare `_pdfState`, `_doc`, `_page`, `_zoom` as `internal` shared state.

### 2. ViewerViewModel is inactive
`ViewerViewModel` defines `Document`, `CurrentPage`, `TotalPages`, `Zoom` properties but `ViewerPage` maintains parallel `_doc`, `_page`, `_total`, `_zoom` fields in code-behind. The ViewModel is functionally dead — XAML does not bind to it.

### 3. No dependency injection
All services are static classes: `Logger`, `BookmarkStore`, `ThumbnailService`, `FileIndexService`, `AppSettings`. This:
- Prevents mock injection for testing
- Creates hidden coupling
- Makes runtime configuration impossible

### 4. Business logic in UI code
`ViewerPage.Pdf.cs`, `ViewerPage.Sidebar.cs`, `ViewerPage.Search.cs`, `ViewerPage.Zoom.cs` contain significant application logic (prefetch strategy, zoom anchoring, search debounce, bookmark management) that cannot be tested without WinUI XAML.

### 5. HomePage is a large procedural class
`HomePage.xaml.cs` (~500+ lines) directly manages scan state, filter/sort state, calls services, and contains file-type extension sets. There is no ViewModel.

### 6. IFileRenderer returns UIElement
The renderer interface (`IFileRenderer`) returns `UIElement?` — a WinUI presentation type — coupling the infrastructure layer directly to the UI framework. Renderers cannot be tested without constructing UI elements.

---

## Main Security Concerns

### High Severity (3 findings)

**SEC-01 — HtmlRenderer loads arbitrary local file:// URIs in WebView2**
- `HtmlRenderer.cs` navigates WebView2 to `file://` paths without disabling JavaScript
- Malicious HTML files can execute scripts, access local files, exfiltrate data
- **Mitigation:** Disable `IsScriptEnabled`, add `NavigationStarting` handler

**SEC-02 — MarkdownRenderer custom parser susceptible to XSS**
- Hand-rolled Markdown-to-HTML converter generates raw HTML with `javascript:` links and unescaped `href` attributes
- Crafted `.md` files can execute arbitrary scripts in WebView2
- **Mitigation:** Replace with `Markdig` library, add URL scheme validation, add CSP

**SEC-03 — Update installer downloaded without SHA-256 verification**
- Installer URL from GitHub API; no checksum verification before execution
- `WinVerifyTrust` checks Authenticode but does not prevent tampered download if attacker has valid cert
- **Mitigation:** Publish SHA-256 checksum, verify before launch

### Medium Severity (5 findings)

- **SEC-04:** `pdfium.dll` loaded by short name (DLL hijacking risk)
- **SEC-05:** `FolderScanner` does not validate paths before indexing (symlinks bypass validation)
- **SEC-06:** `BookmarkStore` writes full file paths without validation
- **SEC-07:** `WinVerifyTrust` disables revocation checking
- **SEC-08:** Markdown HTML output has no Content Security Policy

### Low Severity (4 findings)

- Path sanitisation only handles Windows-style paths
- Installer written to shared `%TEMP%` directory
- No input validation on registry values (benign)
- WebView2 instances not disposed on tab close

### Informational (4 findings)

- File size limits are constants, not configurable
- `AllowUnsafeBlocks` enabled project-wide
- Documentation inconsistencies

**Positive security findings:**
- `PathValidator` explicitly rejects symlinks, junctions, UNC paths via `GetFileAttributes` (FILE_ATTRIBUTE_REPARSE_POINT check)
- `FileSizeValidator` enforces per-type file size limits (500 MB for PDF, 1 GB general)
- Log path sanitisation removes usernames from log output
- Authenticode verification on downloaded installers
- HKCU-only registry writes (no elevation)
- No secrets found in source code or configuration

---

## Main Maintainability Problems

1. **Large god classes:** `ViewerPage` (~1,500 lines), `HomePage` (~500 lines), `TitleBarWindow` (~400+ lines)
2. **Inactive ViewModels:** `ViewerViewModel` exists but is not used; ViewModel pattern partially implemented
3. **Static service anti-pattern:** All services are static; cannot be mocked or injected
4. **Tight UI-logic coupling:** Business logic embedded in code-behind; cannot be tested without XAML host
5. **Custom Markdown parser:** 300+ lines of string manipulation prone to edge-case failures and XSS
6. **No test suite:** No unit tests found; manual testing only
7. **Static singleton:** `TitleBarWindow.Current` prevents multi-window support and testing
8. **Mixed concerns in Models/**: PDF-specific types (`PdfLink`, `PdfSearchResult`) leaked into shared models namespace

---

## Main Performance Concerns

1. **Prefetch strategy not parameterised:** Hard-coded ±2 page prefetch; no runtime tuning for low-memory devices
2. **Eviction based on page distance only:** Does not consider actual memory usage or rendered page sizes
3. **Thumbnail loading synchronous in UI thread:** `HomePage` awaits thumbnail loads sequentially; could batch
4. **No virtualization in thumbnail grid:** All thumbnails rendered at once; could cause memory pressure with large folders
5. **WebView2 instances never disposed:** Process continues running after tab close; memory leak
6. **Markdown rendering blocks UI thread:** Custom parser runs synchronously; should be moved to background
7. **FolderScanner parallel directory enumeration:** Uses `Parallel.ForEach` without MaxDegreeOfParallelism; could saturate I/O on HDDs

**Positive findings:**
- PDF rendering uses `ArrayPool<byte>.Shared` for raster buffers (good)
- TextFileReader caps at 1 MB with streaming (good)
- SQLite index reduces repeated scans (good)
- Thumbnail cache with LRU eviction at 500 MB (good)

---

## Recommended Target Architecture

**Pattern:** Pragmatic layered feature architecture within a single project (no separate assemblies).

**Dependency direction:**
```
Presentation (Features/, Controls/)
      ↓
Application (Services/, Interfaces/)
      ↓
Domain (Models/, pure business rules)

Infrastructure → Application interfaces (never upward to Presentation)
Shared         → used by any layer (no upward dependencies)
```

**Key changes:**
1. Activate `ViewerViewModel` to own page/zoom/doc state
2. Extract `HomeViewModel` for scan/filter logic
3. Extract `PdfViewerService` for rendering orchestration (prefetch, eviction, zoom)
4. Change `IFileRenderer` to return DTOs instead of `UIElement`
5. Introduce `Microsoft.Extensions.DependencyInjection` for service registration
6. Convert static service classes to injectable instances
7. Replace custom Markdown parser with `Markdig`
8. Disable JavaScript in `HtmlRenderer` WebView2

**Keep as single project:** No need for multiple `.csproj` files; enforce layering via folder conventions.

---

## Top Priorities

### Security (immediate)
1. **Disable JavaScript in HtmlRenderer** (1 day, closes SEC-01)
2. **Replace MarkdownRenderer with Markdig** (2 days, closes SEC-02)
3. **Add SHA-256 installer verification** (1 day, closes SEC-03)
4. **Restrict pdfium.dll loading to app directory** (1 day, closes SEC-04)
5. **Apply PathValidator in FolderScanner** (0.5 days, closes SEC-05)

### Architecture (short-term)
1. **Extract HomeViewModel** (2-3 days) — enables testability, reduces procedural code
2. **Activate ViewerViewModel** (3-4 days) — separates UI from state, enables testing
3. **Extract PdfViewerService** (4-5 days) — isolates rendering logic

### Architecture (medium-term)
1. **Introduce dependency injection** (5-7 days) — eliminates static services, enables mocking
2. **Fix IFileRenderer interface** (3-4 days) — decouples renderers from UI framework

---

## Refactoring Risk

**Low-risk quick wins (can be done independently):**
- Security fixes (immediate)
- HomeViewModel extraction
- Markdown → Markdig replacement

**Medium-risk foundational changes (require coordination):**
- ViewerViewModel activation — touches all 8 ViewerPage partials
- PdfViewerService extraction — core rendering flow
- Dependency injection introduction — touches every service call site

**Recommended sequence:**
1. Security fixes first (high benefit, low risk, independent)
2. HomeViewModel (validate ViewModel pattern works)
3. ViewerViewModel (major state refactor)
4. PdfViewerService (rendering logic extraction)
5. Full DI introduction (after services extracted)

**Rollback strategy:** Each phase in dedicated feature branch; phases are incrementally reversible (except full DI which is all-or-nothing).

---

## Overall Observations

**Strengths:**
- Clean feature-based organisation
- Explicit security validators for paths and file sizes
- Provider pattern for thumbnails and renderers
- Recent refactoring work has improved separation (Shared/ folder with formatters/validators)
- No third-party analytics or telemetry
- Build is clean (0 errors, 0 warnings)

**Weaknesses:**
- ViewModels defined but not used
- Business logic embedded in UI code-behind
- No dependency injection
- Large god classes (ViewerPage, HomePage)
- Custom Markdown parser with XSS vulnerability
- HTML files loaded in WebView2 with JavaScript enabled
- No unit tests

**Maturity level:** Early-stage open-source project with solid foundations but incomplete MVVM implementation and missing test coverage. Security posture is **above average** for an open-source viewer (explicit path validation, Authenticode verification) but has **3 High-severity vulnerabilities** in the HTML/Markdown rendering pipeline that should be addressed immediately.

**Recommended investment:**
- **Security fixes:** 5-6 days (highest priority)
- **ViewModel activation + service extraction:** 10-15 days (high impact on testability)
- **Full DI introduction:** 5-7 days (enables long-term maintainability)
- **Total refactoring effort:** 20-28 days (4-6 weeks)
