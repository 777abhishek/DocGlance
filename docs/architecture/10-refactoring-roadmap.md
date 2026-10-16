# 10 — Refactoring Roadmap (re-audit 2026-10-16 — remaining work only)

**Status:** Phases 1–4 and half of Phase 5 of the original roadmap shipped in commit `10db6a8` (Interop consolidation, engines by format, shell extractions, viewer contracts + registry + Text/Spreadsheet/Fallback viewers, legacy pipeline deletion). This roadmap covers **only the remaining delta** to `tree.md`. Each phase is dependency-ordered, independently buildable/shippable, and its "Completes" line lists exact final-tree paths. No phase deletes code before its replacement works and consumers are verified.

Global validation (every phase): `build.ps1` → app launches → open one file per format {`.pdf`, `.docx`, `.pptx`, `.xlsx`, `.txt`, `.md`, `.csv`, `.json`, `.cs`, `.xls`(fallback card)} → `dotnet test DocGlance.Tests`.

## Phase 1 — PDF viewer extraction
**Objective:** the largest format leaves the shell; PDF rendering/selection/annotation become a control boundary.
**Files affected:** NEW `Features/Viewer/Formats/Pdf/PdfViewer.xaml`+`.cs`, `PdfSelectionLayer.cs`, `PdfAnnotationLayer.cs`; DISSOLVE `Partials/ViewerPage.PdfTextSelection.cs`, `ViewerPage.PdfAnnotations.cs`; SPLIT `ViewerPage.Pdf.cs` (PDF block 158–437 → PdfViewer; dispatch stays temporarily), `ViewerPage.Viewport.cs` (PDF loop → PdfViewer), `ViewerPage.Zoom.cs` (`ApplyFitWidth/ApplyFitPage` → PdfViewer via `ISupportsZoom`), `ViewerPage.Search.cs` (PDF highlight parts → PdfViewer via `ISupportsSearch`); cutover the `.pdf` dispatch branch to `_viewerRegistry.Resolve(ext)`.
**Prerequisites:** none (builds directly on the shipped registry).
**Actions:** move responsibilities per tree §2.1; PdfViewer implements `IDocumentViewer, ISupportsZoom, ISupportsSearch, ISupportsTextSelection, ISupportsAnnotations, ISupportsThumbnails, ISupportsOutline`; register `PdfViewerFactory` in `ViewerPage.xaml.cs`; keep shared canvas approach inside PdfViewer (it owns its ScrollViewer + page canvases).
**Tests:** full suite green; manual PDF matrix (open, password-protected PDF, zoom/fit, rotate, search + navigate, annotate markup/pen/note/text/signature/image, undo/redo, deferred-save flush on tab close, thumbnails, outline, bookmarks).
**Acceptance:** `.pdf` served by PdfViewer through the registry; `ViewerPage.Pdf.cs` shrinks to dispatch + DOCX + PPTX + XLSX blocks; no PDF-specific field remains on ViewerPage except transitional mirrors.
**Rollback:** revert the phase commit; registry still routes `.pdf` to the legacy branch.
**Completes:** `Formats/Pdf/PdfViewer.xaml`, `PdfViewer.xaml.cs`, `PdfSelectionLayer.cs`, `PdfAnnotationLayer.cs`; dissolves `PdfTextSelection.cs`, `PdfAnnotations.cs`.

## Phase 2 — DOCX + PPTX cutover (including wiring the dormant PptxViewer)
**Objective:** last two shell formats become viewers; `PptxViewer` — migrated in `10db6a8` but never constructed (RA-04) — is wired in and absorbs the shell's parallel slide implementation.
**Files affected:** NEW `Features/Viewer/Formats/Docx/DocxViewer.xaml`+`.cs`; EDIT `Formats/Pptx/PptxViewer.xaml.cs` (absorbs PPTX block); DISSOLVE `ViewerPage.Pdf.cs` remainder (PPTX 438–919, DOCX 985–1365, XLSX glue 920–984 → generic registry path), `ViewerPage.OfficeTextSelection.cs`, `ViewerPage.Presentation.cs` (presentation parts → PptxViewer; `InitializeViewerShortcuts` etc. → `KeyboardShortcuts.cs`); SPLIT `ViewerPage.Sidebar.cs` (outline loaders → viewers via `ISupportsOutline`); dispatch drops `.docx/.pptx` literals.
**Prerequisites:** Phase 1 (shared dispatch path established).
**Actions:** DocxViewer implements `IDocumentViewer, ISupportsSearch, ISupportsTextSelection, ISupportsOutline, ISupportsThumbnails`; register factories; convert `LoadXlsxAsync` into the generic registry host path (resolve → create → host → `LoadAsync` → page-info).
**Tests:** full suite; manual DOCX matrix (open, zoom/fit, search, selection+copy, outline navigation, thumbnails) and PPTX matrix (open, slide navigation, presentation mode F5/Esc, search, selection, thumbnails, outline).
**Acceptance:** `ViewerPage.Pdf.cs` no longer exists; dispatch contains zero format literals except the generic registry path; presentation mode works through PptxViewer.
**Rollback:** revert; legacy branches restored (bounded coexistence — do not stack other phases on top).
**Completes:** `Formats/Docx/DocxViewer.xaml`+`.cs`; final-scope `Formats/Pptx/PptxViewer.xaml.cs`; dissolves `Pdf.cs`, `OfficeTextSelection.cs`, `Presentation.cs`.

## Phase 3 — Shell + ViewModel final scope
**Objective:** single source of truth for view state; capability-driven chrome; last MOVE.
**Files affected:** EDIT `ViewerPage.xaml.cs` (registry-first dispatch — drop `is SpreadsheetViewerFactory`-style type matching, RA-06; absorb `Helpers.cs`; drop `_page/_zoom/_total/_rotation` mirrors + `SyncToViewModel()` and its 8 call sites), `ViewerViewModel.cs` (sole owner), `ViewerPage.xaml`, `Partials/ViewerPage.Search.cs`, `Sidebar.cs`, `Zoom.cs`, `Viewport.cs` (final scope per tree §2.1), `Components/ViewerToolbar.xaml(.cs)` (imperative setters → ViewModel bindings), MERGE `ViewerPage.Helpers.cs`; MOVE `Services/Updates/UpdateBanner.xaml(.cs)` → `UI/Controls/` + update the `update:` URI in `TitleBarWindow.xaml`.
**Prerequisites:** Phase 2 (all five formats registry-served, so VM ownership changes are format-agnostic).
**Actions:** ViewerViewModel owns page/zoom/rotation/fit/sidebar; viewers mutate it directly via `ISupportsZoom`/events; keyboard shortcuts forward to focused viewer.
**Tests:** extend `ViewerViewModelTests` with sole-state-owner behaviors (zoom change → single notification path; page change → no mirror drift); full suite; manual: fit-mode persistence across resize, sidebar restore logic, update banner still shows, F5/Ctrl+F/Ctrl+wheel per format.
**Acceptance:** no `SyncToViewModel` anywhere; no `else if (ext ==` in Features; `ViewerPage.Helpers.cs` gone; UpdateBanner out of Services.
**Rollback:** revert; Phase 2 state remains valid.
**Completes:** final-scope `ViewerPage.xaml(.cs)`, `ViewerViewModel.cs`, `Partials/*`, `Components/ViewerToolbar.*`; `UI/Controls/UpdateBanner.xaml(.cs)`; removes `Helpers.cs`, `Services/Updates/UpdateBanner.*`.

## Phase 4 — REVIEW-gated deletions
**Objective:** remove the eight verified-dead files.
**Files:** DELETE `Core/Interfaces/IFileScanner.cs`, `Rendering/Code/CodeRenderer.cs`, `Services/FileSystem/Watcher/FileWatcherService.cs`, `Features/Browser/BrowserPage.xaml`, `BrowserPage.xaml.cs`, `BrowserViewModel.cs`, `UI/Controls/PageThumbnailSidebar.xaml`, `PageThumbnailSidebar.xaml.cs`.
**Protocol per file:** pre-delete grep for consumers (done 2026-10-16, zero hits — re-run at deletion time) → delete → `build.ps1` + full format matrix + `dotnet test` → commit individually or as one reviewed batch.
**Acceptance:** build clean, grep clean, suite green.
**Rollback:** `git revert` per file/batch.
**Completes:** deletion-ledger rows 1–8 (tree §3).

## Phase 5 — Architectural enforcement + doc-reference cleanup
**Objective:** make the layering regression-proof; fix dangling documentation references.
**Files:** NEW `DocGlance.Tests/Architecture/NamespaceDependencyTests.cs` encoding `08-dependency-and-interop-rules.md` §1 (Rendering: no `Microsoft.UI.Xaml.Controls`, no Features/UI refs; Interop consumers = Rendering + Services/Translation only; Core/Common/Infrastructure: no WinUI; Services: no XAML controls); EDIT comments in `App.xaml.cs:34` and `Infrastructure/Configuration/AppSettings.cs:110` which cite `docs/reliability/CRASH_DIAGNOSTICS.md` (deleted from the repo) — reword or restore a reliability note (RA-09; comment-only change).
**Acceptance:** deliberate layer violations fail `dotnet test`; no dangling doc paths in code comments.
**Rollback:** revert test file / comment edits.
**Completes:** final tree fully enforced; `Architecture/NamespaceDependencyTests.cs`.

## Explicitly out of scope
`ToolsPage` splitting; new csproj projects; **any Rust change**; drag-drop/picker validation gap (tracked separately); installer/winget regeneration; moving `PdfAnnotationTool` out of `ViewerToolbar.xaml.cs` (accepted, RA-03).

## Risk register
| Risk | Mitigation |
|---|---|
| PDF migration size (≈2,900 LOC moving) | selection/annotation move as whole layers; existing PDF tests + manual matrix; PdfViewer keeps internal structure mirroring the partials it replaces |
| PptxViewer bit-rot before Phase 2 wires it | it compiles today; Phase 2 lands next; manual PPTX matrix gates cutover |
| `.xls` behavior regression | already pinned by `DocumentViewerRegistryTests.Resolve_ClaimsXlsWithFallbackFactory`; re-run each phase |
| State-ownership switch regressions | Phase 3 is format-agnostic and isolated; ViewerViewModelTests extended first |
| Two dispatch mechanisms coexisting | bounded to Phases 1–2; Phase 2 ends with zero format literals |
| Hidden consumers of deletions | per-file grep + manual run protocol in Phase 4 |
