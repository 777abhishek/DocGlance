# 04 — Naming Conventions (reconciled re-audit 2026-10-16)

Rules below match the implemented tree; deviations found in the re-audit are folded in (RA-02, RA-03).

## C#
- **PascalCase** for types, methods, properties, constants; **camelCase** for locals/parameters; `_camelCase` for private fields (`_pdfService`, `_viewerRegistry`); `internal` fields allowed inside the `ViewerPage` partial family for cross-partial access.
- **Namespaces** mirror folders: `DocGlance.{Area}[.Subarea]` — `DocGlance.Features.Viewer.Formats.Pdf`, `DocGlance.Interop.PdfCraft`, `DocGlance.Rendering.Pdf`. One namespace per folder; no `Internal`/`Impl` suffixes.
- **Interfaces** `I`-prefixed, capability-named: `ISupportsZoom`, not `IZoomable`. Contracts live in `Features/Viewer/Contracts` (they need `FrameworkElement`; not Core — carried AF-02).
- **Files:** one primary type per file, file named for it (`PdfViewer.xaml.cs` may contain its `PdfViewerFactory` — factory-of-the-file pattern, as in `TextViewer.xaml.cs`, `SpreadsheetViewer.xaml.cs`, `FallbackViewer.cs`). Small adjacent types (e.g. `SlideItem`) may live in their consumer's file.
- **Enums:** a shared-component enum may live in its consumer's file until a second consumer appears (currently `PdfAnnotationTool` in `ViewerToolbar.xaml.cs` — accepted, RA-03).
- **Tests:** `{Type}Tests.cs`, mirroring the main tree path under `DocGlance.Tests/`.

## Viewers and engines
- **Format viewers:** `{Format}Viewer` (`PdfViewer`, `DocxViewer`, `PptxViewer`, `SpreadsheetViewer`, `TextViewer`, `FallbackViewer`) in `Features/Viewer/Formats/{Format}/`; factory `{Format}ViewerFactory` in the viewer's file. Folder name = format (`Formats/`, adopted over `Viewers/`, RA-02).
- **Engines:** format-named documents in `Rendering/{Format}/` (`DocxDocument`, `SpreadsheetDocument`, `PptxDocument`); PDF keeps its service/state/service-class names (`PdfViewerService`, `PdfDocumentState`, `PdfRenderer`, `PdfTextService`, `PdfAnnotationService`) — proven, do not rename.
- **Layer classes:** `{Concern}Layer` inside the viewer folder (`PdfSelectionLayer`, `PdfAnnotationLayer`) — code-only files with no XAML.

## FFI
- Binding classes `{Engine}Native.cs` under `Interop/{Engine}/`, named for the Rust crate (`pdfcraft_ffi` → `PdfCraft`), not the format. Export prefixes stay Rust-side (`pc_`, `wc_`, `gc_`, `dc_`, `qv_` — `qv_` stays per carried decision). Engine folders stay `internal` to their consumers: Rendering engines + `Services/Translation` only.

## XAML
- Pages/controls PascalCase matching code-behind class; partials of a page: `{Page}.{Concern}.cs` under `Partials/` (`ViewerPage.Search.cs`). Resource dictionaries named for content (`ViewerToolbarIcons.xaml`, `Colors.xaml`); merged in `App.xaml`. Every XAML control file pairs with `.xaml.cs`; resource dictionaries and style-only dictionaries have none by definition.
- XAML resource URIs use the rooted form (`ms-appx` / relative-to-app); renaming/moving a XAML control requires updating its URI (only `UpdateBanner` in `TitleBarWindow.xaml` remains in scope).

## Documentation
- `docs/architecture/NN-{topic}.md` numbered by reading order; `tree.md` unnumbered (authoritative); findings/validation reports named `architecture-{review-findings,validation-report}.md`.
