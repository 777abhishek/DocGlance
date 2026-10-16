# 10 — Refactoring Roadmap (reconciled with tree.md)

**Not started.** Each phase is dependency-ordered, independently buildable/shippable, and moves the repo toward exactly [`tree.md`](tree.md) — the "Completes" line of every phase lists the final-tree paths it produces, so the roadmap cannot drift from the tree. Rust phases from the earlier draft were **dropped** (decision: Rust intact, AF-09). No phase deletes code before its replacement works and consumers are verified.

Global validation (every phase): `build.ps1` (native DLLs prebuilt) → app launches → open one file per format {`.pdf`, `.docx`, `.pptx`, `.xlsx`, `.txt`, `.md`, `.csv`, `.json`, `.cs`, `.xls`(info card)} → `dotnet test DocGlance.Tests`.

## Phase 0 — Baseline
**Objective**: freeze current behavior as the comparison point.
**Actions**: run the global matrix on `main`; record results (pass/fail per format + Home/Tools/Settings sweep) in the PR description; confirm all 18 test files green.
**Completes**: nothing (baseline only). **Rollback**: n/a.

## Phase 1 — Interop consolidation
**Objective**: one home + one style for FFI bindings.
**Files**: create `Interop/{PdfCraft,WordCraft,GridCraft,DeckCraft,Translation}/`; move the 5 binding files; migrate `PdfCraftNative` 25×`DllImport`→`[LibraryImport]`; write `Interop/README.md`.
**Consumers**: `PdfRenderer`, `DocxDocument`, `SpreadsheetDocument`, `PptxDocument`, `TranslationService` (using updates, compiler-driven).
**Tests**: existing suite (NativeCallGate, PdfViewerService, translation). **Manual**: every format renders (bindings are load-bearing).
**Completes (tree paths)**: `Interop/PdfCraft/PdfCraftNative.cs`, `Interop/WordCraft/WordCraftNative.cs`, `Interop/GridCraft/GridCraftNative.cs`, `Interop/DeckCraft/DeckCraftNative.cs`, `Interop/Translation/TranslationNative.cs`, `Interop/README.md`.
**Rollback**: revert moves. **Acceptance**: no `DllImport` outside sanctioned Win32 files (`Program.cs`, `TitleBarWindow`, `AppRegistration`, `UpdateService`).

## Phase 2 — Engines by format + shared rendering
**Objective**: format-named engine folders; Core becomes format-neutral.
**Files/actions**: `Rendering/Document/DocxDocument.cs → Rendering/Docx/`; `{SpreadsheetDocument,SheetData}.cs → Rendering/Xlsx/`; `Presentation/PptxDocument.cs → Rendering/Pptx/`; `{NativeBitmap,RenderBudget}.cs → Rendering/Shared/`; `Text/TextFileReader.cs → Rendering/Shared/`; `Services/Documents/{IPdfViewerService,PdfViewerService,PdfLoadResult}.cs → Rendering/Pdf/`; `Core/Models/Pdf*.cs` (6) `→ Rendering/Pdf/Models/`; `Core/Extensions/UIThreadHelper.cs → Infrastructure/Extensions/`; delete stale `Assets\windows|SampleMedia|legacy-PNG` `Exclude` entries from csproj (AF-12). Mark `RendererRegistry`, `IFileRenderer`, `Office/*` `[Obsolete]` (deletion in Phase 5).
**Consumers**: ~30 using updates; tests move (`PdfViewerServiceTests`, `PdfEncryptionProbeTests`, `DocxOutlineTests`, `PdfPageTextMapTests`).
**Manual**: full format matrix + Tools page (Word→PDF uses moved engines).
**Completes**: `Rendering/{Pdf(+Models),Docx,Xlsx,Pptx,Shared}` final contents minus nothing; `Infrastructure/Extensions/UIThreadHelper.cs`.
**Rollback**: revert. **Acceptance**: old folders `Rendering/{Document,Office,Presentation,Text,Code}` contain only `[Obsolete]` pipeline files + `CodeRenderer` (Phase 7 item).

## Phase 3 — Shell extractions
**Objective**: `App` and `TitleBarWindow` become wiring/chrome.
**Files**: NEW `Services/Activation/ActivationService.cs` (absorbs `TryOpenFromActivationArgs`, `TryOpenFromCommandLine`, `OpenExternalFiles`, `ShowOpenError`); NEW `Services/Session/SessionService.cs` (absorbs `SaveSession`/`RestoreSession`); NEW `Services/Input/QuickLookHotkeyService.cs` (absorbs hotkey block + `TryGetSelectedExplorerFile`); slim `App.xaml.cs` (~180) and `TitleBarWindow.xaml.cs` (~700).
**Manual**: cold start; second-file Explorer activation (redirect + pipe fallback); Quick Look hotkey incl. multi-window ownership; session restore on/off; tab tear-out; update banner appears.
**Completes**: `Services/Activation/ActivationService.cs`, `Services/Session/SessionService.cs`, `Services/Input/QuickLookHotkeyService.cs`; final-scope `App.xaml.cs`, `UI/Shell/TitleBarWindow.xaml.cs`.
**Rollback**: additive extractions — revert or keep services unused. **Acceptance**: no activation/hotkey/session logic remains in App/TitleBarWindow beyond call-throughs.

## Phase 4 — Viewer contracts + registry + first two viewers
**Objective**: the keystone mechanism, proven on the lowest-risk formats.
**Files**: NEW `Core/Enums/FitMode.cs`; NEW `Features/Viewer/Contracts/` (8 files); NEW `Features/Viewer/DocumentViewerRegistry.cs`; NEW `Features/Viewer/Viewers/Text/TextViewer.xaml`+`.cs` (from `ViewerPage.Text.cs`); MOVE `Spreadsheet/SpreadsheetViewer.* → Viewers/Spreadsheet/` + factory adapter (URI update in `ViewerPage.xaml`); MOVE+RENAME+EXTEND `UI/Controls/ViewerInfoCard.cs → Viewers/Fallback/FallbackViewer.cs` (factory claims `.xls` + fallback). Registry replaces the xlsx/text/else branches of the dispatch; pdf/docx/pptx branches remain temporarily.
**Tests**: NEW `DocumentViewerRegistryTests` (must assert `.xls` → FallbackViewer, AF-06); text/spreadsheet manual matrix.
**Completes**: `Core/Enums/FitMode.cs`, `Features/Viewer/Contracts/*`, `DocumentViewerRegistry.cs`, `Viewers/Text/*`, `Viewers/Spreadsheet/*`, `Viewers/Fallback/FallbackViewer.cs`.
**Rollback**: registry additive; keep legacy branches. **Acceptance**: xlsx/csv/text/md/json/cs via registry; `.xls` shows info card identical to today.

## Phase 5 — Migrate remaining formats; delete legacy pipeline
Four PRs, risk ascending. Each: build viewer control, move partial content per tree §2.1, cut the dispatch branch over to the registry, run that format's manual matrix + regressions.
1. **Docx**: NEW `Viewers/Docx/DocxViewer.xaml`+`.cs` ← `LoadDocx*`, docx viewport/selection/outline parts.
2. **Pptx**: MOVE+RENAME `Rendering/Presentation/PptxViewerControl.* → Viewers/Pptx/PptxViewer.*`, extended with slide loop, text maps, selection, presentation mode.
3. **Pdf**: NEW `Viewers/Pdf/PdfViewer.xaml`+`.cs`, `PdfSelectionLayer.cs`, `PdfAnnotationLayer.cs`; `ViewerViewModel` becomes sole state owner (delete `_page/_zoom/_total/_rotation` mirrors + `SyncToViewModel`).
4. **Pipeline deletion** (after 1–3 verified): DELETE `Rendering/RendererRegistry.cs`, `Core/Interfaces/IFileRenderer.cs`, `Rendering/Office/{Docx,Xlsx,Pptx}Renderer.cs`, `App.RegisterRenderers`, `LoadViaRendererAsync`, `ShowInfoCard`.
**Completes**: `Viewers/{Docx,Pptx,Pdf}/*`; dissolves `ViewerPage.Pdf.cs`, `PdfAnnotations.cs`, `PdfTextSelection.cs`, `OfficeTextSelection.cs`, `Text.cs`(already Phase 4), `Presentation.cs`, `Spreadsheet.cs`; deletion-ledger rows 1–5.
**Acceptance**: dispatch contains zero format literals; `.xls` regression test green; `ViewerViewModelTests` extended and green.

## Phase 6 — Shell polish to final scope
**Objective**: finish the SPLIT-in-place files at target scope.
**Actions**: toolbar/sidebar/search become capability-driven (drop per-format conditionals); `ViewerToolbar` imperative setters (`UpdatePageInfo`, `SetMarkupToolActive`, …) replaced by ViewModel bindings; `ViewerPage.Helpers.cs` MERGE-dissolved; keyboard shortcuts forward to focused viewer; `ViewerPage.Zoom.cs` reduced to coordination (math already moved with Phase 5).
**Completes**: final-scope `ViewerPage.xaml.cs`, `Partials/{Toolbar,Sidebar,Search,Zoom,Viewport,KeyboardShortcuts,Translator}.cs`; removal of `ViewerPage.Helpers.cs`.
**Acceptance**: no `else if (ext ==` anywhere in Features; shortcuts F5/Ctrl+F/Ctrl+wheel work per format.

## Phase 7 — REVIEW-gated deletions + docs reconciliation
**Actions** (each preceded by 09 §12 verification): DELETE `Core/Interfaces/IFileScanner.cs`, `Rendering/Code/CodeRenderer.cs`, `Services/FileSystem/Watcher/FileWatcherService.cs`, `Features/Browser/*` (3), `UI/Controls/PageThumbnailSidebar.*` (2). Add one-line rename/redirect headers to `docs/reliability/*`.
**Completes**: deletion-ledger rows 6–13. **Acceptance**: build + full matrix + grep clean.

## Phase 8 — Architectural enforcement
**Actions**: NEW `DocGlance.Tests/Architecture/NamespaceDependencyTests.cs` encoding 08 §1 (Rendering: no `Microsoft.UI.Xaml.Controls`, no Features/UI refs; Interop consumers = Rendering + Services/Translation only; Core/Common: no WinUI; Services: no XAML controls); wire tests into build script's CI step if present.
**Completes**: final tree fully enforced. **Acceptance**: deliberate layer violations fail `dotnet test`.

## Explicitly out of scope
`ToolsPage` splitting; new csproj projects; **any Rust change** (AF-09); drag-drop/picker validation gap (logged as issue, fixed separately); `installer/winget` regeneration mechanics.

## Risk register
| Risk | Mitigation |
|---|---|
| `.xls` behavior regression | Phase 4 acceptance test pins FallbackViewer claim (AF-06) |
| PDF migration size | selection/annotation move as whole units; existing PDF tests + manual matrix each PR |
| XAML URI breakage | exactly 2 URIs in scope; XAML compiler catches both |
| Hidden consumers of deletions | 09 §12 protocol (grep + manual run + git intent) per file |
| Two dispatch mechanisms coexisting | bounded to Phases 4–5; Phase 5.4 removes the legacy branch as its own PR |
