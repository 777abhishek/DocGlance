# Architecture Review Findings — Contradictions, Decisions, Risks

Reconciliation pass over the audit set (`01`–`10`, `README`, `tree.md`) against the repository at `main` @ `6fa6515`. Each finding states the conflict, the repository evidence, the resolution, and where it is now reflected. IDs `AF-01…` are referenced by the other documents.

## AF-01 — ViewerPage partial count and LOC misstated across the doc set
**Conflict**: `01` said "16 files, 7,352 LOC" for `Partials/` and "17 partials" in prose; `02` diagram said "16 partials ~7,900 LOC"; `05`/`06` summed the cluster to "~8,800 LOC".
**Evidence**: `find Features/Viewer/Partials -name '*.cs'` → **15** files; `wc -l` sum = **6,543**; plus `ViewerPage.xaml.cs` (280) = **6,823** LOC of code-behind for the class; +`ViewerPage.xaml` (633) +`ViewerViewModel.cs` (131) = 7,587 for the page cluster.
**Resolution**: corrected everywhere to *15 partials + main file, 6,823 LOC class code-behind*. Targets restated: shell (page + 5–6 remaining partials) ≈ 1,200 LOC; viewers ≈ 2,400–2,900 LOC.
**Why it matters**: the dissolution plan's denominator was wrong by ~25%; progress measurement against it would be meaningless.

## AF-02 — "UI-free Rendering layer" vs WinUI bitmap types crossing the boundary
**Conflict**: `03` declares Rendering engines "UI-free"; `08` hand-waved "documented exception" for `IPdfViewerService` returning `WriteableBitmap`; `NativeBitmap` depends on `WriteableBitmap` + `DispatcherQueue`.
**Evidence**: `Services/Documents/IPdfViewerService.cs` (`RenderPageProgressiveAsync → Task<WriteableBitmap?>`), `Rendering/NativeBitmap.cs` (UI-dispatcher bitmap creation), `ViewerPage` consumes bitmaps directly.
**Resolution**: the boundary is **"no XAML *controls*, no Features/UI references"** — Rendering may use WinUI *media/imaging primitives* (`WriteableBitmap`) and `DispatcherQueue`, because pixel handoff to the composition thread is an engine responsibility here. Rewriting the service to return raw RGBA buffers would be a functional change, not a restructure, and is rejected. Rule now precise in `08 §1`.
**Remaining risk**: none functional; the rule is enforceable by namespace test (`Microsoft.UI.Xaml.Controls` forbidden in `Rendering.*`).

## AF-03 — `IViewerServices` referenced but never defined
**Conflict**: `03 §3` factory signature: `Create(ViewerViewModel state, IViewerServices services)` — no such type exists in any doc or the repo.
**Evidence**: grep — zero occurrences outside that sentence.
**Resolution**: removed. Signature is `IDocumentViewer Create(ViewerViewModel viewModel)`. Viewers that need host services (password prompt, dialogs) use their own `Control.XamlRoot`. Reflected in `03 §3`, `tree.md` contract entry.

## AF-04 — Contracts location ambiguous ("Features/Viewer/Contracts or Core")
**Conflict**: `03 §4` left the folder open; `Core` cannot host them.
**Evidence**: `IDocumentViewer.Control` is `FrameworkElement` (WinUI); `08` (as amended by AF-02) keeps Core WinUI-free.
**Resolution**: **`Features/Viewer/Contracts/`** — final; stated in `03`, `tree.md`.

## AF-05 — Layer rule forbade `Services → Interop` while permanently requiring it
**Conflict**: `08` table said Services may reference Interop "only until translation gains an engine class (then never)", but the target tree keeps `Services/Translation/TranslationService` consuming `Interop/Translation/TranslationNative` forever.
**Evidence**: translation is an engine without a document format; creating a `Rendering/Translation` wrapper would add a pass-through class with no second implementation — exactly what the constraints forbid.
**Resolution**: **permanent, documented exception**: `Interop/Translation/*` may be consumed by `Services/Translation/*` only. All other Interop folders are consumed exclusively by `Rendering/*` engines. Encoded in `08 §1` and the namespace dependency test.

## AF-06 — "Office renderers are unreachable" was wrong for `.xls`
**Conflict**: `09` justified deleting `Rendering/Office/*` as unreachable; the dispatch handles `.xlsx/.xlsm/.xltx/.xltm` (xlsx branch), and `.csv/.tsv` are in `_textExts` (`ViewerPage.Text.cs`) — but **`.xls` matches none of these** and falls through to `RendererRegistry` → `XlsxRenderer` → info card today.
**Evidence**: `ViewerPage.Pdf.cs:120-143` extension chain + `_textExts` set + `XlsxRenderer.SupportedExtensions = [".xlsx",".xls",".csv"]`.
**Resolution**: deletion stands **provided `FallbackViewer`'s factory claims `.xls`** (and any other registry-only extension), producing the identical `ViewerInfoCard`. Behavior-preserving; captured in `tree.md` (FallbackViewer extensions) and `10` Phase 4 acceptance.

## AF-07 — `IFileScanner` disposition upgraded from "optional fold" to DELETE
**Conflict**: `09` said "optional keep; recommend fold".
**Evidence**: `grep -rln IFileScanner` → matches **only its own file**. `FolderScanner` does not implement or reference it.
**Resolution**: `Core/Interfaces/IFileScanner.cs` moves to the deletion ledger (still grep-verified at deletion time). Core keeps only `IThumbnailProvider` (4 real consumers).

## AF-08 — `ViewerPage.Translator.cs` omitted from kept-partials lists
**Conflict**: `05`/old `tree.md` listed the surviving partials without `Translator.cs` while also not dissolving it.
**Evidence**: file exists (55 LOC), launches `TranslationDialog`, used by `SpreadsheetViewer.TranslateRequested` wiring in `ViewerPage.xaml.cs:62`.
**Resolution**: `ViewerPage.Translator.cs` is **[KEEP]** in all trees/roadmaps.

## AF-09 — Rust-touching phases contradict the "Rust intact" decision
**Conflict**: `10` Phase 8 renamed `qv_`→`tr_`; Phase 9 split `pdfcraft_ffi/src/lib.rs`; `04`/`03` repeated the optional rename.
**Evidence/decision**: user decision — **Rust is untouched** in this restructuring (the crates have no structural issue).
**Resolution**: both phases deleted from `10` (roadmap renumbered to 8 phases); rename mapping removed from `04`; `03 §6` and `08 §2.8` now state prefixes stay as-is (`pc_/wc_/gc_/dc_/qv_`). `docs/offline-translation.md` untouched.

## AF-10 — Small factual errors in `01`
Program.cs is **232** LOC (doc said 276); App.xaml is **22** (doc said 35); a malformed table row ("`Pdf/PdfViewerService.cs`? — no:") garbled the PdfViewerService location. Fixed; the service's true location is `Services/Documents/` → moves to `Rendering/Pdf/`.

## AF-11 — `AppRegistration` icon-map "may move to Core/Constants"
**Conflict**: `03 §9` suggested a move; `09` said stays.
**Resolution**: **stays** in `Services/Registration/AppRegistration.cs` — unpackaged-only registry detail, single consumer. No move.

## AF-12 — Stale csproj asset exclusions
**Evidence**: `DocGlance.csproj:84-86` excludes `Assets\SampleMedia\**`, `Assets\windows\**`, and 5 named PNGs — **none of these exist** on disk (verified by `ls`). `Assets/AppIconNew.png` exists, is referenced only by `tools/imggen` (generator source), and is not shipped.
**Resolution**: out of code-restructuring scope; recorded as a zero-risk build-config cleanup to fold into `10` Phase 2's commit (delete stale `Exclude` entries). No behavior change either way.

## AF-13 — `05` duplicated the tree (drift risk)
**Conflict**: `05-proposed-file-tree.md` contained a second, slightly different tree from `tree.md`.
**Resolution**: `05` is rewritten as a concise planning view; **`tree.md` is the single authoritative final tree** (README states this).

## AF-14 — Deletion-confidence upgrades from this pass
`CodeRenderer`: confirmed doubly dead — zero consumers **and** `IsCodeFile() => false` with "No longer using separate code viewer" comment (`ViewerPage.Text.cs`). Remains REVIEW-gated only for runtime confirmation. `FileWatcherService`, `Features/Browser/*`, `PageThumbnailSidebar.*`: still zero consumers; REVIEW-gated.

## AF-15 — Counting-rule clarification for the validation report
`App.xaml.cs`, `TitleBarWindow.xaml.cs`, `ViewerPage.xaml.cs` are **SPLIT-in-place** (file survives at same path, reduced) — counted once under SPLIT, not double-counted as KEEP. `ViewerInfoCard.cs` and `ViewerPage.Helpers.cs` are **MERGE** sources (content absorbed; file path disappears). Deletions are 13 (not 12) after AF-07. All documents use these rules; see `architecture-validation-report.md`.

## Risks carried forward
| Risk | Mitigation |
|---|---|
| `.xls` behavior regression (AF-06) | FallbackViewer extension list asserted in Phase 4 acceptance test |
| PDF viewer migration (largest split) | selection/annotation partials move as units; existing `PdfViewerServiceTests` + manual matrix |
| XAML URI updates for moved controls | only 2 URIs in scope (`SpreadsheetViewer` in `ViewerPage.xaml`, `UpdateBanner` in `TitleBarWindow.xaml`); compiler-verified |
| Hidden consumers of REVIEW deletions | each deletion preceded by full-tree grep + one manual run of the affected surface |
