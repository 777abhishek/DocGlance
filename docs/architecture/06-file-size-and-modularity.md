# 06 — File Size and Modularity Estimates

Ranges are targets, not limits; the deciding factor is always "one reason to change". Current LOC measured with `wc -l` (includes comments/blank lines — code-behind totals cite file size).

## 1. ViewerPage cluster (the main event)

| Current file | LOC | Verdict | Target | Rationale |
|---|---:|---|---|---|
| `Partials/ViewerPage.Pdf.cs` | 1,439 | split 4 ways | dispatcher → registry (~60 NEW); PDF load → `PdfViewer` (~200); Docx load+thumbs → `DocxViewer`; Pptx → `PptxViewer`; Xlsx → `SpreadsheetViewer` glue | Contains four formats' loaders + universal dispatch (F2); four different change drivers |
| `Partials/ViewerPage.PdfAnnotations.cs` | 859 | move | `PdfAnnotationLayer` 400–600 | PDF-only; changes with annotation features only |
| `Partials/ViewerPage.PdfTextSelection.cs` | 729 | move | `PdfSelectionLayer` 250–400 | PDF-only |
| `Partials/ViewerPage.Viewport.cs` | 640 | split by format | PDF part → PdfViewer; docx/pptx parts → their viewers; resize/fit plumbing → ViewerPage shell | Shared only in name; each render loop is format-specific |
| `Partials/ViewerPage.OfficeTextSelection.cs` | 515 | split | docx part → DocxViewer, pptx part → PptxViewer | Two formats interleaved in one file |
| `Partials/ViewerPage.Sidebar.cs` | 504 | split | shell sidebar hosting (~200) stays; `LoadDocxOutlineAsync`/`LoadPptxOutlineAsync` move to viewers via `ISupportsOutline` | Mixes shell hosting with format loading |
| `Partials/ViewerPage.Search.cs` | 478 | split | shared search panel (~200) + per-viewer `SearchAsync` implementations (move into viewers, 50–100 each) | UI shareable, execution format-specific |
| `Partials/ViewerPage.Zoom.cs` | 315 | shrink | fit/zoom coordination (~150) + `FitMode` enum to Core; per-viewer math moves to viewers | Two zoom engines merged by contract |
| `Partials/ViewerPage.Text.cs` | 270 | move | `TextViewer` 150–250 | Whole file is text-format logic |
| `Partials/ViewerPage.Presentation.cs` | 250 | move | absorbed by `PptxViewer` | PPTX presentation mode |
| `Partials/ViewerPage.Toolbar.cs` | 235 | keep (shell) | 200–250 | Legitimately shared toolbar wiring |
| `Partials/ViewerPage.Spreadsheet.cs` | 127 | move | `SpreadsheetViewer` glue (~80 in shell) | Format glue |
| `Partials/ViewerPage.KeyboardShortcuts.cs` | 105 | keep | 100–150 | Shared routing; format-specific keys forward to focused viewer |
| `Partials/ViewerPage.Helpers.cs` | 22 | merge | dissolve into shell/helpers | Fragmentation without value |
| `Partials/ViewerPage.Translator.cs` | 55 | keep | 50–80 | Dialog launch shared across formats |
| `ViewerPage.xaml.cs` | 280 | shrink | ~200 shell + registry | Loses per-format teardown to `CleanupAsync` |
| `ViewerViewModel.cs` | 131 | keep | 150–220 | Gains sole-ownership of state (F3 fix) |
| **Cluster total** | **6,823** (15 partials + main) | | **~1,200 shell + ~2,400–2,900 viewers** | Every remaining file has one change driver (AF-01-corrected figures; 7,587 with XAML + VM) |

## 2. Shell & pages

| File | LOC | Verdict | Target | Notes |
|---|---:|---|---|---|
| `UI/Shell/TitleBarWindow.xaml.cs` | 1,331 | split | ~700 + SessionService 120–200 + QuickLookHotkeyService 150–250 | F7; session restore and hotkey ownership are independent concerns |
| `Features/Tools/ToolsPage.xaml.cs` | 1,174 | review, phased split | 600–900 | Each tool (Word→PDF, PDF merge/split…) → own panel control later; not urgent, cohesive enough |
| `Features/Settings/SettingsPage.xaml.cs` | 532 | keep | ≤550 | Sections already grouped |
| `Features/Home/HomePage.xaml.cs` | 480 | keep | ≤500 | Fine |
| `Features/Viewer/Spreadsheet/SpreadsheetViewer.xaml.cs` | 1,250 | keep | 1,250 | One cohesive Win2D grid control; splitting virtualization/painting/selection is possible but low-value — only if selection logic grows |
| `Rendering/Pdf/PdfAnnotationService.cs` | 527 | keep | ≤550 | Engine service, focused |
| `Services/Updates/UpdateService.cs` | 600 | keep (phase 2 option: extract signature verification) | ≤650 | Update check + verification + download orchestration |
| `Services/Translation/TranslationModelManager.cs` | 471 | keep | ≤500 | Model lifecycle |

## 3. Engines, interop, services

| Group | Current sizes | Verdict |
|---|---|---|
| `DocxDocument.cs` 537, `PptxDocument.cs` ~250, `SpreadsheetDocument.cs` ~250, `PdfRenderer.cs` ~300, `PdfTextService.cs` ~200 | keep as-is | Each is the single C# owner of one native engine; parallel in shape — the most maintainable code in the repo |
| `*Native.cs` bindings 124–200 | keep | Bindings should stay dumb; growth signals logic leaking into bindings |
| `PdfViewerService.cs` 284 + interface 95 | keep | Interface earns its keep (viewer + Tools page) |
| Storage stores (4 files ~100 each) | keep | Right-sized |
| Logging 4 files | keep | Right-sized |
| `App.xaml.cs` 392 | split → ~180 + ActivationService 150–250 | F12 |

## 4. Rust crates

| Crate | lib.rs LOC | Verdict |
|---|---:|---|
| `pdfcraft_ffi` | 2,184 | Single-file lib.rs is the largest maintained Rust file. Optional (Phase 9, only if PDF feature work continues): split into `ffi.rs` (exports), `render.rs`, `annotation.rs`, `export.rs`, `doc.rs` modules — mechanical, no ABI change. Not required; crate is cohesive today. |
| `wordcraft_ffi` 895, `deckcraft_ffi` 575, `gridcraft_ffi` 746, `translation_ffi` 584 | keep | All within range; vendored code excluded |

## 5. New files (from doc 05) — target sizes

`DocumentViewerRegistry` 50–80 · `Contracts/*` 15–40 each · `ActivationService` 150–250 · `SessionService` 120–200 · `QuickLookHotkeyService` 150–250 · `Interop/README.md` ~60 lines · `NamespaceDependencyTests` 80–150 · viewer controls as tabled above.

## 6. Anti-goals

- No file split merely for LOC: `SpreadsheetViewer` (1,250) and `ToolsPage` (1,174) stay whole until a second change driver appears.
- No merging of the four engine `*Document` classes into a generic one — parallel duplication across a C ABI is cheaper than a wrong abstraction.
- XAML LOC is not budgeted — layout verbosity is intrinsic; only code-behind is measured.
