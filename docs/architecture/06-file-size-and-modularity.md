# 06 — File Size and Modularity (measured 2026-10-16)

Sizes are **measured** (`wc -l`) unless marked `(target)`. Targets follow responsibility boundaries, not line counts; no file is split or kept solely for a number.

## Files above guideline size and their disposition

| File | LOC now | Final | Rationale |
|---|---|---|---|
| `Formats/Spreadsheet/SpreadsheetViewer.xaml.cs` | 1,382 | keep | cohesive Win2D grid control + theme palette + factory; one responsibility (the grid); precedent control |
| `Features/Tools/ToolsPage.xaml.cs` | 1,176 | keep | out of scope (carried decision); convert/merge/split tools call engines directly |
| `UI/Shell/TitleBarWindow.xaml.cs` | 1,052 | keep (revised target ~1,050, RA-15) | after extraction it is genuinely one responsibility: window chrome + tab management + tear-out + drag-drop + picker + presentation chrome; further splits would create a `TitleBarWindow.Partial` sprawl for no consumer benefit |
| `ViewerPage.Pdf.cs` | 1,365 | **dissolve** | four formats' load/render logic in one file — the modularity failure being fixed |
| `ViewerPage.PdfAnnotations.cs` | 859 | → `PdfAnnotationLayer.cs` (859) | PDF-only concern, independent change driver |
| `ViewerPage.PdfTextSelection.cs` | 730 | → `PdfSelectionLayer.cs` (730) | ditto |
| `ViewerPage.Viewport.cs` | 642 | → split: PDF loop to PdfViewer, ~100 stays | mixed PDF rendering + shared sizing |
| `ViewerPage.Search.cs` | 534 | → split: per-format parts to viewers, ~250 stays | legacy dual path collapses once viewers implement `ISupportsSearch` |
| `ViewerPage.Sidebar.cs` | 504 | → split: outline loaders to viewers, ~250 stays | hosting vs. format data |
| `Rendering/Docx/DocxDocument.cs` | 538 | keep | engine: open/render/text/search/export |
| `Features/Home/HomePage.xaml` | 652 | keep | declarative markup, not logic |
| `Services/Updates/UpdateService.cs` | 600 | keep | release check + wintrust verification, cohesive |
| `Rendering/Pdf/PdfAnnotationService.cs` | 529 | keep | FFI CRUD + history |
| `Features/Settings/SettingsPage.xaml(.cs)` | 529+532 | keep | page + logic, single concern |
| `ViewerPage.OfficeTextSelection.cs` | 516 | **dissolve** | two formats' selection logic in one file |
| `ViewerPage.Zoom.cs` | 302 | → split: math to PdfViewer, ~120 stays | |
| `ViewerPage.Presentation.cs` | 240 | **dissolve** | mixes pptx presentation with global shortcut scaffolding |
| `PdfViewer.xaml.cs` (target) | ~550 (target) | NEW | PDF load/render/thumbs — matches the partials it absorbs minus layers |
| `PptxViewer.xaml.cs` (target) | 179 → ~450 (target) | EDIT | absorbs shell's parallel slide implementation |

## Target sizes (guidance)

- Viewer shell (`ViewerPage.xaml.cs`): ~300. Viewer controls: 150–600. Layers: as-is (600–900 acceptable — single concern). Engines: 100–550. Services: 30–600. Tests: file per type under test.
- Every new file must justify a separate responsibility; merges must produce a coherent one. `ViewerPage.Helpers.cs` (22 LOC) fails that bar → merge.

## Verification of estimates

Current LOC per file: inventory in doc 01 (measured). The 2026-10-16 re-audit replaces all pre-implementation estimates (the old "6,823 LOC partials" figure is now 6,104 across 13 partials + 352 main + 501 XAML).
