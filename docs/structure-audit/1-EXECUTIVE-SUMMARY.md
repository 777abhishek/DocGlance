# QuickView Codebase Audit — Executive Summary

**Date:** 2026-10-03  
**Status:** ✅ Structure migration complete — codebase matches proposed architecture  
**Total Files:** ~160 files (125 .cs, 32 .xaml)  
**Lines of Code:** ~15,000 LOC  
**Test Coverage:** 126 passing tests

---

## What Was Done (Sprints 1–5 Phase 2)

### Sprint 1 — Security Hardening ✅
All 5 security controls implemented and verified:

| ID | Control | File |
|----|---------|------|
| SEC-01 | JavaScript disabled in WebView2 | `Rendering/Web/HtmlRenderer.cs` |
| SEC-02 | Markdig + URL sanitization | `Rendering/Web/MarkdownRenderer.cs` |
| SEC-03 | SHA-256 checksum for updates | `Services/Updates/UpdateService.cs` |
| SEC-04 | Secure pdfium.dll loading | `Rendering/Pdf/Pdfium.cs` ⚠️ NEVER MODIFY |
| SEC-05 | Path validation (symlinks, UNC) | `Common/Validators/PathValidator.cs` |

### Sprint 2 — Test Infrastructure ✅
- xUnit test project created (`QuickView.Tests/`)
- 73 tests covering `HomeViewModel`, `DateFormatter`, `FileSizeFormatter`, `FileSizeValidator`

### Sprint 3 — ViewerViewModel Extraction ✅
- `ViewerViewModel.cs` extracted from `ViewerPage.xaml.cs`
- 38 new tests for pagination, zoom, rotation commands
- Total: 111 tests

### Sprint 4 — Web Renderer Hardening ✅
- Security tests for `MarkdownRenderer` URL sanitization
- 15 new tests for SEC-02
- Total: 126 tests

### Structure Migration ✅
Full restructure to proposed architecture (90 files moved):

| Change | Before | After |
|--------|--------|-------|
| Formatters | `Core/Extensions/` | `Common/Formatters/` |
| Validators | `Core/Extensions/` | `Common/Validators/` |
| Utilities | `Core/Extensions/` | `Common/Utilities/` |
| Collections | `Core/Extensions/` | `Common/Collections/` |
| FileSystem | `FileSystem/` (root) | `Services/FileSystem/` |
| ViewerPage partials | `Viewer/Pdf/`, `Viewer/Text/`, `Viewer/Search/`, `Viewer/Components/` | `Viewer/Partials/` |
| SearchHighlighter | `Features/Search/` | `Features/Viewer/Components/` |
| Test formatters | `Tests/Shared/Formatters/` | `Tests/Common/Formatters/` |
| Test validators | `Tests/Shared/Validation/` | `Tests/Common/Validators/` |

### Sprint 5 — PdfViewerService Extraction 🚧
- ✅ Phase 1: `PdfViewerService.cs` skeleton in `Services/Documents/`
- ✅ Phase 2: `LoadDocumentAsync` logic extracted from `ViewerPage.Pdf.cs`
- ⏳ Phase 3: Progressive rendering (pending)
- ⏳ Phase 4: Prefetch & eviction (pending)
- ⏳ Phase 5: Thumbnail rendering via service (pending)
- ⏳ Phase 6: Unit tests for `PdfViewerService` (pending)

---

## Current Folder Structure

```
QuickView/
├── Common/                    ← NEW: shared utilities (replaces Core/Extensions mess)
│   ├── Formatters/            DateFormatter, FileSizeFormatter
│   ├── Validators/            FileSizeValidator, PathValidator
│   ├── Utilities/             FileTypeDetector
│   ├── Collections/           IncrementalLoadingCollection
│   └── Extensions/            UIThreadHelper (true extension methods)
├── Core/                      ← Domain models, interfaces, enums, constants
│   ├── Constants/
│   ├── Enums/
│   ├── Interfaces/
│   └── Models/
├── Features/
│   ├── Home/
│   ├── Browser/               (future — not wired into navigation)
│   ├── Settings/
│   └── Viewer/
│       ├── Partials/          ← NEW: all ViewerPage partial files in one place
│       │   ├── ViewerPage.Pdf.cs
│       │   ├── ViewerPage.Text.cs
│       │   ├── ViewerPage.Search.cs
│       │   ├── ViewerPage.Toolbar.cs
│       │   ├── ViewerPage.Sidebar.cs
│       │   ├── ViewerPage.Zoom.cs
│       │   └── ViewerPage.Helpers.cs
│       └── Components/        ViewerToolbar, ViewerSidebar, DocumentInfoDialog,
│                              SearchHighlighter (moved from Features/Search/)
├── Rendering/
│   ├── Pdf/                   PdfRenderer, Pdfium, PdfTextService, etc.
│   ├── Code/, Text/, Web/     Other format renderers
│   └── Office/, Unsupported/
├── Services/
│   ├── Documents/             PdfViewerService, IPdfViewerService, PdfLoadResult
│   ├── FileSystem/            ← MOVED from root FileSystem/
│   │   ├── Scanner/           FolderScanner, ScanOptions
│   │   ├── Index/             FileIndexService, FileIndexContext, FileIndexRepository
│   │   └── Watcher/           FileWatcherService
│   ├── Thumbnails/
│   ├── Storage/
│   ├── Registration/
│   └── Updates/
├── Infrastructure/            AppSettings, UserPreferences, Logger
├── UI/                        Controls, Shell, Styles, Templates, Icons
└── QuickView.Tests/
    ├── Common/                ← NEW: mirrors Common/
    │   ├── Formatters/        DateFormatterTests, FileSizeFormatterTests
    │   └── Validators/        FileSizeValidatorTests
    ├── Features/              HomeViewModelTests, ViewerViewModelTests
    ├── Rendering/Web/         WebSecurityTests
    └── Services/              (empty — future tests)
```

---

## Remaining Issues

### 1. Sprint 5 Incomplete 🚧
`ViewerPage.Pdf.cs` is still 571 LOC with mixed UI and business logic. Phases 3–6 will fix this.  
**Target:** `ViewerPage.Pdf.cs` drops to ~200 LOC (UI-only after service extraction).

### 2. Dead Code (Documented, Kept Intentionally)
Three PDF features are implemented at the rendering layer but have no UI yet:

| File | LOC | Missing |
|------|-----|---------|
| `Rendering/Pdf/PdfAnnotationService.cs` | 195 | Annotation toolbar, save |
| `Rendering/Pdf/PdfLinkService.cs` | 221 | Link overlay, click handlers |
| `UI/Controls/PdfTextSelectionOverlay.xaml` | — | Codebehind implementation |

These are **kept** — the rendering code is correct. UI integration is a future sprint.

### 3. Test Coverage Gaps
| Area | Tests | Status |
|------|-------|--------|
| `Common/Formatters/` | ✅ 35 | Good |
| `Features/Home/` | ✅ 38 | Good |
| `Features/Viewer/` | ✅ 38 | Good |
| `Rendering/Web/` | ✅ 15 | Good |
| `Rendering/Pdf/` | ❌ 0 | Sprint 5 Phase 6 |
| `Services/Documents/` | ❌ 0 | Sprint 5 Phase 6 |
| `Services/FileSystem/` | ❌ 0 | Future sprint |
| `Services/Thumbnails/` | ❌ 0 | Future sprint |

### 4. Compiler Warning
`PdfViewerService._prefetchGeneration` field is declared but never read (CS0169).  
**Fix:** Remove field or use it in Phase 4 prefetch logic.

### 5. Office Renderers Are Placeholders
`Rendering/Office/` (DocxRenderer, XlsxRenderer, PptxRenderer) return info cards only — no actual rendering.  
**Status:** By design for now. Real rendering would require Office interop or a library.

---

## Security Posture

✅ All 5 controls active. No regressions.  
⚠️ `Rendering/Pdf/Pdfium.cs` — **NEVER MODIFY** the static constructor. It implements SEC-04 (DLL hijacking protection). Any change silently breaks the security chain.

---

## "Where Does My Code Go?" — Quick Reference

| I'm adding... | Put it here |
|--------------|-------------|
| New file format renderer | `Rendering/{Format}/` + register in `Rendering/RendererRegistry.cs` |
| PDF business logic | `Services/Documents/PdfViewerService.cs` or new `Services/Documents/Pdf*.cs` |
| PDF rendering (low-level) | `Rendering/Pdf/` |
| Utility / helper | `Common/Utilities/` |
| Formatter (display) | `Common/Formatters/` |
| Validator | `Common/Validators/` |
| New UI page | `Features/{PageName}/` with ViewModel + Page |
| Shared UI control | `UI/Controls/` |
| Cross-cutting service | `Services/{ServiceName}/` |
| Domain model | `Core/Models/` |
| New test | Mirror source path under `QuickView.Tests/` |

---

## Related Documents

- `2-FOLDER-BY-FOLDER-ANALYSIS.md` — Every file analyzed with status
- `3-FLOW-DIAGRAMS.md` — Code flow for all major operations
- `4-PROPOSED-ARCHITECTURE.md` — Migration history and remaining work
