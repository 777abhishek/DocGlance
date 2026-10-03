# QuickView — Code Flow Diagrams

**Updated:** 2026-10-03 — reflects post-migration file paths  
Visual representation of how code flows through the system for common operations.

---

## 1. Application Startup Flow

```
OS launches QuickView.exe
         ↓
Program.Main() [Program.cs]
 ├─ SetProcessDpiAwarenessContext() ← SEC: DPI awareness before WinUI init
 ├─ WinRT.ComWrappersSupport.InitializeComWrappers()
 └─ Application.Start(() => new App())
         ↓
App constructor [App.xaml.cs]
 ├─ InitializeComponent() ← Load App.xaml resources
 ├─ Wire UnhandledException handlers
 ├─ Wire AppInstance.Activated (for multi-instance file opening)
 └─ Logger.Log("App constructor completed")
         ↓
App.OnLaunched()
 ├─ RegisterRenderers() ← Register Office + HTML renderers
 ├─ new TitleBarWindow() ← Create main window
 ├─ window.Activate()
 ├─ RegistrationHelper.EnsureRegisteredAsync() ← Background (unpackaged only)
 ├─ Check activation args (file opened from Explorer?)
 └─ TryOpenFromCommandLine() ← Fallback
         ↓
TitleBarWindow constructor [UI/Shell/TitleBarWindow.xaml.cs]
 ├─ SetTitleBar(TitleBarDragArea)
 ├─ SetWindowIcon() ← SEC-04 related
 ├─ ConfigureTitleBarTheme()
 ├─ AddHomeTab() ← Permanent "Home" tab
 ├─ HomeFrame.Navigate(typeof(HomePage))
 └─ UpdateBannerControl.Attach() ← Check for updates
         ↓
HomePage.Page_Loaded() [Features/Home/HomePage.xaml.cs]
 ├─ LoadRecentDocuments() ← RecentFilesStore.Load()
 ├─ FileIndexService.InitializeDatabaseAsync() ← SQLite init
 └─ LoadCachedIndexAsync() ← Load previously scanned folders
```

---

## 2. Opening a File from Explorer (Double-Click)

```
User double-clicks file.pdf in Windows Explorer
         ↓
Windows Shell activates QuickView
         ↓
AppInstance.Activated event [App.xaml.cs:OnAppActivated]
 └─ DispatcherQueue.TryEnqueue(() => {
         window.Activate() ← Bring to front
         TryOpenFromActivationArgs(args)
     })
         ↓
App.TryOpenFromActivationArgs()
 ├─ Extract file path from IFileActivatedEventArgs
 ├─ PathValidator.IsValidFilePath() ← SEC-05: Reject symlinks, UNC
 ├─ FileSizeValidator.IsFileValidAndSafe() ← SEC: Size limits
 ├─ Build DocumentItem from FileInfo
 └─ TitleBarWindow.Current?.OpenDocument(doc)
         ↓
TitleBarWindow.OpenDocument() [UI/Shell/TitleBarWindow.xaml.cs]
 ├─ Check if file already open in a tab → focus it
 ├─ RecentFilesStore.Push(doc.FilePath) ← Add to recent list
 ├─ HomePage.LoadRecentDocuments() ← Refresh home strip
 ├─ new ViewerPage() ← Create viewer
 ├─ new TabViewItem { Header = doc.Title, Content = viewer }
 ├─ _tabData[tab] = (viewer, doc) ← Track per-tab state
 ├─ Tabs.TabItems.Add(tab)
 ├─ Tabs.SelectedItem = tab ← Focus new tab
 └─ viewer.LoadDocument(doc)
         ↓
ViewerPage.LoadDocument() [Features/Viewer/ViewerPage.xaml.cs]
 └─ LoadDocumentAsync(doc)
         ↓
ViewerPage.LoadDocumentAsync() [Main entry point]
 ├─ Reset state (_doc, _cts, _pdfState, etc.)
 ├─ Clear UI (PagesPanel, Thumbnails, Outline)
 ├─ Detect file type from extension
 └─ Route to format-specific loader:
      ├─ .pdf   → LoadPdfAsync()
      ├─ .md    → LoadMarkdownFileAsync()
      ├─ .cs/.js → LoadCodeFileAsync()
      ├─ .txt   → LoadTextFileAsync()
      └─ else   → RendererRegistry.GetRenderer(ext)
                   OR ShowInfoCard() if unsupported
```

---

## 3. PDF Loading Flow (Sprint 5 Target)

### Current Flow (Sprint 5 Phase 2 Complete):

```
ViewerPage.LoadPdfAsync() [Features/Viewer/Partials/ViewerPage.Pdf.cs]
 ├─ ShowLoading(true)
 ├─ _pdfService.LoadDocumentAsync(filePath, ShowPasswordDialogAsync, ct)
 │    ↓
 │   PdfViewerService.LoadDocumentAsync() [Services/Documents/PdfViewerService.cs]
 │    ├─ PdfRenderer.OpenDocumentAsync(filePath, ct)
 │    │    ↓
 │    │   PdfRenderer.OpenDocumentAsync() [Rendering/Pdf/PdfRenderer.cs]
 │    │    ├─ FileSizeValidator.IsFileValidAndSafe() ← SEC: Size check
 │    │    ├─ Task.Run(() => {
 │    │    ├──  Pdfium.EnsureInitialized() ← SEC-04: Load pdfium.dll
 │    │    ├──  lock (Pdfium.Gate)
 │    │    ├──  FPDF_LoadDocument(filePath, password)
 │    │    ├──  if (NeedsPassword) → return PdfOpenResult(needsPassword: true)
 │    │    ├──  FPDF_GetPageCount(doc)
 │    │    ├──  Pre-cache all page sizes (PageSizes list)
 │    │    └──  return PdfOpenResult(new PdfDocumentState(doc, count, sizes))
 │    │    })
 │    │    ↓
 │    └─ if (result.NeedsPassword) {
 │           password = await passwordPrompt() ← UI callback
 │           retry with password
 │       }
 │       return PdfLoadResult.Ok(document, pageCount)
 │    ↓
 ├─ if (!result.Success) → ShowErrorCard()
 ├─ _pdfState = result.Document
 ├─ _total = result.PageCount
 ├─ Toolbar.UpdatePageInfo(_page, _total)
 ├─ Task.Run(() => LoadOutline(pdfState)) ← Background
 ├─ Auto-hide sidebar if single-page PDF
 ├─ Create placeholders for all pages (PagesPanel.Children.Add)
 ├─ Create thumbnail items (Thumbnails.Add)
 ├─ Set zoom to 100% (NaturalZoom)
 ├─ WireScrollSync()
 ├─ RenderPageAsync(_pdfState, 0, ct) ← Render first page immediately (⚠️ TODO: move to service in Phase 3)
 ├─ ShowLoading(false) ← Interactive now!
 └─ PrefetchPagesAroundAsync(0, _pdfState, ct) ← Background (⚠️ TODO: move to service in Phase 4)
```

### Target Flow (After Sprint 5 Phases 3-6 Complete):

```
ViewerPage.LoadPdfAsync()
 ├─ ShowLoading(true)
 ├─ result = await _pdfService.LoadDocumentAsync(...)
 ├─ if (!result.Success) → ShowErrorCard()
 ├─ _pdfState = result.Document
 ├─ _total = result.PageCount
 ├─ Create UI placeholders (PagesPanel, Thumbnails) ← UI-only, stays in ViewerPage
 ├─ Set zoom to 100%
 ├─ firstPageBmp = await _pdfService.RenderPageProgressiveAsync(0, onFullQuality)
 │    ↓
 │   PdfViewerService.RenderPageProgressiveAsync() [NEW in Phase 3]
 │    ├─ if (_renderedPages.Contains(pageIndex)) → return null (already rendered)
 │    ├─ Phase 1: Render 96 DPI preview
 │    ├─ Task.Run(() => PdfRenderer.RenderPageAsync(state, pageIndex, 96f, ct))
 │    ├─ _renderedPages.Add(pageIndex)
 │    ├─ Background Phase 2: Render 192 DPI full quality
 │    ├─ Task.Run(() => PdfRenderer.RenderPageAsync(state, pageIndex, 192f, ct))
 │    └─ onFullQuality?.Invoke(fullBitmap) ← Callback to UI
 │    ↓
 ├─ ApplyPageVisual(0, previewBmp) ← UI update
 ├─ ShowLoading(false)
 └─ _pdfService.PrefetchPagesAround(0) ← Prefetch ±2 pages
      ↓
     PdfViewerService.PrefetchPagesAround() [NEW in Phase 4]
      ├─ Cancel previous prefetch (_prefetchCts?.Cancel())
      ├─ Determine range: [centerPage - 2, centerPage + 2]
      ├─ Evict pages outside ±6 window
      ├─ Prioritize center page, then neighbors
      └─ foreach page in range: RenderPageProgressiveAsync(page)
```

**Key improvement:** UI layer (`ViewerPage.Pdf.cs`) only handles XAML updates. Service layer (`PdfViewerService`) owns all PDF business logic.

**Current Status (Phase 2):** LoadDocument extracted ✅. Rendering, prefetch, thumbnails still in ViewerPage.Pdf.cs (Phases 3-6 pending).

---

## 4. PDF Search Flow

```
User types "invoice" in FindBox
         ↓
ViewerPage.FindBox_TextChanged() [Features/Viewer/Partials/ViewerPage.Search.cs]
 ├─ if (IsTextViewerActive()) → SearchInTextViewer() [different path]
 ├─ Cancel previous search (_searchCts?.Cancel())
 ├─ Cancel debounce timer (_searchDebounceCts?.Cancel())
 ├─ if (query.Empty) → ClearSearchHighlights() → return
 ├─ await Task.Delay(300, debounceCt) ← 300ms debounce
 ├─ _searchCts = new CancellationTokenSource()
 ├─ ClearSearchHighlights()
 ├─ SetSearchLoading(true)
 └─ Task.Run(() => {
         results = await PdfTextService.SearchAsync(_pdfState, query, caseSensitive, ct)
          ↓
         PdfTextService.SearchAsync() [Rendering/Pdf/PdfTextService.cs]
          ├─ Task.Run(() => SearchCore(state, query, caseSensitive, ct))
          ├─ lock (Pdfium.Gate)
          ├─ for each page (0.._pageCount):
          │    ├─ FPDF_LoadPage(doc, pageIndex)
          │    ├─ FPDFText_LoadPage(page)
          │    ├─ FPDFText_FindStart(textPage, query, flags)
          │    ├─ while (FPDFText_FindNext(search)):
          │    │    ├─ charIndex = FPDFText_GetSchResultIndex(search)
          │    │    ├─ charCount = FPDFText_GetSchCount(search)
          │    │    ├─ Extract rects for each char in match
          │    │    ├─ Convert rect coords (bottom-left → top-left origin)
          │    │    └─ results.Add(new PdfSearchResult(pageIndex, charIndex, charCount, rects))
          │    ├─ FPDFText_FindClose(search)
          │    ├─ FPDFText_ClosePage(textPage)
          │    └─ FPDF_ClosePage(page)
          └─ return results
     })
         ↓
 ├─ _searchResults = results
 ├─ _activeSearchIndex = results.Count > 0 ? 0 : -1
 ├─ UpdateSearchUI() ← "1 of 42 results"
 ├─ SetSearchLoading(false)
 └─ DrawHighlights() ← Draw yellow rectangles on all pages
      ↓
     DrawHighlights() [Features/Viewer/Partials/ViewerPage.Search.cs]
      ├─ foreach result in _searchResults:
      │    ├─ pageIndex = result.PageIndex
      │    ├─ Find Canvas overlay for this page (by Tag)
      │    ├─ foreach rect in result.Rects:
      │    │    ├─ Create Rectangle shape
      │    │    ├─ Set Fill = yellow (passive) or orange (active)
      │    │    └─ Canvas.Children.Add(rect)
      │    └─ Canvas.InvalidateArrange()
      └─ Scroll to first result
```

---

## 5. Folder Scanning Flow (Home Page)

```
User clicks "Add Folder" button [Features/Home/HomePage.xaml.cs]
         ↓
HomePage.AddFolder_Click()
 ├─ FolderPicker.PickSingleFolderAsync()
 ├─ if (ScannedFolders.Contains(folder.Path)) → return (already scanned)
 ├─ ScannedFolders.Add(folder.Path)
 └─ ScanFolderAsync(folder.Path)
         ↓
HomePage.ScanFolderAsync()
 ├─ cts = ViewModel.CreateScanToken(folderPath) ← Track per-folder cancellation
 ├─ ViewModel.IsScanning = true
 ├─ items = await FolderScanner.ScanFolderAsync(folderPath, cts.Token)
 │    ↓
 │   FolderScanner.ScanFolderAsync() [Services/FileSystem/Scanner/FolderScanner.cs]
 │    ├─ Task.Run(() => ScanCore(folderPath, ct))
 │    ├─ AllDirs(root, ct) ← Enumerate all subdirectories
 │    ├─ Parallel.ForEach(dirs, MaxDOP = CPU/2, dir => {
 │    │      ScanDir(dir, results, ct)
 │    │    })
 │    │    ↓
 │    │   ScanDir(dir, results, ct)
 │    │    ├─ foreach file in Directory.EnumerateFiles(dir, ...):
 │    │    ├──  PathValidator.IsValidFilePath(fi.FullName) ← SEC-05: Reject symlinks
 │    │    ├──  if (SupportedExtensions.Contains(fi.Extension))
 │    │    └──    results.Add(DocumentItem.FromFileInfo(fi))
 │    └─ return results.ToList()
 │    ↓
 ├─ Filter out duplicates (already in BackgroundIndex)
 ├─ foreach item in newItems: ViewModel.BackgroundIndex.Add(item)
 ├─ RefreshDisplay() ← Update UI
 ├─ Task.Run(() => {
 │       FileIndexService.SaveFilesToIndexAsync(newItems) ← SQLite persist
 │       FileIndexService.AddScannedFolderAsync(folderPath, count)
 │   })
 └─ LoadThumbnailsAsync(newItems, ct) ← Background thumbnail generation
         ↓
    LoadThumbnailsAsync()
     ├─ foreach doc in items:
     ├──  if (!ThumbnailService.SupportsThumbnail(ext)) → skip
     ├──  thumb = await ThumbnailService.GetThumbnailAsync(doc.FilePath, ct)
     │     ↓
     │    ThumbnailService.GetThumbnailAsync() [Services/Thumbnails/ThumbnailService.cs]
     │     ├─ await _sem.WaitAsync(ct) ← Limit to 6 concurrent
     │     ├─ cached = await ThumbnailCache.TryLoadAsync(filePath)
     │     ├─ if (cached != null) → return cached
     │     ├─ Find IThumbnailProvider for extension
     │     │    ├─ PdfThumbnailProvider (.pdf)
     │     │    ├─ OfficeThumbnailProvider (.docx, .xlsx, .pptx)
     │     │    └─ GenericThumbnailProvider (*) ← Fallback
     │     ├─ bmp = await provider.GetThumbnailAsync(filePath, ct)
     │     ├─ ThumbnailCache.SaveBitmapAsync(filePath, bmp) ← Fire-and-forget
     │     └─ return bmp
     │     ↓
     └──  DispatcherQueue.TryEnqueue(() => doc.Thumbnail = thumb)
```

---

## 6. Thumbnail Provider Selection

```
ThumbnailService.GetThumbnailAsync(filePath)
         ↓
Determine extension: ext = Path.GetExtension(filePath)
         ↓
┌─────────────────────────────────────────────────────────┐
│ Provider Registry (ordered list):                       │
│  1. PdfThumbnailProvider      (.pdf)                    │
│  2. OfficeThumbnailProvider   (.docx, .xlsx, .pptx)     │
│  3. GenericThumbnailProvider  (*) ← Wildcard fallback   │
└─────────────────────────────────────────────────────────┘
         ↓
Match extension against providers:
 ├─ if (ext == ".pdf") → PdfThumbnailProvider
 │    └─ Windows.Data.Pdf.PdfDocument.LoadFromFileAsync()
 │        └─ page.RenderToStreamAsync(stream, 200px width)
 ├─ if (ext in [".docx", ".xlsx", ".pptx"]) → OfficeThumbnailProvider
 │    └─ StorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 256px)
 └─ else → GenericThumbnailProvider
      └─ StorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 256px)
```

**Question:** Why does PDF use `Windows.Data.Pdf` for thumbnails but `PDFium` for main rendering?

**Answer:** Thumbnails prioritize speed and Windows integration. Main rendering needs precise text extraction, search, annotations — PDFium is more powerful.

---

## 7. Renderer Selection Flow

```
ViewerPage.LoadDocumentAsync(doc)
         ↓
Detect file extension: ext = Path.GetExtension(doc.FilePath)
         ↓
┌──────────────────────────────────────────────────────────┐
│ Routing Logic:                                           │
│  .pdf       → ViewerPage.LoadPdfAsync()                  │
│  .md        → ViewerPage.LoadMarkdownFileAsync()         │
│  .txt/.log  → ViewerPage.LoadTextFileAsync()             │
│  .cs/.js/.. → ViewerPage.LoadCodeFileAsync()             │
│  else       → RendererRegistry.GetRenderer(ext)          │
│               OR ShowInfoCard() if not registered        │
└──────────────────────────────────────────────────────────┘
         ↓
RendererRegistry.GetRenderer(ext) [Rendering/RendererRegistry.cs]
 ├─ Look up ext in _map (ConcurrentDictionary)
 ├─ return IFileRenderer or null
 └─ Registered renderers (from App.RegisterRenderers):
      ├─ DocxRenderer  (.docx, .doc)
      ├─ XlsxRenderer  (.xlsx, .xls)
      ├─ PptxRenderer  (.pptx, .ppt)
      └─ HtmlRenderer  (.html, .htm)
         ↓
IFileRenderer.RenderAsync(filePath, ct)
 ├─ DocxRenderer → return ViewerInfoCard(doc) [No actual rendering]
 ├─ XlsxRenderer → return ViewerInfoCard(doc) [No actual rendering]
 ├─ PptxRenderer → return ViewerInfoCard(doc) [No actual rendering]
 └─ HtmlRenderer → return WebView2 with SEC-01 hardening
```

**Problem:** Only 4 renderers registered, but `CodeRenderer`, `MarkdownRenderer`, `TextFileReader` also exist. They're called directly instead of via registry.

---

## 8. Theme Change Flow

```
User selects "Dark" theme in Settings
         ↓
SettingsPage.ThemeComboBox_SelectionChanged()
 ├─ Map index to ElementTheme (0=Light, 1=Dark, 2=Default)
 └─ ((App)Application.Current).SetTheme(theme)
         ↓
App.SetTheme() [App.xaml.cs]
 ├─ rootElement.RequestedTheme = theme ← Apply to content root
 └─ if (window is TitleBarWindow titleBarWindow)
         titleBarWindow.ConfigureTitleBarTheme()
         ↓
        TitleBarWindow.ConfigureTitleBarTheme() [UI/Shell/TitleBarWindow.xaml.cs]
         ├─ Determine current theme (Dark vs Light)
         ├─ Define colors for title bar buttons:
         │    ├─ fg      = White (dark) or Black (light)
         │    ├─ fgDim   = Semi-transparent fg
         │    ├─ hoverBg = Semi-transparent white/black
         │    ├─ pressBg = More opaque white/black
         │    └─ transp  = Transparent (Mica shows through)
         ├─ AppWindow.TitleBar.ButtonForegroundColor = fg
         ├─ AppWindow.TitleBar.ButtonHoverBackgroundColor = hoverBg
         ├─ AppWindow.TitleBar.ButtonPressedBackgroundColor = pressBg
         └─ Keep BackgroundColor = transparent (Mica backdrop visible)
```

---

## 9. Update Check Flow (Unpackaged Builds Only)

```
App Launch
         ↓
TitleBarWindow constructor
 └─ UpdateBannerControl.Attach()
         ↓
UpdateBanner.Attach() [Services/Updates/UpdateBanner.xaml.cs]
 └─ UpdateService.CheckInBackground()
         ↓
UpdateService.CheckInBackground() [Services/Updates/UpdateService.cs]
 ├─ #if PACKAGED → return (no-op, Store handles updates)
 ├─ if (_checked) → return (already checked this session)
 ├─ _checked = true
 ├─ Task.Run(async () => {
 │       response = await _httpClient.GetAsync(GitHubApiUrl)
 │       release = await response.Content.ReadFromJsonAsync<GitHubRelease>()
 │       if (release.TagName > currentVersion)
 │           UpdateAvailable?.Invoke(new UpdateAvailableEventArgs(release))
 │   })
 └─ (Fire-and-forget, non-blocking)
         ↓
UpdateBanner receives UpdateAvailable event
 ├─ DispatcherQueue.TryEnqueue(() => {
 │       BannerTitle.Text = $"Version {release.TagName} is available"
 │       UpdateBanner.IsOpen = true
 │   })
 └─ User clicks "Download" → UpdateService.DownloadAndInstallAsync()
         ↓
UpdateService.DownloadAndInstallAsync()
 ├─ #if PACKAGED → Open Store page (ms-windows-store:// URI)
 ├─ #if !PACKAGED:
 │    ├─ Download .exe installer from GitHub Release
 │    ├─ Verify SHA-256 checksum ← SEC-03
 │    ├─ Save to %TEMP%\QuickView-Setup.exe
 │    └─ Process.Start(installerPath) ← Launch installer
 └─ return
```

---

## 10. Security Controls Flow

### SEC-04: Secure pdfium.dll Loading

```
First PDFium call anywhere in the app
         ↓
PdfRenderer.EnsureInitialized() [Rendering/Pdf/PdfRenderer.cs]
 └─ Pdfium.EnsureInitialized()
         ↓
Pdfium.EnsureInitialized() [Rendering/Pdf/Pdfium.cs]
 ├─ if (Interlocked.Exchange(ref _initialized, 1) == 0)
 └──  FPDF_InitLibrary() ← First P/Invoke call
         ↓
        [DllImport("pdfium.dll")] ← CLR needs to load DLL
         ↓
        Static constructor Pdfium() runs FIRST (only once)
         ├─ dllPath = Path.Combine(AppContext.BaseDirectory, "pdfium.dll")
         ├─ if (File.Exists(dllPath))
         └──  NativeLibrary.Load(dllPath) ← EXPLICIT load from app directory
         ↓
CLR uses already-loaded DLL for all P/Invoke calls
```

**Why critical:** Without explicit load, CLR searches `PATH` environment variable first → DLL hijacking attack vector. Explicit load locks DLL to app directory.

### SEC-05: Path Validation

```
User selects folder to scan
         ↓
HomePage.ScanFolderAsync(folderPath)
         ↓
FolderScanner.ScanFolderAsync(folderPath)
         ↓
For each file found:
 ├─ PathValidator.IsValidFilePath(filePath, out error)
 │    ↓
 │   PathValidator.IsValidFilePath() [Common/Validators/PathValidator.cs]
 │    ├─ if (IsReparsePoint(filePath)) → REJECT ← Symlinks/junctions
 │    ├─ if (IsReparsePoint(parentDir)) → REJECT ← Parent is symlink
 │    ├─ if (filePath.StartsWith(@"\\")) → REJECT ← UNC paths
 │    ├─ if (hasInvalidPathChars) → REJECT
 │    └─ return true (safe)
 │    ↓
 ├─ if (!valid) → Logger.Log("Skipped: " + reason) → continue
 └─ results.Add(DocumentItem.FromFileInfo(fi))
```

**Why critical:** Prevents directory traversal, symlink attacks, accessing network shares.

---

## Summary: Dependency Flow (Layers)

```
┌───────────────────────────────────────────────────────┐
│ Layer 1: OS / Native Libraries                        │
│  - pdfium.dll (PDFium)                                │
│  - Windows.Data.Pdf (WinRT)                           │
│  - WebView2 (Edge Chromium)                           │
└───────────────────┬───────────────────────────────────┘
                    ↓
┌───────────────────────────────────────────────────────┐
│ Layer 2: P/Invoke / Native Wrappers                   │
│  - Pdfium.cs (355 extern methods)                     │
└───────────────────┬───────────────────────────────────┘
                    ↓
┌───────────────────────────────────────────────────────┐
│ Layer 3: Rendering Engines                            │
│  - PdfRenderer, PdfTextService, PdfLinkService        │
│  - TextFileReader, CodeRenderer, MarkdownRenderer     │
│  - HtmlRenderer, Office Renderers                     │
└───────────────────┬───────────────────────────────────┘
                    ↓
┌───────────────────────────────────────────────────────┐
│ Layer 4: Services (Business Logic)                    │
│  - PdfViewerService (PDF orchestration)               │
│  - ThumbnailService (thumbnail generation)            │
│  - FolderScanner (file indexing)                      │
│  - SettingsStore, RecentFilesStore                    │
└───────────────────┬───────────────────────────────────┘
                    ↓
┌───────────────────────────────────────────────────────┐
│ Layer 5: ViewModels (UI State)                        │
│  - ViewerViewModel, HomeViewModel                     │
└───────────────────┬───────────────────────────────────┘
                    ↓
┌───────────────────────────────────────────────────────┐
│ Layer 6: Pages / Views (XAML + Code-Behind)           │
│  - ViewerPage, HomePage, SettingsPage                 │
│  - TitleBarWindow (shell)                             │
└───────────────────────────────────────────────────────┘
```

**Clean Architecture Rule:** Dependencies only flow downward. No upward references (Layer 3 cannot reference Layer 5).

**Current Violations:**
- None! ✅ The architecture correctly separates concerns. ViewerPage → PdfViewerService → PdfRenderer → Pdfium. No circular dependencies.
