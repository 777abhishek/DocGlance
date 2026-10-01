# QuickView — Implementation Checklist

> **Actionable task list for implementing audit recommendations**  
> Track progress by checking off items as they're completed

---

## 🚨 Sprint 1: Critical Security Fixes (5 days)

### SEC-01: Disable JavaScript in HtmlRenderer (0.5 days)
- [ ] Open `Rendering/Web/HtmlRenderer.cs`
- [ ] Add `await wv.EnsureCoreWebView2Async()` before setting Source
- [ ] Set `wv.CoreWebView2.Settings.IsScriptEnabled = false`
- [ ] Set `wv.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false`
- [ ] Add `NavigationStarting` event handler to block non-file:// navigation
- [ ] Test with sample HTML file containing `<script>` tags
- [ ] Verify JavaScript is blocked
- [ ] Commit with message: "security: disable JavaScript in HtmlRenderer (SEC-01)"

**Verification:**
```powershell
# Create test HTML file with script
echo '<html><body><script>alert("XSS")</script>Test</body></html>' > test-xss.html
# Open in QuickView — should show "Test" without alert
```

---

### SEC-02: Replace Markdown Parser with Markdig (2 days)
- [ ] Add NuGet package: `Markdig`
- [ ] Open `Rendering/Web/MarkdownRenderer.cs`
- [ ] Delete existing `InlineFormat` method and regex patterns
- [ ] Add Markdig pipeline configuration:
  ```csharp
  var pipeline = new MarkdownPipelineBuilder()
      .DisableHtml()
      .UseAdvancedExtensions()
      .Build();
  ```
- [ ] Replace `BuildHtml` implementation:
  ```csharp
  var html = Markdown.ToHtml(markdownText, pipeline);
  ```
- [ ] Add URL validation before inserting into HTML
- [ ] Add CSP meta tag to generated HTML:
  ```html
  <meta http-equiv="Content-Security-Policy" 
        content="default-src 'none'; style-src 'unsafe-inline'; img-src 'self' data:;">
  ```
- [ ] Test with various Markdown files (headings, lists, links, images, code blocks)
- [ ] Test with malicious Markdown: `[click](javascript:alert(1))`
- [ ] Verify links are sanitized
- [ ] Update `QuickView.csproj` if package reference not auto-added
- [ ] Commit with message: "security: replace custom Markdown parser with Markdig (SEC-02)"

**Verification:**
```powershell
# Create test Markdown file
@"
# Test
[safe link](https://example.com)
[unsafe link](javascript:alert(1))
"@ > test-xss.md
# Open in QuickView — javascript: link should be blocked
```

---

### SEC-03: Add SHA-256 Installer Verification (1 day)
- [ ] Open `Infrastructure/Update/ReleaseInfo.cs`
- [ ] Add property: `public string? Sha256 { get; set; }`
- [ ] Open `Infrastructure/Update/UpdateService.cs`
- [ ] In `FetchLatestReleaseAsync`, parse SHA-256 from GitHub release (asset or description)
- [ ] In `DownloadAndInstallAsync`, after download:
  ```csharp
  using var sha = SHA256.Create();
  using var fs = File.OpenRead(tempPath);
  var computed = Convert.ToHexString(sha.ComputeHash(fs));
  if (!computed.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("Installer checksum mismatch");
  ```
- [ ] Add logging for verification success/failure
- [ ] Test with mocked release data
- [ ] Update GitHub Actions workflow to publish SHA-256 checksum
- [ ] Update release documentation
- [ ] Commit with message: "security: add SHA-256 installer verification (SEC-03)"

**Note:** GitHub workflow update is separate from code changes.

---

### SEC-04: Fix pdfium.dll Loading (1 day)
- [ ] Open `Rendering/Pdf/Pdfium.cs`
- [ ] Add static constructor:
  ```csharp
  static Pdfium()
  {
      var dllPath = Path.Combine(AppContext.BaseDirectory, "pdfium.dll");
      if (File.Exists(dllPath))
      {
          NativeLibrary.Load(dllPath);
      }
  }
  ```
- [ ] Change all `[DllImport("pdfium")]` to `[DllImport("pdfium.dll")]`
- [ ] Test PDF rendering still works
- [ ] Test on clean machine (or with `pdfium.dll` removed from PATH)
- [ ] Commit with message: "security: restrict pdfium.dll loading to app directory (SEC-04)"

**Verification:**
```powershell
# Ensure pdfium.dll is in app directory
Test-Path (Join-Path $env:LOCALAPPDATA "QuickView\pdfium.dll")
# Open PDF — should work without pdfium.dll in PATH
```

---

### SEC-05: Add PathValidator to FolderScanner (0.5 days)
- [ ] Open `FileSystem/Scanner/FolderScanner.cs`
- [ ] Find `ScanDir` method
- [ ] Before `DocumentItem.FromFileInfo(fi)`, add:
  ```csharp
  if (!PathValidator.IsValidFilePath(fi.FullName, out var reason))
  {
      Logger.Log($"[SCAN] Skipped invalid path: {fi.Name} (Reason: {reason})");
      continue;
  }
  ```
- [ ] Test scanning folder with symlink (should skip)
- [ ] Test scanning folder with junction (should skip)
- [ ] Test scanning folder with normal files (should work)
- [ ] Verify logs show skipped items
- [ ] Commit with message: "security: apply PathValidator in FolderScanner (SEC-05)"

**Verification:**
```powershell
# Create test folder with symlink
mkdir test-scan
New-Item -ItemType SymbolicLink -Path "test-scan\symlink.txt" -Target "C:\Windows\System32\license.rtf"
echo "normal file" > test-scan\normal.txt
# Scan folder in QuickView — should see only normal.txt
```

---

## ✅ Sprint 1 Complete Criteria
- [ ] All 5 security fixes implemented
- [ ] All tests passed
- [ ] Code committed to feature branch
- [ ] Pull request created
- [ ] Security review completed
- [ ] Merged to main

---

## 🧪 Sprint 2: Testing Foundation (4 days)

### Phase 0: Add Test Project (1 day)
- [ ] Create new project: `QuickView.Tests`
- [ ] Add NuGet packages: `xUnit`, `xUnit.runner.visualstudio`, `FluentAssertions`, `Moq`
- [ ] Add project reference to `QuickView.csproj`
- [ ] Create `Shared/Validators/PathValidatorTests.cs`:
  ```csharp
  public class PathValidatorTests
  {
      [Fact]
      public void IsValidFilePath_RejectsSymlinks() { ... }
      
      [Fact]
      public void IsValidFilePath_RejectsJunctions() { ... }
      
      [Fact]
      public void IsValidFilePath_AcceptsNormalPaths() { ... }
  }
  ```
- [ ] Create `Shared/Formatters/FileSizeFormatterTests.cs`
- [ ] Create `Shared/Formatters/DateFormatterTests.cs`
- [ ] Run tests: `dotnet test`
- [ ] Verify all tests pass
- [ ] Add test results to CI/CD pipeline
- [ ] Commit with message: "test: add test project with initial unit tests (Phase 0)"

---

### Phase 1: Extract HomeViewModel (2-3 days)
- [ ] Create `Features/Home/HomeViewModel.cs` inheriting `ObservableObject`
- [ ] Move scan state from `HomePage.xaml.cs`:
  - [ ] `_scanTokens` → `ScanCancellationTokens`
  - [ ] `_isScanning` → `IsScanning` (observable property)
- [ ] Move filter/sort properties:
  - [ ] `_currentFilter` → `CurrentFilter`
  - [ ] `_sortMode` → `SortMode`
- [ ] Create `Shared/Constants/FileTypeConstants.cs`
- [ ] Move extension sets (`ImageExts`, `VideoExts`, etc.) to `FileTypeConstants`
- [ ] Update `HomePage.xaml.cs` to use ViewModel
- [ ] Update `HomePage.xaml` bindings:
  ```xml
  <ProgressRing IsActive="{x:Bind ViewModel.IsScanning, Mode=OneWay}" />
  ```
- [ ] Create `Features/Home/HomeViewModelTests.cs`
- [ ] Add tests for scan logic, filter logic
- [ ] Run tests: `dotnet test`
- [ ] Test HomePage in app (manual verification)
- [ ] Commit with message: "refactor: extract HomeViewModel (Phase 1)"

**Files modified:**
- New: `Features/Home/HomeViewModel.cs` (~300 lines)
- New: `Shared/Constants/FileTypeConstants.cs` (~50 lines)
- New: `QuickView.Tests/Features/Home/HomeViewModelTests.cs` (~100 lines)
- Modified: `Features/Home/HomePage.xaml.cs` (shrink from ~500 to ~150 lines)
- Modified: `Features/Home/HomePage.xaml` (update bindings)

---

## ✅ Sprint 2 Complete Criteria
- [ ] Test project created and running
- [ ] At least 10 unit tests passing
- [ ] HomeViewModel extracted and tested
- [ ] HomePage UI still works correctly
- [ ] Code coverage report generated
- [ ] Pull request created and merged

---

## 🏗️ Sprint 3: Activate ViewerViewModel (5 days)

### Phase 2: ViewerViewModel Activation (3-4 days)
- [ ] Open `Features/Viewer/ViewerViewModel.cs`
- [ ] Add observable properties:
  - [ ] `CurrentPage` (int)
  - [ ] `TotalPages` (int)
  - [ ] `ZoomLevel` (double)
  - [ ] `Rotation` (int)
  - [ ] `DocumentTitle` (string)
- [ ] Add commands:
  - [ ] `NextPageCommand` → `RelayCommand`
  - [ ] `PreviousPageCommand` → `RelayCommand`
  - [ ] `GoToPageCommand` → `RelayCommand<int>`
  - [ ] `ZoomInCommand` → `RelayCommand`
  - [ ] `ZoomOutCommand` → `RelayCommand`
  - [ ] `ResetZoomCommand` → `RelayCommand`
- [ ] Move state from `ViewerPage.xaml.cs`:
  - [ ] `_doc` → `ViewModel.Document`
  - [ ] `_page` → `ViewModel.CurrentPage`
  - [ ] `_total` → `ViewModel.TotalPages`
  - [ ] `_zoom` → `ViewModel.ZoomLevel`
  - [ ] `_rotation` → `ViewModel.Rotation`
- [ ] Update all 8 partial classes to read from ViewModel
- [ ] Update `ViewerPage.xaml` bindings:
  ```xml
  <TextBlock Text="{x:Bind ViewModel.CurrentPage, Mode=OneWay}" />
  <Slider Value="{x:Bind ViewModel.ZoomLevel, Mode=TwoWay}" />
  ```
- [ ] Update `ViewerToolbar.xaml.cs` to use ViewModel commands
- [ ] Create `Features/Viewer/ViewerViewModelTests.cs`
- [ ] Add tests for page navigation, zoom math
- [ ] Run tests: `dotnet test`
- [ ] Test Viewer in app (manual verification: open PDF, navigate pages, zoom)
- [ ] Commit with message: "refactor: activate ViewerViewModel (Phase 2)"

**Files modified:**
- Modified: `Features/Viewer/ViewerViewModel.cs` (~200 lines)
- Modified: `Features/Viewer/ViewerPage.xaml` (update bindings)
- Modified: All 8 `Features/Viewer/Components/ViewerPage.*.cs` partials
- Modified: `Features/Viewer/Components/ViewerToolbar.xaml.cs`
- New: `QuickView.Tests/Features/Viewer/ViewerViewModelTests.cs` (~150 lines)

---

### Testing ViewerViewModel (1 day)
- [ ] Test PDF page navigation
- [ ] Test zoom in/out/reset
- [ ] Test rotation
- [ ] Test edge cases (page 0, page > total, zoom < min, zoom > max)
- [ ] Verify bindings update UI correctly
- [ ] Verify commands are enabled/disabled appropriately
- [ ] Run full test suite
- [ ] Fix any regressions

---

## ✅ Sprint 3 Complete Criteria
- [ ] ViewerViewModel fully active
- [ ] All state moved from code-behind to ViewModel
- [ ] XAML bindings updated and working
- [ ] At least 15 new unit tests passing
- [ ] Manual testing passed (PDF viewing works)
- [ ] Pull request created and merged

---

## 🔐 Sprint 4: Harden Web Rendering (2-3 days)

### Phase 7: HTML/Markdown Security Hardening (2-3 days)
- [ ] Already done: Markdig integration (Sprint 1)
- [ ] Already done: JavaScript disabled in HtmlRenderer (Sprint 1)
- [ ] Add URL scheme validation in `MarkdownRenderer`:
  ```csharp
  private bool IsSafeUrl(string url)
  {
      return url.StartsWith("http://") || 
             url.StartsWith("https://") || 
             url.StartsWith("/") || 
             url.StartsWith("./");
  }
  ```
- [ ] Filter URLs before inserting into HTML
- [ ] Add `NavigationStarting` handler to `HtmlRenderer` WebView2
- [ ] Test with malicious samples:
  - [ ] `javascript:` URLs
  - [ ] `data:` URLs
  - [ ] `vbscript:` URLs
  - [ ] External navigation attempts
- [ ] Verify all blocked correctly
- [ ] Add integration tests for web rendering security
- [ ] Commit with message: "security: harden HTML/Markdown rendering (Phase 7)"

---

## ✅ Sprint 4 Complete Criteria
- [ ] All web rendering security gaps closed
- [ ] Malicious samples blocked correctly
- [ ] Tests passing
- [ ] Security review passed
- [ ] Pull request created and merged

---

## 🎯 Future Sprints (Backlog)

### Sprint 5: Extract PdfViewerService (5 days)
See [Refactoring Roadmap Phase 3](./refactoring-roadmap.md#phase-3--extract-pdfviewerservice-medium-risk)

### Sprint 6: Introduce Dependency Injection (8 days)
See [Refactoring Roadmap Phase 6](./refactoring-roadmap.md#phase-6--introduce-dependency-injection-medium-risk-wide-impact)

### Sprint 7: Fix IFileRenderer Interface (5 days)
See [Refactoring Roadmap Phase 4](./refactoring-roadmap.md#phase-4--fix-ifilerenderer-interface-low-risk-wide-impact)

### Sprint 8: Additional Security Fixes (2 days)
- Phase 8: Native DLL loading
- Phase 10: PathValidator in scanner
- SEC-06, SEC-07, SEC-08 remaining items

### Sprint 9: Performance Improvements (5 days)
- PERF-03: Batch thumbnail loading
- PERF-04: Add thumbnail virtualization
- PERF-05: Dispose WebView2 instances
- PERF-06: Move Markdown parsing to background

---

## 📊 Progress Tracking

### Completed
- [x] Audit completed (6 reports generated)
- [ ] Sprint 1: Security fixes
- [ ] Sprint 2: Testing foundation
- [ ] Sprint 3: ViewerViewModel activation
- [ ] Sprint 4: Web rendering hardening

### In Progress
- [ ] (Current sprint)

### Blocked
- [ ] (List any blockers)

---

## 🎓 Best Practices During Implementation

### Before Each Change
- [ ] Read relevant audit report section
- [ ] Understand files that will be modified
- [ ] Create feature branch: `refactor/phase-XX-description`
- [ ] Ensure current code compiles

### During Implementation
- [ ] Make small, atomic commits
- [ ] Write tests before/during implementation (TDD when possible)
- [ ] Run tests frequently: `dotnet test`
- [ ] Keep changes focused on single responsibility

### After Each Change
- [ ] Run full test suite
- [ ] Manual smoke test of affected features
- [ ] Check for build warnings
- [ ] Update this checklist
- [ ] Create pull request with description linking to audit report
- [ ] Request review from team

### Code Review Checklist
- [ ] Tests added/updated
- [ ] No new build warnings
- [ ] Follows existing code style
- [ ] XML doc comments added for public APIs
- [ ] No unnecessary changes (stay focused on task)
- [ ] Rollback plan documented if high-risk change

---

## 📝 Notes & Decisions

### Sprint 1 Notes
_Add notes here as you implement_

### Sprint 2 Notes
_Add notes here as you implement_

### Sprint 3 Notes
_Add notes here as you implement_

---

**Last Updated:** 2026-10-01  
**Next Review:** After Sprint 1 completion
