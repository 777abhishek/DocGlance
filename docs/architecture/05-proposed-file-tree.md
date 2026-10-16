# 05 — Proposed File Tree (planning view)

**The authoritative, exhaustive final tree is [`tree.md`](tree.md).** This document is the concise planning summary of *why* the tree is shaped this way; it intentionally contains no competing file-by-file listing. Any discrepancy is a bug in this file, not in `tree.md`.

## Shape decisions

1. **One app project + one test project.** Folder-level modularity with namespace rules (08); no new csproj libraries — WinUI/MSIX cross-assembly resource friction buys nothing today.
2. **Five top-level module kinds**: `Common` (pure helpers) → `Core` (format-neutral models) → `Infrastructure` (cross-cutting) → `Interop` (FFI) → `Rendering` (engines by format) → `Services` (app services) / `Features` (pages + viewers) / `UI` (shell + shared controls).
3. **One format, one folder** in two places: `Rendering/<Format>` (engine, UI-control-free) and `Features/Viewer/Viewers/<Format>` (viewer control). This is the SpreadsheetViewer precedent generalized.
4. **The ViewerPage partial family dissolves** into a ~1,200-LOC shell (page + 7 partials at final scope) plus five viewers + fallback; dispatch becomes `DocumentViewerRegistry`; capabilities via `ISupports*` contracts.
5. **Rust untouched.** `native/` is reproduced in `tree.md` for completeness only.

## Headline moves (details in tree.md)

| From | To |
|---|---|
| 5 `*Native.cs` bindings (4 folders) | `Interop/<Engine>/` |
| `Rendering/{Document,Office,Presentation,Text,Code}` | `Rendering/{Docx,Xlsx,Pptx,Shared}` + deletions |
| `Services/Documents` trio + `Core/Models/Pdf*` (6) | `Rendering/Pdf/` |
| `PptxViewerControl` (Rendering) | `Viewers/Pptx/PptxViewer` |
| `SpreadsheetViewer` (Viewer/Spreadsheet) | `Viewers/Spreadsheet/` |
| `ViewerInfoCard` (UI/Controls) | `Viewers/Fallback/FallbackViewer` |
| `UpdateBanner` (Services/Updates) | `UI/Controls/` |
| `UIThreadHelper` (Core) | `Infrastructure/Extensions/` |
| App/TitleBarWindow internals | `ActivationService` / `SessionService` / `QuickLookHotkeyService` |

## Scale (from tree.md, AF-01-corrected numbers)

- ViewerPage class today: **15 partials + main = 6,823 LOC** code-behind (7,587 with XAML + VM) → shell ~1,200 + viewers ~2,400–2,900.
- App code files: 139 today → **139 final** (82 KEEP, 29 MOVE, 7 SPLIT-in-place, 7 dissolve, 1 merge, 13 delete, 21 new — see `09` and the validation report).
- Deleting: legacy renderer pipeline (5 files, Phase 5) + 8 REVIEW-gated dead files (Phase 7).
