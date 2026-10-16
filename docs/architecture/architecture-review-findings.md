# Architecture Review Findings — Re-audit (2026-10-16)

Register `RA-xx` covers findings from this re-audit of the post-`10db6a8` repository. Register `AF-xx` (pre-implementation audit, commit `25d7fa1`) is superseded; carried-forward decisions are listed at the end.

## Implementation-state findings

**RA-01 — The original roadmap was executed out of order, and the result is safe.** `10db6a8` shipped Phases 1–4 plus Phase 5.4 (legacy pipeline deletion: `RendererRegistry`, `IFileRenderer`, `Rendering/Office/*Renderer`, `ViewerInfoCard`) in one commit, instead of incrementally. Verified safe: the deleted renderers were only reachable via the deleted registry; `ToolsPage` consumes the moved engines (`Rendering.{Docx,Pptx,Xlsx,Pdf}`) directly; the full format matrix was the commit's stated validation. Resolution: accepted as-is; the remaining roadmap restarts numbering at Phase 1 (PDF extraction).

**RA-02 — Viewer folder named `Formats/`, not `Viewers/`.** The original tree specified `Features/Viewer/Viewers/`; implementation used `Features/Viewer/Formats/` (Text, Spreadsheet, Pptx, Fallback). Resolution: **adopt `Formats/`** — it is implemented, equally descriptive, and renaming would be churn with no benefit. Naming conventions (doc 04) and this tree updated accordingly.

**RA-03 — Two components not in the original tree, plus an enum placement.** `Components/PdfMarkupToolbar.xaml(.cs)` and `ViewerToolbarIcons.xaml` were extracted from `ViewerToolbar` in `10db6a8` (cleaner toolbar, centralized glyphs). The `PdfAnnotationTool` enum lives in `ViewerToolbar.xaml.cs`. Resolution: adopt both components into the final tree; keep the enum where it is (moving it buys nothing — its only consumers are ViewerToolbar and the PDF wiring that will consume the toolbar's events). Doc 04 documents the "shared component hosts a format-specific enum until its consumer is format-neutral" rule.

**RA-04 — `PptxViewer` is dormant.** The control was moved+renamed into `Formats/Pptx/` but is constructed nowhere: `ViewerPage` still renders PPTX itself through `ViewerPage.Pdf.cs` lines 438–919 (slide cache, viewport loop, search highlights — a parallel implementation of what PptxViewer already contains). Resolution: flagged as the top Phase 2 risk; Phase 2 wires it and deletes the shell's parallel path. Until then it must not be "cleaned up" — it is the destination, not dead code.

**RA-05 — ViewerViewModel is still a mirror, not the owner.** `ViewerPage` keeps `_page/_zoom/_total/_rotation` and pushes them via `SyncToViewModel()` (8 call sites, confirmed by grep); the class doc comment states this openly. Resolution: sole-ownership moves to Phase 3, after all five formats are registry-served so the switch is format-agnostic. Old tree claimed this was a Phase 5.3 item; unchanged in substance.

**RA-06 — Registry exists but the shell re-dispatches on factory types.** `ViewerPage.LoadDocumentCoreAsync` handles `.pdf/.docx/.ppt` literals, then resolves the registry **and pattern-matches** `factory is SpreadsheetViewerFactory / TextViewerFactory` before calling the old per-format load methods. Resolution: interim mechanism (per original Phase 4 design: "registry replaces xlsx/text/else branches; pdf/docx/pptx remain temporarily"); Phase 2 removes the literals, Phase 3 removes the type-matching. Acceptance check "no format literals in dispatch" applies to Phase 2+.

**RA-07 — `DllImport` sanctioned list expanded vs. original Phase 1 acceptance.** Original acceptance allowed `DllImport` only in `Program.cs`, `TitleBarWindow`, `AppRegistration`, `UpdateService`. Grep now also finds it in `Services/Input/GlobalHotkeyRegistration.cs`, `Services/Input/QuickLookHotkeyService.cs`, `Features/Settings/SettingsPage.xaml.cs`, `tools/EmbedIcon/EmbedIcon.cs`. All are Win32 P/Invokes (hotkey registration, shell APIs, icon embedding) — **not engine FFI**. Resolution: rule restated in doc 08 as "all engine bindings live in `Interop/` and use `[LibraryImport]` (verified: 25+14+11+12+5); Win32 P/Invokes are permitted only in the named files". The intent (no engine DllImports outside Interop) holds exactly.

**RA-08 — One-line Rust change since baseline.** `translation_ffi/src/lib.rs` test temp-dir string `quickview-translation-test-*` → `DocGlance-translation-test-*`. No ABI/export/struct change; export counts verified unchanged (pc_×27, wc_×14, gc_×11, dc_×12, qv_×5). "Rust intact" decision upheld for the ABI; the rename is cosmetic and accepted.

**RA-09 — Dangling documentation references in code comments.** `App.xaml.cs:34` and `Infrastructure/Configuration/AppSettings.cs:110` cite `docs/reliability/CRASH_DIAGNOSTICS.md`, which no longer exists (`docs/` was emptied at user request in `10db6a8`). Resolution: Phase 5 rewords the comments (or the user restores a reliability note). Out of scope for this documentation-only re-audit to edit code comments.

**RA-10 — `docs/` emptied by user decision.** The original tree's docs section (01–10 + reliability + offline-translation + android-kotlin notes) was deleted in `10db6a8` "as requested". This 14-document set is the re-audit replacement. `docs/offline-translation.md` remains deleted by decision (recoverable via `git show 6fa6515:docs/offline-translation.md`); the `.gitignore` whitelist lines are retained so both `docs/architecture/` and a restored `offline-translation.md` would be trackable.

**RA-11 — csproj stale excludes resolved.** The `Assets\windows|SampleMedia|legacy-PNG` `Exclude` entries (AF-12) are gone from `DocGlance.csproj`. Closed, no action.

**RA-12 — Tests were reorganized with the moves.** 17 test files mirror the new layout (`Rendering/Pdf/*`, `Rendering/Docx/*`, `Features/Viewer/DocumentViewerRegistryTests.cs`). The required AF-06 assertion exists: `Resolve_ClaimsXlsWithFallbackFactory` pins `.xls` and a full path ending `.xls` to `FallbackViewerFactory`, plus fallback-for-unclaimed behavior. Carried decision verified in code.

## Carried-forward decisions (from AF-01…AF-15, re-verified 2026-10-16)

| Decision | Status |
|---|---|
| Rust fully intact; no `qv_`→`tr_` rename, no crate splits (AF-09) | ✔ verified (RA-08) |
| Contracts live in `Features/Viewer/Contracts/` (need `FrameworkElement`; cannot be in Core) (AF-02) | ✔ implemented |
| Rendering layer boundary: WinUI *media* primitives (`WriteableBitmap`) allowed in Rendering/Interop; XAML controls not (AF-02) | ✔ holds; PdfViewer/DocxViewer/PptxViewer are `Features` citizens where XAML is fine |
| `.xls` falls to FallbackViewer, pinned by test (AF-06) | ✔ implemented + tested |
| `Services/Translation/*` is the sole sanctioned consumer of `Interop/Translation` (permanent exception) | ✔ documented in `Interop/README.md` |
| `IFileScanner` upgraded to DELETE (AF-07) | ✔ pending Phase 4 (still zero consumers) |
| Partial count corrected to 15 partials + main = 6,823 LOC (AF-15) | updated: after `10db6a8` it is **13 partials + main + XAML = 6,957 LOC** (`Text.cs`, `Spreadsheet.cs` dissolved; PDF/DOCX/PPTX blocks grew) |
| No DI container; no new projects; single app + test csproj | ✔ holds |

## Risks

1. **PDF extraction size** — `ViewerPage.Pdf.cs` (1,365) + `PdfTextSelection.cs` (730) + `PdfAnnotations.cs` (859) ≈ 2,900 LOC is the largest remaining move. Mitigation: whole-layer moves, existing PDF tests, manual matrix per roadmap Phase 1.
2. **Dormant PptxViewer** (RA-04) — compiles but untested at runtime until Phase 2 wires it. The PPTX manual matrix gates the cutover.
3. **State-ownership switch** (RA-05) — subtle binding regressions possible; mitigated by doing it last among viewer work, format-agnostically, with extended ViewerViewModelTests.
4. **Unvalidated build state** — this re-audit is documentation-only; no build or test run was performed. The proposed structure requires build + runtime validation during migration (roadmap global validation applies to every phase).
