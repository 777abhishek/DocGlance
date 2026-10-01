# QuickView — Refactoring Roadmap

> Audit date: 2026-10-01  
> Read-only planning document. No source files were modified.

---

## Refactoring Phases

---

### Phase 0 — Preparation (0 risk)

**Goal:** Establish testing foundation and dependency baseline.

**Tasks:**
- [ ] Add `xUnit` or `MSTest` project: `QuickView.Tests`
- [ ] Add test for `FileSizeValidator`, `PathValidator`, `FileSizeFormatter`, `DateFormatter` (pure functions)
- [ ] Document existing component dependencies (generate diagram)
- [ ] Add XML doc comments to all public interfaces
- [ ] Add `Microsoft.Extensions.DependencyInjection` package

**Affected files:** None (additive only)  
**Risk:** None  
**Effort:** 1-2 days  
**Benefit:** Foundation for subsequent phases  
**Prerequisites:** None

---

### Phase 1 — Extract HomeViewModel (low risk)

**Goal:** Move HomePage scan/filter logic into a testable ViewModel.

**Tasks:**
- [ ] Create `Features/Home/HomeViewModel.cs` inheriting `ObservableObject`
- [ ] Move scan state (`_scanTokens`, `_isScanning`) into ViewModel
- [ ] Move filter/sort properties (`_currentFilter`, `_sortMode`) into ViewModel
- [ ] Move extension sets (`ImageExts`, `VideoExts`, etc.) to `Shared/Constants/FileTypeConstants.cs`
- [ ] Add ViewModel to `HomePage` constructor; bind in XAML
- [ ] Write unit tests for ViewModel (scan logic, filter logic)

**Affected files:**
- `Features/Home/HomePage.xaml.cs` (shrink from ~500 to ~150 lines)
- `Features/Home/HomeViewModel.cs` (new, ~300 lines)
- `Features/Home/HomePage.xaml` (update bindings)
- `Shared/Constants/FileTypeConstants.cs` (new)

**Risk:** Low — isolated to Home page  
**Effort:** 2-3 days  
**Benefit:** HomePage becomes testable; reduces procedural code  
**Prerequisites:** Phase 0

---

### Phase 2 — Activate ViewerViewModel (medium risk)

**Goal:** Use the existing (but inactive) ViewerViewModel to hold page/zoom/doc state.

**Tasks:**
- [ ] Move `_doc`, `_page`, `_total`, `_zoom`, `_rotation` from `ViewerPage` partials into `ViewerViewModel`
- [ ] Change `ViewerViewModel` properties to `[ObservableProperty]` or full `INotifyPropertyChanged` implementations
- [ ] Update XAML bindings to read from `{x:Bind ViewModel.CurrentPage}` instead of code-behind properties
- [ ] Move navigation commands (`NextPage`, `PrevPage`, `GoToPage`) into ViewModel
- [ ] Add relay commands for zoom in/out/reset
- [ ] Write unit tests for page navigation, zoom math

**Affected files:**
- `Features/Viewer/ViewerViewModel.cs` (~200 lines)
- `Features/Viewer/ViewerPage.xaml` (update bindings)
- All 8 `ViewerPage.*.cs` partials (read from `ViewModel.*` instead of `_*`)

**Risk:** Medium — touches all ViewerPage partials; potential binding regressions  
**Effort:** 3-4 days  
**Benefit:** Viewer state becomes testable; clear separation UI ↔ state  
**Prerequisites:** Phase 1 (to validate ViewModel pattern works)

---

### Phase 3 — Extract PdfViewerService (medium risk)

**Goal:** Move PDF rendering orchestration out of ViewerPage into a dedicated service.

**Tasks:**
- [ ] Create `Application/Services/PdfViewerService.cs`
- [ ] Move prefetch logic from `ViewerPage.Pdf.cs` into service
- [ ] Move eviction logic into service
- [ ] Move re-render-at-zoom logic into service
- [ ] Service exposes methods: `LoadDocumentAsync`, `RenderPageAsync`, `PrefetchAroundAsync`, `EvictFarPagesAsync`
- [ ] `ViewerPage.Pdf.cs` calls the service, receives `WriteableBitmap?`, applies to UI
- [ ] Add unit tests for prefetch, eviction, zoom-render strategies

**Affected files:**
- `Application/Services/PdfViewerService.cs` (new, ~400 lines)
- `Features/Viewer/Components/ViewerPage.Pdf.cs` (shrink from ~500 to ~150 lines)

**Risk:** Medium — core PDF rendering flow; requires careful regression testing  
**Effort:** 4-5 days  
**Benefit:** PDF rendering becomes unit-testable; ViewerPage becomes thinner  
**Prerequisites:** Phase 2 (ViewModel must be active to hold state)

---

### Phase 4 — Fix IFileRenderer interface (low risk, wide impact)

**Goal:** Decouple renderer contract from `UIElement`.

**Tasks:**
- [ ] Create `Application/Models/RenderResult.cs` record:
  ```csharp
  public record RenderResult(Stream? ContentStream, string MimeType, IDictionary<string, object>? Metadata);
  ```
- [ ] Create new interface: `Application/Interfaces/IDocumentRenderer.cs`:
  ```csharp
  Task<RenderResult?> RenderAsync(string filePath, CancellationToken ct);
  ```
- [ ] Migrate `PdfRenderer`, `CodeRenderer`, `TextFileReader`, `MarkdownRenderer`, `HtmlRenderer` to return `RenderResult` instead of `UIElement`
- [ ] `ViewerPage.LoadDocumentAsync` interprets `RenderResult` and constructs the appropriate WinUI control
- [ ] Mark old `IFileRenderer` as `[Obsolete]` for one release, then delete

**Affected files:**
- `Application/Interfaces/IDocumentRenderer.cs` (new)
- `Application/Models/RenderResult.cs` (new)
- All renderers in `Rendering/*/` (modify return type)
- `Features/Viewer/ViewerPage.xaml.cs` (modify consumption)
- `Rendering/RendererRegistry.cs` (change generic type)

**Risk:** Low — well-scoped change; breaking change but limited to internal code  
**Effort:** 3-4 days  
**Benefit:** Renderers become UI-framework-agnostic; testable without XAML  
**Prerequisites:** Phase 3 (PDF rendering already refactored)

---

### Phase 5 — Replace MarkdownRenderer with Markdig (low risk)

**Goal:** Eliminate custom Markdown parser; use battle-tested library.

**Tasks:**
- [ ] Add NuGet package: `Markdig`
- [ ] Rewrite `MarkdownRenderer.cs` to use `Markdown.ToHtml(text, pipeline)`
- [ ] Configure Markdig pipeline:
  ```csharp
  var pipeline = new MarkdownPipelineBuilder()
      .DisableHtml()  // block inline HTML
      .Build();
  ```
- [ ] Add CSP `<meta>` tag to generated HTML output
- [ ] Add URL validation to reject `javascript:`, `data:`, `vbscript:` schemes
- [ ] Test with malicious Markdown samples

**Affected files:**
- `Rendering/Web/MarkdownRenderer.cs` (replace 300+ lines with ~50)
- `QuickView.csproj` (add `Markdig` package)

**Risk:** Low — isolated to Markdown rendering; no other dependencies  
**Effort:** 1-2 days  
**Benefit:** Eliminates SEC-02 XSS vulnerability; more robust parsing  
**Prerequisites:** None (can be done independently)

---

### Phase 6 — Introduce Dependency Injection (medium risk, wide impact)

**Goal:** Replace static service classes with injectable instances.

**Tasks:**
- [ ] In `App.xaml.cs`, create `ServiceCollection` and register:
  ```csharp
  services.AddSingleton<IFileIndexService, FileIndexService>();
  services.AddSingleton<IBookmarkService, BookmarkService>();
  services.AddSingleton<ISettingsStore, SettingsStore>();
  services.AddSingleton<IThumbnailService, ThumbnailService>();
  services.AddTransient<HomeViewModel>();
  services.AddTransient<ViewerViewModel>();
  ```
- [ ] Change all static service classes to instance classes with interface abstractions
- [ ] Update `TitleBarWindow`, `HomePage`, `ViewerPage` to receive services via constructor injection
- [ ] Create `ILogger` interface to abstract `Logger` static class
- [ ] Update all call sites to receive injected dependencies

**Affected files:**
- `App.xaml.cs` (add DI composition root)
- All service classes in `Infrastructure/` (convert static → instance)
- All pages in `Features/` (add constructor parameters)
- All tests (inject mocks instead of using static classes)

**Risk:** Medium — touches almost every service call site; risk of missed references  
**Effort:** 5-7 days  
**Benefit:** Enables full unit testing; eliminates hidden coupling  
**Prerequisites:** Phases 1-4 (ViewModels and services already extracted)

---

### Phase 7 — Harden HTML/Markdown security (medium risk)

**Goal:** Eliminate SEC-01, SEC-02, SEC-08 vulnerabilities.

**Tasks:**
- [ ] In `HtmlRenderer.cs`, disable JavaScript:
  ```csharp
  await wv.EnsureCoreWebView2Async();
  wv.CoreWebView2.Settings.IsScriptEnabled = false;
  wv.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
  ```
- [ ] Add `NavigationStarting` handler to block non-file:// navigation
- [ ] In `MarkdownRenderer.cs`, add CSP `<meta>` tag:
  ```html
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src 'self' data:;">
  ```
- [ ] Add URL scheme validation before inserting into `<a href>` and `<img src>`
- [ ] Test with malicious HTML/MD samples

**Affected files:**
- `Rendering/Web/HtmlRenderer.cs`
- `Rendering/Web/MarkdownRenderer.cs`

**Risk:** Medium — could break legitimate use-cases if CSP is too strict  
**Effort:** 2-3 days  
**Benefit:** Closes 3 High-severity security findings  
**Prerequisites:** Phase 5 (Markdig already integrated)

---

### Phase 8 — Harden native DLL loading (low risk)

**Goal:** Eliminate SEC-04 DLL hijacking risk.

**Tasks:**
- [ ] In `Pdfium.cs`, add static constructor:
  ```csharp
  static Pdfium()
  {
      var dllPath = Path.Combine(AppContext.BaseDirectory, "pdfium.dll");
      NativeLibrary.Load(dllPath);
  }
  ```
- [ ] Remove `[DllImport("pdfium")]` and replace with `[DllImport("pdfium.dll", ...)]` (explicit `.dll` extension)
- [ ] Test on clean machine to ensure `pdfium.dll` is found

**Affected files:**
- `Rendering/Pdf/Pdfium.cs` (add static constructor)

**Risk:** Low — explicit path reduces risk; fallback to current behavior if load fails  
**Effort:** 1 day  
**Benefit:** Closes SEC-04 Medium-severity finding  
**Prerequisites:** None (can be done independently)

---

### Phase 9 — Add SHA-256 installer verification (low risk)

**Goal:** Eliminate SEC-03 installer integrity risk.

**Tasks:**
- [ ] In `UpdateService.cs`, after download:
  ```csharp
  using var sha = SHA256.Create();
  using var fs = File.OpenRead(tempPath);
  var computed = Convert.ToHexString(sha.ComputeHash(fs));
  if (!computed.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("Checksum mismatch");
  ```
- [ ] Modify GitHub release process to publish SHA-256 checksum alongside `.exe`
- [ ] Add `Sha256` property to `ReleaseInfo` model
- [ ] Parse checksum from GitHub release assets or description

**Affected files:**
- `Infrastructure/Update/UpdateService.cs` (add verification)
- `Infrastructure/Update/ReleaseInfo.cs` (add `Sha256` property)
- GitHub Actions workflow (publish checksum)

**Risk:** Low — additive verification step; does not break existing flow  
**Effort:** 1-2 days  
**Benefit:** Closes SEC-03 High-severity finding  
**Prerequisites:** None (can be done independently)

---

### Phase 10 — Apply PathValidator in FolderScanner (low risk)

**Goal:** Eliminate SEC-05 symlink bypass.

**Tasks:**
- [ ] In `FolderScanner.ScanDir`, before calling `DocumentItem.FromFileInfo`:
  ```csharp
  if (!PathValidator.IsValidFilePath(fi.FullName, out _))
  {
      Logger.Log($"[SCAN] Skipped invalid path: {fi.Name}");
      continue;
  }
  ```
- [ ] Test with folder containing symlinks, junctions, UNC paths

**Affected files:**
- `FileSystem/Scanner/FolderScanner.cs` (add validation)

**Risk:** Low — additive check; no breaking changes  
**Effort:** 0.5 days  
**Benefit:** Closes SEC-05 Medium-severity finding  
**Prerequisites:** None (can be done independently)

---

## Priority Matrix

| Phase | Benefit | Risk | Effort | Priority |
|---|---|---|---|---|\
| Phase 0 | Foundation | None | Low | P0 (prerequisite) |
| Phase 1 | High | Low | Low | P1 |
| Phase 5 | High (security) | Low | Low | P1 |
| Phase 8 | Medium (security) | Low | Low | P1 |
| Phase 9 | High (security) | Low | Low | P1 |
| Phase 10 | Medium (security) | Low | Low | P1 |
| Phase 2 | High | Medium | Medium | P2 |
| Phase 3 | High | Medium | High | P2 |
| Phase 7 | High (security) | Medium | Medium | P2 |
| Phase 4 | Medium | Low | Medium | P3 |
| Phase 6 | High | Medium | High | P3 |

**Recommended sequence:**
1. Phase 0 (foundation)
2. Phases 1, 5, 8, 9, 10 (quick wins: HomeViewModel + security fixes)
3. Phases 2, 7 (ViewerViewModel + HTML/Markdown hardening)
4. Phase 3 (PdfViewerService extraction)
5. Phase 4 (IDocumentRenderer interface fix)
6. Phase 6 (full DI introduction)

---

## Rollback Strategy

Each phase should be completed in a dedicated feature branch:
```
refactor/phase-01-home-viewmodel
refactor/phase-02-viewer-viewmodel
etc.
```

If a phase introduces regressions:
1. Revert the merge commit on `main`
2. Fix the issue in the feature branch
3. Re-test before merging again

All phases except Phase 6 (DI) are **incrementally reversible** — rolling back one phase does not require rolling back previous phases.
