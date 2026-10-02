# Sprint 5: PdfViewerService Extraction Plan

> **Investigation Date:** 2025-01-XX  
> **Current State:** Sprint 4 complete (126 tests passing)  
> **Goal:** Extract PDF-specific logic from ViewerViewModel/ViewerPage into PdfViewerService

---

## Executive Summary

### Current Architecture

PDF rendering logic is currently **distributed across three layers**:

1. **ViewerViewModel** (`ViewerViewModel.cs`) — Holds page/zoom/rotation state but is **format-agnostic**
2. **ViewerPage.Pdf.cs** — Contains PDF-specific orchestration: loading, prefetching, eviction, progressive rendering
3. **PdfRenderer** + **PdfTextService** — Low-level PDFium wrapper for rendering and text extraction

**Key Finding:** ViewerViewModel is already well-separated and contains NO PDF-specific logic. All PDF operations live in `ViewerPage.Pdf.cs` (550+ lines), making extraction clean.

### Extraction Scope

**What moves to PdfViewerService:**
- PDF document loading with password handling (SEC-04 preserved)
- Progressive rendering strategy (96 DPI preview → 192 DPI full quality)
- Prefetch orchestration (±2 pages around current, ±4 for thumbnails)
- Memory eviction (±6 page window for main view, ±10 for thumbnails)
- Page/thumbnail rendering coordination
- Re-render on zoom changes

**What stays in ViewerPage.Pdf.cs:**
- UI layer operations (applying bitmaps to Image controls)
- Canvas creation for highlights
- ScrollViewer positioning
- Event handlers (sidebar, toolbar)
- XAML element lifecycle

**What stays in ViewerViewModel:**
- Format-agnostic viewer state (page, zoom, rotation)
- Commands (next/prev page, zoom in/out)
- Property change notifications for bindings

---

## Current Code Analysis

### 1. ViewerViewModel — Already Clean ✓

**File:** `c:\Code\QuickView\Features\Viewer\ViewerViewModel.cs`  
**Lines:** 142  
**Status:** ✅ **No changes needed** — already format-agnostic

**State managed:**
- `Document` (DocumentItem) — file metadata
- `CurrentPage`, `TotalPages` — pagination (works for any multi-page format)
- `Zoom`, `NaturalZoom` — zoom state (works for any zoomable format)
- `Rotation` — rotation angle
- `SidebarOpen`, `IsLoading`, `ErrorMessage` — UI state

**Commands:**
- `NextPageCommand`, `PreviousPageCommand`, `GoToPageCommand` → raise `PageChangeRequested` event
- `ZoomInCommand`, `ZoomOutCommand`, `ResetZoomCommand` → raise `Zoom*Requested` events

**Key insight:** ViewerViewModel already follows MVVM correctly — it owns state and raises events, but delegates actual operations to the page. This is the ideal separation.

### 2. ViewerPage.Pdf.cs — Extraction Target

**File:** `c:\Code\QuickView\Features\Viewer\Components\ViewerPage.Pdf.cs`  
**Lines:** 571  
**Status:** 🎯 **Primary extraction target**

**PDF-specific operations (to extract):**

#### A. Document Loading (`LoadPdfAsync` lines 39-151)
- Opens PDF via `PdfRenderer.OpenDocumentAsync`
- **SEC-04 preserved:** Uses secure DLL loading in `Pdfium.cs` static constructor
- Password dialog handling (F5 feature)
- Page size pre-caching
- Outline/bookmark loading
- Auto-hide sidebar for single-page PDFs
- **Security:** `FileSizeValidator.IsFileValidAndSafe` check in PdfRenderer

#### B. Progressive Rendering Strategy (`RenderPageAsync` lines 262-312)
- **Two-phase rendering:**
  1. Fast 96 DPI preview for instant feedback
  2. Full 192 DPI render in background
- Render tracking (`_renderedPages` HashSet)
- Bitmap application to UI layer
- Highlight canvas integration

#### C. Prefetch Orchestration (`PrefetchPagesAroundAsync` lines 157-226)
- **Main pages:** ±2 pages around center (5 total)
- **Thumbnails:** ±4 pages around center (9 total)
- **Priority queue:** Center page first, then closest neighbors
- Generation counter to cancel stale prefetch operations
- Eviction: keeps rendered bitmaps for ±6 pages (main), ±10 (thumbnails)

#### D. Thumbnail Rendering (`RenderThumbnailAsync` lines 314-340)
- Renders at 96 DPI (lower quality for speed)
- Separate tracking (`_renderedThumbs` HashSet)
- Updates `PageThumbnailItem.Thumbnail` property

#### E. UI Application (stays in ViewerPage.Pdf.cs)
- `ApplyPageVisual` (lines 314+) — creates Image control, adds to Grid
- `CreateHighlightCanvas` — used by search highlights
- `ShowInfoCard`, `ShowErrorCard` — error UI
- Password dialog creation (`CreatePasswordDialogContent`)

### 3. Supporting Infrastructure

#### PdfRenderer.cs — Low-level rendering ✓
**File:** `c:\Code\QuickView\Rendering\Pdf\PdfRenderer.cs`  
**Lines:** 131  
**Status:** ✅ **No changes needed** — already pure rendering logic

**Responsibilities:**
- `OpenDocumentAsync` — loads PDF, returns `PdfDocumentState`
- `RenderPageAsync` — rasters a single page to `WriteableBitmap`
- Uses `ArrayPool<byte>` for efficient memory allocation
- **Security:** Validates file size/safety before opening

#### PdfDocumentState.cs — Document handle ✓
**File:** `c:\Code\QuickView\Rendering\Pdf\PdfDocumentState.cs`  
**Lines:** 49  
**Status:** ✅ **No changes needed**

**Holds:**
- Native PDFium document handle
- Page count
- Pre-cached page sizes
- Generation counter (detects stale async operations)
- `IDisposable` — closes PDFium document on disposal

#### PdfTextService.cs — Text extraction ✓
**File:** `c:\Code\QuickView\Rendering\Pdf\PdfTextService.cs`  
**Lines:** 223  
**Status:** ✅ **No changes needed**

**Responsibilities:**
- `SearchAsync` — full-document text search (used by ViewerPage.Search.cs)
- `ExtractTextAsync` — page text extraction for clipboard copy
- `GetCharIndexAtPositionAsync` — text selection support
- `ExtractTextRangeAsync` — copy selected text

#### Pdfium.cs — P/Invoke wrapper ✓
**File:** `c:\Code\QuickView\Rendering\Pdf\Pdfium.cs`  
**Lines:** 355  
**Status:** ✅ **Contains SEC-04 fix** — secure DLL loading

**Security (SEC-04):**
```csharp
static Pdfium()
{
    var dllPath = Path.Combine(AppContext.BaseDirectory, "pdfium.dll");
    if (File.Exists(dllPath))
        NativeLibrary.Load(dllPath);  // Explicit load from app directory
}
```

**Key:** All PDFium calls must run under `Pdfium.Gate` (process-wide lock).

---

## Proposed Architecture

### IPdfViewerService Interface

```csharp
namespace QuickView.Application.Services;

/// <summary>
/// Orchestrates PDF document lifecycle, progressive rendering, prefetching, and memory management.
/// Bridges ViewerPage UI layer and low-level PdfRenderer.
/// </summary>
public interface IPdfViewerService
{
    // ── Document Lifecycle ────────────────────────────────────────────────
    
    /// <summary>
    /// Opens a PDF document, handling passwords and validation.
    /// Returns document state + page count, or error message.
    /// </summary>
    Task<PdfLoadResult> LoadDocumentAsync(
        string filePath,
        Func<Task<string?>>? passwordPrompt = null,
        CancellationToken ct = default);
    
    /// <summary>Closes the document and releases all resources.</summary>
    void CloseDocument();
    
    // ── Rendering ─────────────────────────────────────────────────────────
    
    /// <summary>
    /// Renders a page with progressive strategy (fast preview → full quality).
    /// Returns preview bitmap synchronously if available, full quality via callback.
    /// </summary>
    Task<WriteableBitmap?> RenderPageProgressiveAsync(
        int pageIndex,
        Action<WriteableBitmap>? onFullQuality = null,
        CancellationToken ct = default);
    
    /// <summary>Renders a thumbnail at low DPI.</summary>
    Task<WriteableBitmap?> RenderThumbnailAsync(
        int pageIndex,
        CancellationToken ct = default);
    
    // ── Prefetch & Memory Management ──────────────────────────────────────
    
    /// <summary>
    /// Prefetches pages around the current page for smooth scrolling.
    /// Evicts pages outside the window to manage memory.
    /// </summary>
    void PrefetchPagesAround(int centerPage);
    
    /// <summary>
    /// Prefetches thumbnails around the current page for smooth sidebar scrolling.
    /// </summary>
    void PrefetchThumbnailsAround(int centerPage);
    
    /// <summary>Cancels all in-flight prefetch operations.</summary>
    void CancelPrefetch();
    
    /// <summary>
    /// Triggers re-render of visible pages when zoom changes significantly.
    /// Used when zoom crosses DPI threshold (e.g., 2x → needs higher res render).
    /// </summary>
    void InvalidateRenderedPages();
    
    // ── State Queries ─────────────────────────────────────────────────────
    
    /// <summary>Gets the current document state (null if no document loaded).</summary>
    PdfDocumentState? CurrentDocument { get; }
    
    /// <summary>Gets page count (0 if no document loaded).</summary>
    int PageCount { get; }
    
    /// <summary>Gets page size in points (PDF units, 1 pt = 1/72 inch).</summary>
    (double Width, double Height) GetPageSize(int pageIndex);
    
    /// <summary>Natural zoom factor (96 DPI / RenderDpi).</summary>
    double NaturalZoom { get; }
}

/// <summary>Result of LoadDocumentAsync operation.</summary>
public sealed record PdfLoadResult(
    PdfDocumentState? Document,
    int PageCount,
    string? ErrorMessage,
    bool Success)
{
    public static PdfLoadResult Ok(PdfDocumentState doc, int pageCount) 
        => new(doc, pageCount, null, true);
    
    public static PdfLoadResult Error(string message) 
        => new(null, 0, message, false);
}
```

### Implementation: PdfViewerService.cs

**Location:** `c:\Code\QuickView\Application\Services\PdfViewerService.cs`  
**Estimated lines:** ~450

**State:**
```csharp
private PdfDocumentState? _document;
private int _pageCount;
private readonly HashSet<int> _renderedPages = new();
private readonly HashSet<int> _renderedThumbs = new();
private CancellationTokenSource? _prefetchCts;
private int _prefetchGeneration;
```

**Key methods:**
1. `LoadDocumentAsync` — extracted from `ViewerPage.Pdf.cs:LoadPdfAsync` (lines 39-151)
2. `RenderPageProgressiveAsync` — extracted from `RenderPageAsync` (lines 262-312)
3. `PrefetchPagesAroundAsync` — extracted from `PrefetchPagesAroundAsync` (lines 157-226)
4. `RenderThumbnailAsync` — extracted from `RenderThumbnailAsync` (lines 314-340)

**Dependencies:**
- `PdfRenderer` (already static, no change needed)
- `PdfDocumentState` (passed through interface)
- `FileSizeValidator` (already used in PdfRenderer)

---

## Extraction Steps

### Step 1: Create PdfViewerService with LoadDocument

**Goal:** Extract PDF loading logic into the new service.

**Files to create:**
- `c:\Code\QuickView\Application\Services\IPdfViewerService.cs` (interface)
- `c:\Code\QuickView\Application\Services\PdfViewerService.cs` (implementation)

**Files to modify:**
- `c:\Code\QuickView\Features\Viewer\Components\ViewerPage.Pdf.cs` — replace `LoadPdfAsync` body with service call

**Changes:**
1. Create `IPdfViewerService` interface with `LoadDocumentAsync` signature
2. Implement `PdfViewerService.LoadDocumentAsync`:
   - Extract lines 39-151 from `ViewerPage.Pdf.cs:LoadPdfAsync`
   - Remove UI operations (Toolbar updates, sidebar visibility, PagesPanel manipulation)
   - Return `PdfLoadResult` with document state + page count
   - Keep password retry logic (call `passwordPrompt()` callback)
3. Modify `ViewerPage.Pdf.cs`:
   - Add `private readonly IPdfViewerService _pdfService` field
   - Initialize in constructor: `_pdfService = new PdfViewerService()` (DI comes in Sprint 6)
   - Replace `LoadPdfAsync` body:
     ```csharp
     var result = await _pdfService.LoadDocumentAsync(
         filePath, 
         ShowPasswordDialogAsync,  // pass dialog as callback
         ct);
     
     if (!result.Success) {
         ShowErrorCard(result.ErrorMessage ?? "Cannot open PDF");
         return;
     }
     
     _pdfState = result.Document;
     _total = result.PageCount;
     // ... UI setup (toolbar, sidebar, placeholders) ...
     ```

**Verify:**
```powershell
# Run existing tests (should still pass)
dotnet test c:\Code\QuickView\QuickView.Tests\QuickView.Tests.csproj

# Manual test: Open a PDF in the app
# Expected: PDF loads normally, password dialog works, sidebar auto-hides for 1-page PDFs
```

**Success criteria:**
- All 126 existing tests pass
- PDF documents load correctly in the app
- Password-protected PDFs prompt for password
- Single-page PDFs auto-hide sidebar

---

### Step 2: Extract Progressive Rendering

**Goal:** Move page rendering orchestration to the service.

**Files to modify:**
- `c:\Code\QuickView\Application\Services\PdfViewerService.cs` — add `RenderPageProgressiveAsync`
- `c:\Code\QuickView\Features\Viewer\Components\ViewerPage.Pdf.cs` — replace `RenderPageAsync` body

**Changes:**
1. Add to `IPdfViewerService`:
   ```csharp
   Task<WriteableBitmap?> RenderPageProgressiveAsync(
       int pageIndex,
       Action<WriteableBitmap>? onFullQuality = null,
       CancellationToken ct = default);
   ```

2. Implement in `PdfViewerService`:
   - Extract rendering logic from `ViewerPage.Pdf.cs:RenderPageAsync` (lines 262-312)
   - Phase 1: Render 96 DPI preview, return immediately
   - Phase 2: Render 192 DPI full quality, call `onFullQuality(bitmap)` callback
   - Track rendered pages in `_renderedPages` HashSet
   - Return `WriteableBitmap?` from PdfRenderer

3. Modify `ViewerPage.Pdf.cs:RenderPageAsync`:
   ```csharp
   private async Task RenderPageAsync(PdfDocumentState pdfState, int pageIdx, CancellationToken ct)
   {
       var previewBmp = await _pdfService.RenderPageProgressiveAsync(
           pageIdx,
           fullBmp => DispatcherQueue.TryEnqueue(() => {
               ApplyPageVisual(pageIdx, pdfState, fullBmp, physW, physH, Stretch.None);
               DrawHighlights(pageIdx);
           }),
           ct);
       
       if (previewBmp != null)
           ApplyPageVisual(pageIdx, pdfState, previewBmp, physW, physH, Stretch.Fill);
   }
   ```

**Verify:**
```powershell
dotnet test c:\Code\QuickView\QuickView.Tests\QuickView.Tests.csproj

# Manual test: Open a large PDF, scroll quickly
# Expected: Pages show low-res preview instantly, then upgrade to full quality
```

**Success criteria:**
- All tests pass
- Pages render progressively (fast preview → full quality)
- No visual regressions in page rendering

---

### Step 3: Extract Prefetch & Eviction

**Goal:** Move prefetch orchestration and memory eviction to the service.

**Files to modify:**
- `c:\Code\QuickView\Application\Services\PdfViewerService.cs` — add prefetch methods
- `c:\Code\QuickView\Features\Viewer\Components\ViewerPage.Pdf.cs` — delegate to service

**Changes:**
1. Add to `IPdfViewerService`:
   ```csharp
   void PrefetchPagesAround(int centerPage);
   void PrefetchThumbnailsAround(int centerPage);
   void CancelPrefetch();
   HashSet<int> GetEvictablePages(int centerPage, int windowSize);
   HashSet<int> GetEvictableThumbnails(int centerPage, int windowSize);
   ```

2. Implement in `PdfViewerService`:
   - Extract `PrefetchPagesAroundAsync` logic (lines 157-226)
   - Extract `PrefetchThumbnailsAroundAsync` logic (lines 228-260)
   - Return sets of page indices to evict (don't touch UI directly)
   - Use generation counter to cancel stale operations

3. Modify `ViewerPage.Pdf.cs`:
   ```csharp
   internal void PrefetchPagesAround(int centerPage)
   {
       // Get pages to evict from service
       var evictPages = _pdfService.GetEvictablePages(centerPage, 6);
       var evictThumbs = _pdfService.GetEvictableThumbnails(centerPage, 10);
       
       // Evict UI elements (only ViewerPage can touch XAML)
       DispatcherQueue.TryEnqueue(() => {
           foreach (int i in evictPages)
               EvictPageVisual(i);  // new helper method
           foreach (int i in evictThumbs)
               Thumbnails[i].Thumbnail = null;
       });
       
       // Trigger prefetch in service
       _pdfService.PrefetchPagesAround(centerPage);
   }
   ```

**Verify:**
```powershell
dotnet test c:\Code\QuickView\QuickView.Tests\QuickView.Tests.csproj

# Manual test: Open a 50-page PDF, scroll through it
# Expected: Nearby pages load smoothly, distant pages evict, memory stays bounded
```

**Success criteria:**
- All tests pass
- Scrolling is smooth (prefetch works)
- Memory usage stays bounded (eviction works)
- No memory leaks (check Task Manager during long scroll)

---

### Step 4: Extract Thumbnail Rendering

**Goal:** Move thumbnail rendering to the service.

**Files to modify:**
- `c:\Code\QuickView\Application\Services\PdfViewerService.cs` — add `RenderThumbnailAsync`
- `c:\Code\QuickView\Features\Viewer\Components\ViewerPage.Pdf.cs` — delegate to service

**Changes:**
1. Add to `IPdfViewerService`:
   ```csharp
   Task<WriteableBitmap?> RenderThumbnailAsync(int pageIndex, CancellationToken ct = default);
   ```

2. Implement in `PdfViewerService`:
   - Extract from `ViewerPage.Pdf.cs:RenderThumbnailAsync` (lines 314-340)
   - Call `PdfRenderer.RenderPageAsync(state, pageIdx, 96f, ct)`
   - Track in `_renderedThumbs` HashSet
   - Return `WriteableBitmap?`

3. Modify `ViewerPage.Pdf.cs:RenderThumbnailAsync`:
   ```csharp
   private async Task RenderThumbnailAsync(PdfDocumentState pdfState, int pageIdx, CancellationToken ct)
   {
       var bmp = await _pdfService.RenderThumbnailAsync(pageIdx, ct);
       if (bmp == null || ct.IsCancellationRequested) return;
       
       DispatcherQueue.TryEnqueue(() => {
           if (ReferenceEquals(_pdfState, pdfState) && pageIdx < Thumbnails.Count)
               Thumbnails[pageIdx].Thumbnail = bmp;
       });
   }
   ```

**Verify:**
```powershell
dotnet test c:\Code\QuickView\QuickView.Tests\QuickView.Tests.csproj

# Manual test: Open a PDF with sidebar visible
# Expected: Thumbnails load progressively as you scroll the sidebar
```

**Success criteria:**
- All tests pass
- Thumbnails render correctly in sidebar
- Thumbnail scrolling is smooth

---

### Step 5: Add Service Unit Tests

**Goal:** Achieve >80% test coverage for PdfViewerService.

**Files to create:**
- `c:\Code\QuickView\QuickView.Tests\Application\Services\PdfViewerServiceTests.cs`

**Test cases:**

#### Document Loading
```csharp
[Fact]
public async Task LoadDocumentAsync_ValidPdf_ReturnsSuccess()
{
    var service = new PdfViewerService();
    var result = await service.LoadDocumentAsync("path/to/valid.pdf");
    
    result.Success.Should().BeTrue();
    result.Document.Should().NotBeNull();
    result.PageCount.Should().BeGreaterThan(0);
}

[Fact]
public async Task LoadDocumentAsync_InvalidPath_ReturnsError()
{
    var service = new PdfViewerService();
    var result = await service.LoadDocumentAsync("nonexistent.pdf");
    
    result.Success.Should().BeFalse();
    result.ErrorMessage.Should().NotBeEmpty();
}

[Fact]
public async Task LoadDocumentAsync_PasswordProtected_PromptsForPassword()
{
    var service = new PdfViewerService();
    bool promptCalled = false;
    
    var result = await service.LoadDocumentAsync(
        "path/to/protected.pdf",
        async () => { promptCalled = true; return "password"; });
    
    promptCalled.Should().BeTrue();
}
```

#### Rendering
```csharp
[Fact]
public async Task RenderPageProgressiveAsync_ValidPage_ReturnsPreviewFirst()
{
    var service = new PdfViewerService();
    await service.LoadDocumentAsync("path/to/test.pdf");
    
    WriteableBitmap? fullQualityBitmap = null;
    var preview = await service.RenderPageProgressiveAsync(
        0,
        bmp => fullQualityBitmap = bmp);
    
    preview.Should().NotBeNull("preview should return immediately");
    // Wait a bit for background render
    await Task.Delay(100);
    fullQualityBitmap.Should().NotBeNull("full quality should arrive via callback");
}

[Fact]
public async Task RenderPageProgressiveAsync_SamePage_SkipsRerender()
{
    var service = new PdfViewerService();
    await service.LoadDocumentAsync("path/to/test.pdf");
    
    await service.RenderPageProgressiveAsync(0);
    var secondCall = await service.RenderPageProgressiveAsync(0);
    
    secondCall.Should().BeNull("already rendered pages should return null");
}
```

#### Prefetch & Eviction
```csharp
[Fact]
public void PrefetchPagesAround_CentersOn5_Prefetches3To7()
{
    var service = new PdfViewerService();
    // Load 10-page PDF
    await service.LoadDocumentAsync("path/to/10page.pdf");
    
    service.PrefetchPagesAround(5);
    
    // Check that pages 3-7 are marked as rendered after prefetch completes
    // (implementation detail: may need to expose IsRendered(int page) for testing)
}

[Fact]
public void GetEvictablePages_Window6_EvictsOutsideRange()
{
    var service = new PdfViewerService();
    await service.LoadDocumentAsync("path/to/20page.pdf");
    
    // Render pages 0-19
    for (int i = 0; i < 20; i++)
        await service.RenderPageProgressiveAsync(i);
    
    var evictable = service.GetEvictablePages(centerPage: 10, windowSize: 6);
    
    evictable.Should().Contain(0, "page 0 is outside ±6 window");
    evictable.Should().Contain(19, "page 19 is outside ±6 window");
    evictable.Should().NotContain(10, "center page should not evict");
    evictable.Should().NotContain(8, "page 8 is within ±6 window");
}
```

**Verify:**
```powershell
dotnet test c:\Code\QuickView\QuickView.Tests\QuickView.Tests.csproj --filter "FullyQualifiedName~PdfViewerServiceTests"

# Expected: All PdfViewerService tests pass
# Expected: Coverage report shows >80% coverage for PdfViewerService.cs
```

**Success criteria:**
- New test file has 15-20 test cases
- All tests pass
- Test coverage >80% for PdfViewerService (measure with `dotnet test --collect:"XPlat Code Coverage"`)

---

### Step 6: Update ViewerViewModel Tests (if needed)

**Goal:** Ensure ViewModel tests still pass after service extraction.

**Files to check:**
- `c:\Code\QuickView\QuickView.Tests\Features\Viewer\ViewerViewModelTests.cs`

**Expected:** No changes needed — ViewerViewModel has no PDF-specific logic, so tests should pass unchanged.

**Verify:**
```powershell
dotnet test c:\Code\QuickView\QuickView.Tests\QuickView.Tests.csproj --filter "FullyQualifiedName~ViewerViewModelTests"

# Expected: All 38 ViewerViewModel tests pass (unchanged from Sprint 3)
```

**Success criteria:**
- All ViewerViewModel tests pass
- Test count remains 38 (no new tests needed)

---

## Security Preservation Checklist

### SEC-04: Secure pdfium.dll Loading

**Current implementation:** `Pdfium.cs` static constructor (lines 18-32)

```csharp
static Pdfium()
{
    try
    {
        var dllPath = Path.Combine(AppContext.BaseDirectory, "pdfium.dll");
        if (File.Exists(dllPath))
            NativeLibrary.Load(dllPath);  // ✅ Explicit load from app directory
    }
    catch { /* Ignore - P/Invoke will handle failures */ }
}
```

**Preservation strategy:**
- ✅ PdfViewerService does NOT touch Pdfium.cs
- ✅ PdfViewerService calls `PdfRenderer.OpenDocumentAsync`, which calls `Pdfium.EnsureInitialized()`
- ✅ Static constructor runs once before first P/Invoke call
- ✅ No changes to DLL loading path

**Verification:**
1. After extraction, inspect `PdfViewerService.cs` — should have zero `[DllImport]` attributes
2. Confirm `Pdfium.cs` is unchanged (git diff should show 0 changes to that file)
3. Manual test: Delete `pdfium.dll` from output directory → app should fail with clear error (not DLL hijack)

---

### File Size Validation (SEC-01 related)

**Current implementation:** `PdfRenderer.OpenDocumentAsync` (lines 24-26)

```csharp
if (!FileSizeValidator.IsFileValidAndSafe(filePath, out var validationError))
    return new PdfOpenResult(null, $"Validation failed: {validationError}");
```

**Preservation strategy:**
- ✅ Validation stays in `PdfRenderer.OpenDocumentAsync`
- ✅ PdfViewerService calls PdfRenderer, inherits validation automatically
- ✅ No changes to validation logic

**Verification:**
1. Manual test: Try to open a 3 GB PDF → should fail with "File too large" error
2. Check `PdfViewerService.LoadDocumentAsync` — should NOT duplicate validation (rely on PdfRenderer)

---

## Test Migration Strategy

### Current Test Coverage (Sprint 4)

**Total tests:** 126 passing

**Test files:**
1. `ViewerViewModelTests.cs` — 38 tests (ViewModel state, commands, property notifications)
2. Other test files — 88 tests (security, rendering, file system, etc.)

**Impact:** ViewerViewModel tests are **unaffected** — no PDF-specific logic in ViewModel.

### New Tests for Sprint 5

**File to create:** `PdfViewerServiceTests.cs`

**Test categories:**
1. **Document Loading** (5 tests)
   - Valid PDF → success
   - Invalid path → error
   - Password-protected → prompts for password
   - Cancel during load → cleans up resources
   - Large file → respects FileSizeValidator

2. **Progressive Rendering** (4 tests)
   - First render → returns preview + calls full quality callback
   - Re-render same page → skips (returns null)
   - Cancel during render → cleans up resources
   - Invalid page index → returns null

3. **Prefetch & Eviction** (6 tests)
   - Prefetch around center → renders ±2 pages
   - Prefetch priority → center first, then neighbors
   - Eviction window → keeps ±6, evicts rest
   - Cancel prefetch → stops in-flight operations
   - Generation counter → cancels stale prefetch
   - Thumbnail prefetch → renders ±4 thumbs

4. **State Queries** (3 tests)
   - PageCount before load → 0
   - PageCount after load → correct value
   - GetPageSize → returns correct dimensions

**Total new tests:** ~18

**Final test count after Sprint 5:** 126 + 18 = **144 tests**

---

## Migration Path — Detailed Steps

### Phase 1: Foundation (Days 1-2)

1. **Create service interfaces** (4 hours)
   - `Application/Services/IPdfViewerService.cs` (interface only)
   - `Application/Services/PdfLoadResult.cs` (result record)
   - Add XML doc comments to all public members

2. **Create service skeleton** (2 hours)
   - `Application/Services/PdfViewerService.cs` (empty implementation)
   - Add constructor, dispose pattern
   - Add private fields (`_document`, `_renderedPages`, etc.)

3. **Wire service into ViewerPage** (2 hours)
   - Add `_pdfService` field to `ViewerPage.xaml.cs`
   - Initialize in constructor: `_pdfService = new PdfViewerService()`
   - No logic changes yet (wire only)

**Checkpoint:** Solution compiles, tests pass (no behavior changes yet)

---

### Phase 2: Extract LoadDocument (Days 2-3)

1. **Implement LoadDocumentAsync** (6 hours)
   - Copy `LoadPdfAsync` logic (lines 39-151) to `PdfViewerService.LoadDocumentAsync`
   - Remove UI operations (keep only PDF operations)
   - Add password retry loop with callback parameter
   - Return `PdfLoadResult`

2. **Update ViewerPage.Pdf.cs** (2 hours)
   - Replace `LoadPdfAsync` body with service call
   - Keep UI setup (toolbar, sidebar, placeholders)
   - Pass `ShowPasswordDialogAsync` as callback

3. **Manual testing** (2 hours)
   - Open various PDFs (small, large, multi-page, single-page)
   - Test password-protected PDFs
   - Test invalid files (should show error)

**Checkpoint:** PDFs load correctly, all tests pass (126/126)

---

### Phase 3: Extract Rendering (Days 3-4)

1. **Implement RenderPageProgressiveAsync** (6 hours)
   - Copy `RenderPageAsync` logic (lines 262-312) to service
   - Split into preview + full quality phases
   - Track in `_renderedPages` HashSet
   - Return `WriteableBitmap?`, call callback for full quality

2. **Update ViewerPage.Pdf.cs** (2 hours)
   - Replace `RenderPageAsync` body with service call
   - Keep `ApplyPageVisual` UI logic in ViewerPage
   - Keep `DrawHighlights` integration

3. **Manual testing** (2 hours)
   - Open large PDF, scroll quickly
   - Verify progressive rendering (preview → full quality)
   - Check that highlights draw correctly

**Checkpoint:** Rendering works, tests pass (126/126)

---

### Phase 4: Extract Prefetch & Eviction (Days 4-5)

1. **Implement prefetch methods** (8 hours)
   - Copy `PrefetchPagesAroundAsync` logic (lines 157-226)
   - Copy `PrefetchThumbnailsAroundAsync` logic (lines 228-260)
   - Return eviction sets (don't touch UI)
   - Add generation counter for cancellation

2. **Update ViewerPage.Pdf.cs** (3 hours)
   - Replace prefetch logic with service calls
   - Add `EvictPageVisual` helper method
   - Keep UI eviction in ViewerPage (can only touch XAML)

3. **Manual testing** (3 hours)
   - Open 50-page PDF, scroll through entire document
   - Monitor memory usage (should stay bounded)
   - Verify smooth scrolling (prefetch working)

**Checkpoint:** Prefetch works, memory bounded, tests pass (126/126)

---

### Phase 5: Extract Thumbnails (Day 5)

1. **Implement RenderThumbnailAsync** (3 hours)
   - Copy `RenderThumbnailAsync` logic (lines 314-340)
   - Track in `_renderedThumbs` HashSet
   - Return `WriteableBitmap?`

2. **Update ViewerPage.Pdf.cs** (1 hour)
   - Replace thumbnail rendering with service call
   - Keep `Thumbnails` collection update in ViewerPage

3. **Manual testing** (1 hour)
   - Open PDF with sidebar visible
   - Scroll sidebar thumbnail list
   - Verify thumbnails load progressively

**Checkpoint:** Thumbnails work, tests pass (126/126)

---

### Phase 6: Add Service Tests (Days 6-7)

1. **Create test file** (2 hours)
   - `QuickView.Tests/Application/Services/PdfViewerServiceTests.cs`
   - Add test fixture setup
   - Add sample PDF files to test resources

2. **Write document loading tests** (3 hours)
   - Valid PDF → success
   - Invalid path → error
   - Password-protected → prompts
   - Cancel during load
   - Large file validation

3. **Write rendering tests** (4 hours)
   - Progressive rendering flow
   - Re-render skipping
   - Cancel during render
   - Invalid page index

4. **Write prefetch/eviction tests** (5 hours)
   - Prefetch window calculation
   - Priority queue ordering
   - Eviction window calculation
   - Generation counter cancellation
   - Thumbnail prefetch

5. **Write state query tests** (2 hours)
   - PageCount before/after load
   - GetPageSize returns correct values

**Checkpoint:** All tests pass (144/144), coverage >80%

---

### Phase 7: Documentation & Cleanup (Day 7)

1. **Add XML doc comments** (2 hours)
   - All public IPdfViewerService members
   - All public PdfViewerService members
   - Update ViewerPage.Pdf.cs comments

2. **Update architecture docs** (2 hours)
   - Add service to architecture diagram
   - Document service responsibilities
   - Update contribution guide

3. **Code review prep** (1 hour)
   - Run code formatter
   - Check for compiler warnings
   - Run static analysis

**Checkpoint:** Code is clean, documented, ready for review

---

## Effort Estimate

**Total effort:** 6-7 days (1 developer)

**Breakdown:**
- **Days 1-2:** Foundation + LoadDocument extraction
- **Days 2-3:** LoadDocument integration + manual testing
- **Days 3-4:** Progressive rendering extraction
- **Days 4-5:** Prefetch/eviction extraction + thumbnails
- **Days 6-7:** Service unit tests (18 tests)
- **Day 7:** Documentation + cleanup

**Risk buffer:** +1 day for unexpected issues (rendering regressions, test flakiness)

**Total calendar time:** 7-8 days

---

## Success Criteria

### Functional Requirements ✓

- [ ] All 126 existing tests pass (no regressions)
- [ ] 18+ new PdfViewerService tests added (total: 144+ tests)
- [ ] PDFs load and render correctly in the application
- [ ] Progressive rendering works (preview → full quality)
- [ ] Prefetch works (smooth scrolling)
- [ ] Memory eviction works (bounded memory usage)
- [ ] Thumbnails render correctly in sidebar
- [ ] Password-protected PDFs prompt for password

### Code Quality ✓

- [ ] PdfViewerService has >80% test coverage
- [ ] All public members have XML doc comments
- [ ] No compiler warnings
- [ ] Code follows existing project conventions
- [ ] `ViewerPage.Pdf.cs` reduced from 571 to ~200 lines

### Security Preservation ✓

- [ ] SEC-04 fix preserved (Pdfium.cs unchanged)
- [ ] FileSizeValidator still enforced (via PdfRenderer)
- [ ] No new security vulnerabilities introduced
- [ ] Manual security test: Try to open 3 GB file → rejected

### Performance ✓

- [ ] PDF load time ≤ baseline (Sprint 4)
- [ ] First page render time ≤ 20ms (96 DPI preview)
- [ ] Smooth scrolling (60 FPS) for 50-page PDF
- [ ] Memory usage bounded (<500 MB for 100-page PDF)

---

## Rollback Strategy

If extraction causes critical regressions:

1. **Revert the merge commit** on main branch:
   ```powershell
   git revert <merge-commit-sha>
   git push origin main
   ```

2. **Fix issues in feature branch**:
   ```powershell
   git checkout sprint5-pdfservice
   # Fix the issue
   git commit -m "fix: resolve rendering regression"
   ```

3. **Re-test before re-merging**:
   ```powershell
   dotnet test
   # Manual testing checklist
   git checkout main
   git merge sprint5-pdfservice
   ```

**Rollback impact:** Low — extraction is self-contained, doesn't affect other features.

---

## Dependencies & Prerequisites

### Sprint 5 Prerequisites ✓

- ✅ Sprint 1 complete — Security fixes (SEC-01 to SEC-05) applied
- ✅ Sprint 2 complete — Test project exists (126 tests passing)
- ✅ Sprint 3 complete — ViewerViewModel activated with commands
- ✅ Sprint 4 complete — HtmlRenderer + MarkdownRenderer hardened

### External Dependencies ✓

- ✅ `xUnit` test framework (already installed)
- ✅ `FluentAssertions` (already installed)
- ✅ `PDFiumCore` NuGet package (already installed, v97.0.4690)
- ✅ `CommunityToolkit.Mvvm` (already installed for ViewModels)

### No Blocking Issues ✓

All dependencies satisfied — Sprint 5 can begin immediately.

---

## Next Steps (Sprint 6 Preview)

After Sprint 5 completes, **Sprint 6: Dependency Injection** will:

1. Convert `PdfViewerService` from `new PdfViewerService()` to DI container registration
2. Create `IFileIndexService`, `IBookmarkService`, `ISettingsStore` interfaces
3. Register all services in `App.xaml.cs` composition root
4. Update all pages to receive services via constructor injection
5. Enable full mocking in unit tests (no more static classes)

**Preparation for Sprint 6:**
- Design IPdfViewerService with DI in mind (avoid static dependencies)
- Ensure PdfViewerService is stateful (holds document state per instance)
- Add IDisposable implementation for proper cleanup

---

## Appendix: File Change Summary

### Files Created (3 new files)

1. `Application/Services/IPdfViewerService.cs` (~80 lines)
2. `Application/Services/PdfViewerService.cs` (~450 lines)
3. `QuickView.Tests/Application/Services/PdfViewerServiceTests.cs` (~400 lines)

### Files Modified (1 file)

1. `Features/Viewer/Components/ViewerPage.Pdf.cs`
   - **Before:** 571 lines
   - **After:** ~200 lines (extract 370 lines to service)
   - **Changes:** Replace implementation with service calls, keep UI layer

### Files Unchanged (Critical - Security)

1. ✅ `Rendering/Pdf/Pdfium.cs` — SEC-04 fix preserved
2. ✅ `Rendering/Pdf/PdfRenderer.cs` — validation logic preserved
3. ✅ `Rendering/Pdf/PdfDocumentState.cs` — no changes
4. ✅ `Rendering/Pdf/PdfTextService.cs` — no changes
5. ✅ `Features/Viewer/ViewerViewModel.cs` — no changes (already clean)

### Total Code Changes

- **Lines added:** ~930 (new service + tests)
- **Lines removed:** ~370 (extracted from ViewerPage.Pdf.cs)
- **Net change:** +560 lines (mostly tests)

---

## Conclusion

Sprint 5 extraction is **low-risk, high-value**:

✅ **Clean separation** — ViewerViewModel is already format-agnostic, no changes needed  
✅ **Security preserved** — SEC-04 fix in Pdfium.cs untouched  
✅ **Testability gain** — 18+ new tests for PDF orchestration logic  
✅ **Code clarity** — ViewerPage.Pdf.cs shrinks from 571 to ~200 lines  
✅ **DI ready** — Service designed for constructor injection (Sprint 6)

**Recommendation:** Proceed with Sprint 5. No blockers identified.

---

**End of Sprint 5 Plan**  
**Prepared by:** Investigation Agent  
**Date:** 2025-01-XX  
**Status:** Ready for implementation
