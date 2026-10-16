# 07 — Shared vs. Format-Specific Ownership Matrix (re-audit 2026-10-16)

✔ = implemented · ○ = planned (roadmap phase) · — = not applicable

## Viewer capabilities

| Capability | Pdf | Docx | Pptx | Xlsx | Text | Fallback | Owner |
|---|---|---|---|---|---|---|---|
| `IDocumentViewer` (load/cleanup) | ○ P1 | ○ P2 | ✔ | ✔ | ✔ | ✔ | shell lifecycle |
| `ISupportsZoom` | ○ P1 | ○ P2 | ○ P2 | ✔ | — | — | ViewerViewModel |
| `ISupportsSearch` | ○ P1 | ○ P2 | ○ P2 | ✔ | ✔ | — | shell panel + viewer hits |
| `ISupportsTextSelection` | ○ P1 | ○ P2 | ○ P2 | ✔ | ✔ | — | viewer |
| `ISupportsOutline` | ○ P1 | ○ P2 | ○ P2 | — | — | — | shell sidebar host |
| `ISupportsThumbnails` | ○ P1 | ○ P2 | ○ P2 | — | — | — | shell sidebar host |
| `ISupportsAnnotations` | ○ P1 | — | — | — | — | — | viewer (deferred flush) |
| Own viewport/scroll | ○ P1 | ○ P2 | ○ P2 | ✔ | ✔ | — | viewer control |
| Deferred save | ○ P1 | — | — | — | — | — | viewer + shell flush rule |

Registry today: Text (75 extensions), Spreadsheet (.xlsx/.xlsm/.xltx/.xltm), Fallback (.xls + unclaimed). PptxViewer compiled but unwired (RA-04). Pdf/Docx join in Phases 1–2.

## Shared components

| Component | Consumers | State owner | Notes |
|---|---|---|---|
| `ViewerToolbar` (+`ViewerToolbarIcons.xaml`) | shell | shell (→ VM bindings, P3) | `PdfAnnotationTool` enum hosted here (RA-03) |
| `PdfMarkupToolbar` | Pdf wiring | Pdf markup flow | markup color/opacity/tool events |
| `SearchHighlighter` | Text now; Pdf/Docx/Pptx via layers | viewer instances | text-run highlighting utility |
| `DocumentInfoDialog`, `TranslationDialog`, `TranslationModelsDialog`, `ViewerPopupStyle` | shell | shell | code-only dialogs |
| Find bar (in `ViewerPage.xaml`) | shell | shell query state | execution via `ISupportsSearch` |
| Sidebar (thumbnails/outline/bookmarks/notes) | shell | shell hosts; viewers supply data via contracts | bookmarks/notes are shell-owned features |
| Zoom pill | shell | ViewerViewModel | math in viewers |
| `NativeBitmap`, `RenderBudget`, `TextFileReader` | engines/viewers | consuming engine/viewer | media-only boundary |
| `Logger`, `DiagnosticsService`, `AppSettings`, `NativeCallGate`, `UIThreadHelper` | everything | infrastructure singletons | — |

## Format-specific (never unified)

| Concern | Home |
|---|---|
| PDF selection geometry + annotation tools | `PdfSelectionLayer`, `PdfAnnotationLayer`, `PdfTextService`, `PdfAnnotationService` |
| DOCX pagination/outline | `DocxViewer`, `DocxDocument` |
| PPTX slide loop + presentation mode | `PptxViewer` |
| XLSX grid/selection | `SpreadsheetViewer` (Win2D) |
| Text/code capping + line numbers | `TextViewer`, `TextFileReader` |
| Fallback info card | `FallbackViewer` |

The matrix is enforceable: after Phase 2, `Features/Viewer` must contain no `else if (ext ==` and no format-named methods outside `Formats/` (checked by Phase 5 architecture tests + review).
