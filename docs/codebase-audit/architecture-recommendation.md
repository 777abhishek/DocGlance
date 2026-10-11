# QuickView — Architecture Recommendation (2026-10-11)

## Current state (verified)

The app is a **single-project, feature-folder WinUI 3 app** with static service registries and a per-tab page architecture:

- `TitleBarWindow` owns tabs; each document tab owns one `ViewerPage` (state preserved across switches) and thus one `PdfViewerService` + at most one native document per format.
- Format dispatch is an if-chain inside `ViewerPage.LoadDocumentAsync`; PDF/PPTX/XLSX logic lives in the `ViewerPage.*` partial family (~3.4k lines); XLSX alone was properly extracted into `SpreadsheetViewer` (UserControl) + `SpreadsheetDocument` (wrapper) — the best-shaped feature in the repo.
- Cross-cutting state is static (`RendererRegistry`, `Logger`, `ThumbnailCache`, `App.AllWindows`); DI container exists but is off.

**What is already good and must not be regressed:** the PDF viewport pipeline (bounded caches, Gate, pooled buffers); `SpreadsheetViewer`'s viewport-culled Win2D grid; per-tab isolation of viewer state; the validators; the async logger.

## Target architecture (minimal, evidence-backed)

Not a rewrite — three rules and two extractions:

### Rules
1. **One owner per native document.** A tab's ViewerPage is the only owner; all three native documents + the PDF service must be released in exactly one place (`CancelLoad`/format-switch guard). (Fixes FIND-001 class permanently.)
2. **UI-free format engines.** A format engine = `*Document` wrapper (native lifetime, no UI types) + optional UserControl host. `SpreadsheetDocument`+`SpreadsheetViewer` is the template. PPTX and PDF migrate toward it in Phase C only after characterization tests (FIND-012).
3. **DI as the single construction seam** (decision D-03 pending): `ServiceContainer.Configure()` in `App`; per-tab resolution of `IPdfViewerService`; no direct `new` of services in pages. Alternative accepted if the owner prefers deleting the container — either is fine, *half-wired* is not.

### Allowed dependency direction
```
Program/App → UI/Shell → Features/* → Rendering/* + Services/* → Core/Common
                                   ↘ Infrastructure (logging/config) 
Rendering/* must not reference Features/* or Microsoft.UI.Xaml controls (models/wrappers only)
Services/* must not host XAML (move UpdateBanner in Phase C — FIND-016)
```

### Extractions (Phase C, ordered)
1. `Features/Viewer/Presentation/` — move PPTX viewport logic out of `ViewerPage.Pdf.cs` (mirror Spreadsheet pattern).
2. `Features/Viewer/Pdf/` — PDF partial family renamed/recorganized under a `PdfViewerController` seam; `ViewerPage.Pdf.cs` renamed to `ViewerPage.Dispatch.cs` after DOCX decision lands.

## Explicit non-goals (do not do these)

- **No** MVVM rewrite of the ViewerPage partial family beyond the existing ViewModel sync (FIND-015 accepted transitional).
- **No** mediation/message-bus, repository-pattern, or generic "DocumentEngine" abstraction layer — three concrete formats do not justify it.
- **No** merging of the three Rust crates or FFI surfaces; they have different owners/upstream cadence.
- **No** new interfaces without a second implementation or a test seam requirement.
- **No** progressive-rendering re-enable (deliberately disabled, `68a3592`; dead code being removed — FIND-014).

## Migration strategy

Phase B fixes happen inside current structure (smallest diffs). Phase C extractions follow the characterization-test rule: extract only what a test pins. Public/runtime contracts (file associations, activation, tabs UX) unchanged throughout.
