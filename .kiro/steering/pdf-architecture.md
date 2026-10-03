---
inclusion: always
---

# QuickView PDF Architecture - Complete Reference

## PDF Component Inventory

### 1. Rendering Layer (`Rendering/Pdf/`)

All PDF rendering components use PDFium library and follow the pattern:
- Lock with `Pdfium.Gate` (process-wide lock)
- Execute PDFium operations
- Release resources in try-finally blocks

#### PdfRenderer.cs
**Purpose:** Core rendering engine
**Status:** ✅ ACTIVE - Core component
**API:**
- `OpenDocumentAsync(filePath, password?)` → Opens PDF, returns PdfOpenResult
- `RenderPageAsync(state, pageIndex, dpi)` → Renders page to WriteableBitmap
**Dependencies:** PdfDocumentState, Pdfium
**Used by:** PdfViewerService (Sprint 5)
**Future:** None - stable and complete

#### Pdfium.cs  
**Purpose:** Native P/Invoke wrapper to pdfium.dll
**Status:** ✅ ACTIVE - **SECURITY CRITICAL (SEC-04)**
**Security:** Static constructor loads pdfium.dll from application directory only
**Rule:** **NEVER MODIFY** - Any changes break SEC-04 DLL hijacking protection
**API:** 355+ P/Invoke methods for PDFium
**Used by:** All `Rendering/Pdf/` components
**Future:** Only update for new PDFium versions

#### PdfDocumentState.cs
**Purpose:** Document handle wrapper with lifecycle management
**Status:** ✅ ACTIVE - Core component
**API:**
- `Handle` - Native PDFium document pointer
- `PageCount` - Total pages
- `PageSizes` - Pre-cached page dimensions
- `Generation` - Counter for stale operation detection
- `Dispose()` - Closes PDFium document
**Used by:** All PDF services
**Future:** None - stable interface

#### PdfOperationBase.cs
**Purpose:** Common operation patterns (resource management helpers)
**Status:** ✅ ACTIVE - Infrastructure
**API:**
- `ExecutePageOperationAsync<T>` - Page operation with auto-cleanup
- `ExecuteTextPageOperationAsync<T>` - Text page operation with auto-cleanup
- `ExecuteDocumentOperationAsync<T>` - Document operation with locking
- `GetPageDimensions(page, dpi)` - Calculate pixel dimensions
- `PageToDeviceCoordinates` - PDF points → device pixels
- `DeviceToPageCoordinates` - Device pixels → PDF points
**Used by:** PdfTextService, PdfLinkService
**Pattern:** Internal helper, not used outside Rendering/Pdf/
**Future:** Add more helpers as patterns emerge

#### PdfTextService.cs
**Purpose:** Text extraction and search
**Status:** ✅ ACTIVE - Core component
**API:**
- `SearchAsync(state, query, caseSensitive)` → List<PdfSearchResult>
- `ExtractTextAsync(state, pageIndex?)` → Full page/document text
- `GetCharIndexAtPositionAsync(state, pageIndex, x, y)` → Character at position
- `ExtractTextRangeAsync(state, pageIndex, start, end)` → Text between indices
**Used by:** 
- `Features/Viewer/Search/ViewerPage.Search.cs` (search)
- Text selection feature (future)
**Future:** Add text formatting (bold/italic detection)

#### PdfLinkService.cs
**Purpose:** Hyperlink extraction (web URLs, email, internal page refs)
**Status:** ✅ ACTIVE - Core component
**API:**
- `GetPageLinksAsync(state, pageIndex)` → List<PdfLink>
**Used by:** Link overlay feature (future)
**Future:** Currently NOT used in UI - needs link overlay implementation

#### PdfOutlineReader.cs
**Purpose:** Bookmark/table of contents extraction
**Status:** ✅ ACTIVE - Core component
**API:**
- `Read(state)` → List<OutlineItem> (hierarchical tree)
**Used by:** `Features/Viewer/Pdf/ViewerPage.Pdf.cs` (sidebar outline tree)
**Future:** None - works perfectly

#### PdfAnnotationService.cs
**Purpose:** Add annotations (highlight, underline, ink drawing)
**Status:** ⚠️ PARTIALLY USED
**API:**
- `AddMarkupAsync(state, pageIndex, kind, rects, color)` - Text markup
- `AddInkAsync(state, pageIndex, points, color, lineWidth)` - Freehand drawing
**Used by:** Annotation feature (future)
**Future:** Currently NOT fully integrated - needs annotation UI
**Dead Code:** Implemented but no UI to trigger it

---

### 2. Service Layer (`Services/Documents/`)

#### PdfViewerService.cs (Sprint 5)
**Purpose:** PDF viewer orchestration (document lifecycle, rendering, prefetch)
**Status:** 🚧 IN PROGRESS
**Phase 1:** ✅ Skeleton created
**Phase 2:** ✅ LoadDocument implemented
**Phase 3-6:** Pending (progressive rendering, prefetch, thumbnails, tests)
**API:**
- `LoadDocumentAsync(filePath, passwordPrompt?)` → PdfLoadResult
- `CloseDocument()` - Cleanup
- `RenderPageProgressiveAsync` (Phase 3)
- `PrefetchPagesAround` (Phase 4)
- `RenderThumbnailAsync` (Phase 5)
**Pattern:** Service + UI callback (password prompt stays in UI)
**Future:** Complete Phases 3-6

#### IPdfViewerService.cs
**Purpose:** Service contract
**Status:** ✅ ACTIVE
**Used by:** ViewerPage.Pdf.cs via dependency injection (Sprint 6)

#### PdfLoadResult.cs
**Purpose:** Load operation result
**Status:** ✅ ACTIVE
**API:**
- `Ok(document, pageCount)` - Success
- `Error(message)` - Failure
**Pattern:** Result type pattern (no exceptions for expected failures)

---

### 3. UI Layer (`Features/Viewer/`)

#### Features/Viewer/Pdf/ViewerPage.Pdf.cs
**Purpose:** PDF-specific UI operations (XAML manipulation, events)
**Status:** ✅ ACTIVE - Main UI entry point
**Responsibilities:**
- Call PdfViewerService for PDF operations
- Update XAML (Toolbar, Sidebar, ScrollViewer, placeholders)
- Handle UI events (toolbar clicks, sidebar tabs)
- Show password dialog (UI callback)
- Progressive page rendering triggers
**Used by:** ViewerPage.xaml.cs (main shell)
**Pattern:** Partial class of ViewerPage
**Anti-pattern:** Should NOT contain PDF loading/rendering logic (moved to Service)
**Future:** Complete Sprint 5 extraction (phases 3-6)

#### Features/Viewer/Search/ViewerPage.Search.cs
**Purpose:** PDF search UI (search box, highlight drawing)
**Status:** ✅ ACTIVE
**Dependencies:** PdfTextService
**Responsibilities:**
- Search query debouncing (300ms)
- Call PdfTextService.SearchAsync
- Draw highlight rectangles on Canvas overlays
- Navigate between search results
**Future:** Add "replace" feature, regex search

---

### 4. UI Controls (`UI/Controls/`)

#### UI/Controls/PageThumbnailSidebar.xaml
**Purpose:** PDF thumbnail sidebar
**Status:** ✅ ACTIVE
**Used by:** ViewerPage.Pdf.cs
**Future:** Virtual scrolling for 1000+ page PDFs

#### UI/Controls/PdfActionBar.xaml
**Purpose:** PDF-specific toolbar actions (annotations, etc.)
**Status:** ⚠️ PARTIALLY USED
**Future:** Wire to PdfAnnotationService

#### UI/Controls/PdfTextSelectionOverlay.xaml
**Purpose:** Text selection UI overlay
**Status:** ⚠️ NOT IMPLEMENTED
**Future:** Implement with PdfTextService.GetCharIndexAtPositionAsync

---

### 5. Core Models (`Core/Models/`)

#### PdfSearchResult.cs
**Status:** ✅ ACTIVE
**Used by:** PdfTextService, ViewerPage.Search.cs
**Fields:** PageIndex, CharIndex, Length, Rects

#### PdfLink.cs
**Status:** ⚠️ PARTIALLY USED
**Used by:** PdfLinkService
**Future:** Link overlay UI needed
**Fields:** Type (WebUrl/Email/InternalPage), Url, TargetPageIndex, BoundingRects

#### OutlineItem.cs
**Status:** ✅ ACTIVE
**Used by:** PdfOutlineReader, ViewerPage.Sidebar.cs
**Fields:** Title, Page, Level, Children

#### PageThumbnailItem.cs
**Status:** ✅ ACTIVE
**Used by:** ViewerPage.Pdf.cs (thumbnail list)
**Fields:** PageNumber, PageLabel, Thumbnail (WriteableBitmap), BoxHeight, IsSelected

---

## PDF Architecture Layers (Top to Bottom)

```
┌─────────────────────────────────────────────────────────────┐
│ UI LAYER (Features/Viewer/Pdf/)                             │
│ - ViewerPage.Pdf.cs (XAML manipulation, UI callbacks)       │
│ - ViewerPage.Search.cs (search UI, highlight drawing)       │
└────────────────────┬────────────────────────────────────────┘
                     │ calls
┌────────────────────▼────────────────────────────────────────┐
│ SERVICE LAYER (Services/Documents/)                         │
│ - PdfViewerService (document lifecycle, orchestration)      │
│ - IPdfViewerService (contract)                              │
└────────────────────┬────────────────────────────────────────┘
                     │ calls
┌────────────────────▼────────────────────────────────────────┐
│ RENDERING LAYER (Rendering/Pdf/)                            │
│ - PdfRenderer (core rendering)                              │
│ - PdfTextService (text extraction, search)                  │
│ - PdfLinkService (hyperlink extraction)                     │
│ - PdfOutlineReader (bookmarks)                              │
│ - PdfAnnotationService (add annotations)                    │
│ - PdfOperationBase (helpers)                                │
└────────────────────┬────────────────────────────────────────┘
                     │ calls
┌────────────────────▼────────────────────────────────────────┐
│ NATIVE LAYER (Rendering/Pdf/)                               │
│ - Pdfium.cs (P/Invoke wrapper) **SEC-04 CRITICAL**          │
│ - PdfDocumentState (handle wrapper)                         │
└─────────────────────────────────────────────────────────────┘
                     │
                ┌────▼────┐
                │pdfium.dll│ (PDFiumCore NuGet v97.0.4690)
                └─────────┘
```

---

## Dead Code / Unused Features

### 1. PdfAnnotationService ⚠️
**Status:** Implemented but NO UI
**Code:** `Rendering/Pdf/PdfAnnotationService.cs` (195 lines)
**Missing:** 
- No annotation toolbar
- No ink drawing overlay
- No save functionality (annotations exist in memory only)
**Action:** Keep code, implement UI in future sprint

### 2. PdfLinkService ⚠️
**Status:** Implemented but NO UI overlay
**Code:** `Rendering/Pdf/PdfLinkService.cs` (221 lines)
**Missing:**
- No link detection overlay
- No click handlers for links
- No URL preview on hover
**Action:** Keep code, implement link overlay in future sprint

### 3. PdfTextSelectionOverlay ⚠️
**Status:** XAML exists, NO implementation
**Code:** `UI/Controls/PdfTextSelectionOverlay.xaml` (exists)
**Missing:**
- No mouse drag handling
- No GetCharIndexAtPositionAsync integration
- No text copy functionality
**Action:** Implement in future sprint (use PdfTextService.ExtractTextRangeAsync)

### 4. Text Formatting Detection ❌
**Status:** NOT IMPLEMENTED
**Missing:** Bold, italic, font size detection
**PDFium Support:** Exists (FPDF_TEXTPAGE_CHARINFO)
**Action:** Add to PdfTextService in future

---

## Future Features

### Priority 1: Complete Sprint 5
1. Phase 3: Progressive rendering (preview → full quality)
2. Phase 4: Prefetch & eviction
3. Phase 5: Thumbnail rendering
4. Phase 6: Service unit tests

### Priority 2: Text Selection
1. Implement PdfTextSelectionOverlay UI
2. Wire mouse events to GetCharIndexAtPositionAsync
3. Wire copy to ExtractTextRangeAsync
4. Add context menu (Copy, Select All)

### Priority 3: Hyperlinks
1. Implement link detection overlay
2. Wire click handlers (web URLs → browser, internal → navigate)
3. Add hover preview
4. Cursor change on link hover

### Priority 4: Annotations
1. Implement annotation toolbar (highlight, underline, ink)
2. Wire to PdfAnnotationService
3. Add save functionality (save PDF with annotations)
4. Add annotation list/delete

### Priority 5: Advanced Features
1. Form filling (PDFium supports forms)
2. Digital signatures (view only)
3. PDF/A validation
4. Redaction
5. OCR integration

---

## Professional Modular Structure

### Current Issues
1. ❌ **ViewerPage.Pdf.cs is too large** (571 lines) - being fixed in Sprint 5
2. ❌ **No clear service boundary** - Sprint 5 fixes this
3. ⚠️ **Unused features clutter codebase** - annotation/link services not integrated
4. ⚠️ **No tests for PDF services** - Sprint 5 Phase 6 adds tests

### Ideal Structure (After Sprint 5)

```
Services/Documents/
├── PdfViewerService.cs         (orchestration - 450 lines)
├── IPdfViewerService.cs         (contract - 50 lines)
└── PdfLoadResult.cs             (result type - 20 lines)

Rendering/Pdf/
├── PdfRenderer.cs               (core rendering - 180 lines) ✅
├── Pdfium.cs                    (P/Invoke - 355 lines) **SEC-04** ✅
├── PdfDocumentState.cs          (handle wrapper - 50 lines) ✅
├── PdfOperationBase.cs          (helpers - 130 lines) ✅
├── PdfTextService.cs            (text ops - 220 lines) ✅
├── PdfLinkService.cs            (links - 220 lines) ⚠️ needs UI
├── PdfOutlineReader.cs          (bookmarks - 70 lines) ✅
└── PdfAnnotationService.cs      (annotations - 195 lines) ⚠️ needs UI

Features/Viewer/Pdf/
└── ViewerPage.Pdf.cs            (UI only - 200 lines after Sprint 5) 🚧

Features/Viewer/Search/
└── ViewerPage.Search.cs         (search UI - stays as-is) ✅

Tests/Rendering/Pdf/
├── PdfRendererTests.cs          (future)
└── PdfTextServiceTests.cs       (future)

Tests/Services/Documents/
└── PdfViewerServiceTests.cs     (Sprint 5 Phase 6)
```

---

## How to Find Specific PDF Code

### "Where is PDF loading?"
→ `Services/Documents/PdfViewerService.cs` (Phase 2 complete)
→ Calls `Rendering/Pdf/PdfRenderer.OpenDocumentAsync`

### "Where is PDF rendering?"
→ `Rendering/Pdf/PdfRenderer.RenderPageAsync`
→ Called by `Services/Documents/PdfViewerService` (Phase 3)

### "Where is text search?"
→ `Rendering/Pdf/PdfTextService.SearchAsync`
→ Called by `Features/Viewer/Search/ViewerPage.Search.cs`

### "Where are bookmarks loaded?"
→ `Rendering/Pdf/PdfOutlineReader.Read`
→ Called by `Features/Viewer/Pdf/ViewerPage.Pdf.cs`

### "Where is the PDF toolbar?"
→ `Features/Viewer/Components/ViewerToolbar.xaml` (general)
→ `UI/Controls/PdfActionBar.xaml` (PDF-specific - not wired)

### "Where is text selection?"
→ ❌ NOT IMPLEMENTED
→ Code ready: `PdfTextService.GetCharIndexAtPositionAsync` + `ExtractTextRangeAsync`
→ Missing: `UI/Controls/PdfTextSelectionOverlay` implementation

### "Where are hyperlinks?"
→ Code: `Rendering/Pdf/PdfLinkService.GetPageLinksAsync`
→ UI: ❌ NOT IMPLEMENTED (needs link overlay)

### "Where are annotations?"
→ Code: `Rendering/Pdf/PdfAnnotationService`
→ UI: ❌ NOT IMPLEMENTED (needs annotation toolbar)

### "Where is the native PDFium wrapper?"
→ `Rendering/Pdf/Pdfium.cs` **SEC-04 CRITICAL - NEVER MODIFY**

---

## Security Chain (SEC-04)

```
User opens PDF
     ↓
ViewerPage.Pdf.cs
     ↓ _pdfService.LoadDocumentAsync
PdfViewerService
     ↓ PdfRenderer.OpenDocumentAsync
PdfRenderer
     ↓ Pdfium.FPDF_LoadDocument
Pdfium.cs
     ↓ static constructor runs ONCE on first call
     ↓ Loads pdfium.dll from AppContext.BaseDirectory only
pdfium.dll (loaded safely)
```

**Critical:** Any break in this chain compromises SEC-04

---

## Component Status Legend

- ✅ **ACTIVE** - Fully implemented and used
- 🚧 **IN PROGRESS** - Being developed (Sprint 5)
- ⚠️ **PARTIALLY USED** - Implemented but missing UI/integration
- ❌ **NOT IMPLEMENTED** - Planned but not started
- **DEAD** - Implemented but obsolete (none currently)
