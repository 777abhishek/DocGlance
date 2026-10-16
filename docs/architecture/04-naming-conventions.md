# 04 — Naming Conventions

## 1. Current conventions (evaluated)

The existing code is already close to standard .NET conventions: `PascalCase` types/members, `_camelCase` private fields, `Async` suffixes widely honored, MVVM source generators, namespaces mirroring folders, Rust `snake_case` exports with per-engine prefixes. **The target convention is therefore the current one, made consistent** — the exceptions are what change:

| Inconsistency observed | Evidence |
|---|---|
| `ViewerPage.Pdf.cs` contains the all-format loader | `Features/Viewer/Partials/ViewerPage.Pdf.cs:120-143` (F2) |
| Engine folders named by shape, not format: `Document/` holds DOCX *and* XLSX; `Office/` holds fallbacks; `Presentation/` holds PPTX | `Rendering/Document/{DocxDocument,SpreadsheetDocument,SheetData}` |
| Same concept, two names: `DocumentInfoDialog` vs `TranslationDialog` vs `UpdateBanner` (all dialogs/banners) | `Features/Viewer/Components/`, `Services/Updates/` |
| `qv_` export prefix from the pre-rename app | `native/translation_ffi/src/lib.rs` (`qv_detect_language`…) — **retained by decision (AF-09, Rust intact)** |
| Bindings named after crates in 4 styles of location | `Rendering/Pdf/PdfCraftNative.cs`, `Rendering/Document/WordCraftNative.cs`, `Services/Translation/TranslationEngineNative.cs` |
| Docs use old name | `docs/reliability/*`, `docs/android-kotlin-integration.md` say "QuickView" |

## 2. Target rules

### 2.1 C# / .NET
- Types, public members: `PascalCase`. Private fields: `_camelCase`. Constants: `PascalCase`. Async methods: `Async` suffix (exception: XAML event handlers `OnXxx`/`Xxx_Click`).
- Interfaces: `I` prefix **only for contracts with multiple current/planned implementations or a genuine seam**: keep `IPdfViewerService`; add `IDocumentViewer(Factory)`, `ISupports*` capabilities. Do **not** extract `IXxxService` twins for single-use static services (anti-pattern already avoided by the codebase — keep avoiding it).
- Services: noun describing responsibility (`ThumbnailService`, `SessionService`). No `Manager`, `Helper`, `Util` suffixes for new types (`ViewerPage.Helpers.cs` and `RegistrationHelper` are legacy names that survive only if their content merges away — doc 09).
- ViewModels: `<Feature>ViewModel`. Viewer controls: `<Format>Viewer` (`PdfViewer`, `DocxViewer`, `PptxViewer`, `SpreadsheetViewer`, `TextViewer`, `FallbackViewer`).
- Engine classes: `<Format>Document` for native-handle owners (`PdfDocument`, `DocxDocument`, `PptxDocument`, `SpreadsheetDocument` — three already conform).
- FFI bindings: `<Engine>Native` (`PdfCraftNative`, `WordCraftNative`, `GridCraftNative`, `DeckCraftNative`, `TranslationNative`) — all `internal static partial`, `[LibraryImport]` style (migrate `PdfCraftNative` off `DllImport`).

### 2.2 WinUI 3 / XAML
- Pages: `<Purpose>Page.xaml` + matching `.cs` (`ViewerPage`, `HomePage`, `SettingsPage`, `ToolsPage` — all conform).
- User controls: `<Purpose>Viewer`, `<Purpose>Toolbar`, `<Purpose>Panel`, `<Purpose>Sidebar`, `<Purpose>Card`, `<Purpose>Banner`, `<Purpose>Dialog`. Code-behind file name identical to XAML name (today's code-only `DocumentInfoDialog.cs`/`TranslationDialog.cs` build ContentDialogs in code — allowed, name keeps `Dialog` suffix).
- Resource dictionaries: content-describing names — `Colors.xaml`, `Typography.xaml`, `Controls.xaml`, `FileTemplates.xaml`, `FileTypeIcons.xaml` (conform).
- Partial code-behind of a page (if any survive): `ViewerPage.<Aspect>.cs` where `<Aspect>` matches the aspect's real scope. After restructure, `Partials/` shrinks to shell-level aspects only (`Toolbar`, `Sidebar`, `KeyboardShortcuts`, `Search` panel hosting) — format partials are dissolved into viewer controls.

### 2.3 Rust
- Crates: `<engine>_ffi`, `snake_case` (conform). Modules `snake_case`, types `PascalCase` (conform).
- Export prefixes: one per crate — `pc_`, `wc_`, `gc_`, `dc_`, `qv_` (all retained; the `qv_`→`tr_` rename was dropped — Rust stays intact, AF-09).
- Export grammar: `<prefix>_<verb>_<noun>` + `<prefix>_free_<thing>` for every allocation-returning export (already true).

### 2.4 C++ / interop
- No C++ exists in the repo. The C ABI headers are implied by Rust exports + C# structs; `Interop/README.md` (new) documents the ABI: struct layouts mirrored in C# `[StructLayout]`, `IntPtr` handles, `nuint` lengths, UTF-8 marshalling, ownership column ("Rust allocates / C# frees via `<fn>`").

### 2.5 Folders
- Feature folders `PascalCase` singular-by-role: `Features/<Feature>`, subfolders `Viewers/`, `Components/`, `Contracts/`.
- Engine folders **by format**: `Rendering/Pdf`, `Rendering/Docx`, `Rendering/Xlsx`, `Rendering/Pptx`, `Rendering/Shared` (replaces the shape-named `Document/Office/Presentation/Code/Text`).
- `native/<crate>` unchanged; `vendor/` inside crates stays.

### 2.6 Tests
- Mirror the main tree: `DocGlance.Tests/<SamePath>/<TypeName>Tests.cs` (current pattern — conform). New architectural tests: `DocGlance.Tests/Architecture/NamespaceDependencyTests.cs`.

## 3. Rename mapping (important existing → proposed)

| Current | Proposed | Kind |
|---|---|---|
| `Features/Viewer/Partials/ViewerPage.Pdf.cs` | split: dispatch → `DocumentViewerRegistry`; PDF parts → `Viewers/Pdf/PdfViewer.cs` | split |
| `Features/Viewer/Spreadsheet/SpreadsheetViewer.xaml` | `Features/Viewer/Viewers/Spreadsheet/SpreadsheetViewer.xaml` | move |
| `Rendering/Presentation/PptxViewerControl.xaml` | `Features/Viewer/Viewers/Pptx/PptxViewer.xaml` (renamed to pattern) | move+rename |
| `Rendering/Presentation/PptxDocument.cs` | `Rendering/Pptx/PptxDocument.cs` | move |
| `Rendering/Presentation/DeckCraftNative.cs` | `Interop/DeckCraft/DeckCraftNative.cs` | move |
| `Rendering/Document/{WordCraft,GridCraft}Native.cs` | `Interop/WordCraft/…`, `Interop/GridCraft/…` | move |
| `Rendering/Pdf/PdfCraftNative.cs` | `Interop/PdfCraft/PdfCraftNative.cs` (+`LibraryImport` migration) | move |
| `Services/Translation/TranslationEngineNative.cs` | `Interop/Translation/TranslationNative.cs` | move+rename |
| `Services/Documents/PdfViewerService.cs` (+interface, `PdfLoadResult`) | `Rendering/Pdf/` | move |
| `Services/Updates/UpdateBanner.xaml` | `UI/Controls/UpdateBanner.xaml` | move |
| `Core/Models/Pdf*.cs` (6 files) | `Rendering/Pdf/Models/` | move |
| `Rendering/Office/*Renderer.cs`, `RendererRegistry.cs`, `Core/Interfaces/IFileRenderer.cs` | — (deleted; superseded by `DocumentViewerRegistry`) | delete |
| `Rendering/Code/CodeRenderer.cs` | — (delete after consumer-verification) | delete |
| Rust `qv_*` exports (5) | unchanged | keep — rename dropped (AF-09) |

Exceptions (renamed *not*, with rationale): `TitleBarWindow` (established shell name, role is clear), `AppRegistration`/`RegistrationHelper` (registry semantics accurate for HKCU file-association registration), `Program.cs`, crate names.

## 4. Preferred vs discouraged examples

Preferred: `PdfViewer`, `DocxDocument`, `PdfTextService`, `SearchPanel`, `SessionService`, `ISupportsSearch`, `pc_render_page`→C# `PdfCraftNative.RenderPage`.
Discouraged (new code): `DocumentManager`, `CommonHelper`, `ServiceFactory`, `IViewerService` (contract without a second implementation), `Misc/`, `New*`/`*2`, `qv_*`-style stale branding.
