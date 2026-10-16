# Architecture Validation Report

Validates the reconciled documentation set against the repository (`main` @ `6fa6515`, clean tree) and against itself. Companion to [`tree.md`](tree.md) (authoritative tree) and [`architecture-review-findings.md`](architecture-review-findings.md) (AF-xx resolutions).

**Standing caveat**: this validation is analytical. The proposed structure has not been compiled or run — build and runtime validation happen during each roadmap phase, as specified in `10`.

## 1. Coverage counts

Counting rules (AF-15): one disposition per current file. `[SPLIT-in-place]` files survive at the same path (counted once, under SPLIT). `[SPLIT→dissolve]` files disappear into named destinations. `[MERGE]` sources disappear.

### Current maintained application source (.cs/.xaml, incl. Properties) — 139 files

| Folder | Files | KEEP | MOVE | SPLIT-in-place | Dissolve | MERGE | DELETE |
|---|---:|---:|---:|---:|---:|---:|---:|
| Root + Properties | 4 | 3 | 0 | 1 (`App.xaml.cs`) | 0 | 0 | 0 |
| Common | 5 | 5 | 0 | 0 | 0 | 0 | 0 |
| Core | 19 | 10 | 7 | 0 | 0 | 0 | 2 |
| Infrastructure | 7 | 7 | 0 | 0 | 0 | 0 | 0 |
| Interop (new) | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Rendering | 22 | 4 | 13 | 0 | 0 | 0 | 5 |
| Features | 37 | 19 | 2 | 5 | 7 | 1 | 3 |
| Services | 33 | 26 | 6 | 0 | 0 | 0 | 1 |
| UI | 12 | 8 | 1 | 1 (`TitleBarWindow.xaml.cs`) | 0 | 0 | 2 |
| **Total** | **139** | **82** | **29** | **7** | **7** | **1** | **13** |

Checks: 82+29+7+7+1+13 = 139 ✔. Per-folder sums: 4, 5, 19, 7, 22, 37, 33, 12 = 139 ✔ (matches the `find`-based inventory in `01`).

Notes on categories that could look like double-counting: the 5 SPLIT-in-place partials (`Zoom`, `Sidebar`, `Search`, `Viewport`, and main `ViewerPage.xaml.cs`) keep their path — they are *not* additionally counted as KEEP; `SpreadsheetViewer.*` is MOVE (path change) even though responsibility is unchanged; `ViewerInfoCard.cs` is MOVE+RENAME (`FallbackViewer.cs`), not MERGE/NEW.

### Final application source — 139 files

139 current − 13 DELETE − 7 dissolve − 1 merge (21 removed) + **21 NEW code files** = **139 final** (coincidental equality, verified per-folder in `09` and `tree.md` §1: 4+5+11+8+5+20+46+29+11 = 139 ✔).

NEW code files (21): `Core/Enums/FitMode.cs`; `Features/Viewer/DocumentViewerRegistry.cs`; `Contracts/` ×8; `Viewers/Pdf/` ×4; `Viewers/Docx/` ×2; `Viewers/Text/` ×2; `Services/Activation/ActivationService.cs`; `Services/Session/SessionService.cs`; `Services/Input/QuickLookHotkeyService.cs`. Non-code NEW: `Interop/README.md`, tests ×2 (`DocumentViewerRegistryTests`, `NamespaceDependencyTests`), docs ×2 (findings, validation).

### Other maintained artifacts
- **Tests**: 16 current `.cs` (12 KEEP, 4 MOVE-with-namespace) + csproj KEEP → 18 final (+2 NEW).
- **Rust (INTACT)**: 5 × (`Cargo.toml` + `Cargo.lock` + `src/lib.rs`) = 15 files + `build-native.ps1`; `pdfcraft_ffi/vendor/` = third-party (hayro ×4, lopdf), labeled, excluded from all counts.
- **Docs**: 14 architecture files (this set) + 4 pre-existing docs retained (reliability ×4 incl. subdir, offline-translation, android-kotlin = 6 files).
- **Assets**: enumerated in `tree.md` §1 (logo sets with scale/targetsize variants, 12 FileTypeIcons pairs, 1 font, 3 loose images) — retention policy stated; `AppIconNew.png` = imggen source, unshipped.
- **Tools/installer/build**: retained with policies; `installer/output/` = local build output, not committed.
- **Untracked local artifacts** (not repo content): `DocGlance.pfx`, `build.log`, `bin/`, `obj/`, `native/*/target/`, `tools/*/obj/`.

## 2. Tree acceptance checks (from the task's 10-point list)

| # | Check | Result |
|---|---|---|
| 1 | Every current maintained file has exactly one disposition | ✔ 139/139 mapped in `09`; totals reconcile (§1 above) |
| 2 | Every retained/moved file appears at its final destination | ✔ `tree.md` §1 lists all final paths; cross-checked against `09` tables |
| 3 | Every NEW file has a justified responsibility | ✔ `tree.md` §4 (21 code + infra) |
| 4 | Every deletion has evidence | ✔ `tree.md` §3 ledger (13 rows, each with evidence + phase) |
| 5 | No file listed twice under different final paths | ✔ spot-checked the overlap candidates: SpreadsheetViewer, UpdateBanner, 5 bindings, 6 Pdf models, PptxViewer pair, ViewerInfoCard→FallbackViewer, UIThreadHelper — each exactly one final path |
| 6 | Every XAML has code-behind or documented reason | ✔ all `*.xaml` paired except resource dictionaries (none needed); code-only controls documented (`FallbackViewer`, `DocumentInfoDialog`, `TranslationDialog`, `TranslationModelsDialog`) |
| 7 | Project/resource/test/native-build relationships valid | ✔ single csproj SDK-globs folders; native `Content` items unchanged; exactly 2 XAML URI updates in scope; tests reference main project unchanged |
| 8 | Tree agrees with 04/08/09/10 | ✔ reconciliation edits applied this pass (AF-01…AF-15); roadmap phases each list completed tree paths |
| 9 | No placeholders hide files | ✔ Assets/vendor/installer directories carry explicit inventories or policies; no `...`/`etc.` in maintained-source sections |
| 10 | Feasible in existing structure | ✔ no new projects; folder moves only; Rust untouched |

## 3. Cross-document consistency audit (performed)

- **Tree ↔ inventory (01)**: partial count/LOC corrections applied (AF-01, AF-10); `01` now states 15 partials, 6,543 LOC, Program.cs 232, App.xaml 22; the malformed PdfViewerService row replaced.
- **Tree ↔ 02**: diagram fixed to "15 partials, ~6.8k LOC".
- **Tree ↔ 03**: `IViewerServices` removed; Contracts location finalized; qv_ rename dropped; AppRegistration stays.
- **Tree ↔ 04**: rename-mapping row for `qv_*` marked keep; prefix rule updated.
- **Tree ↔ 06**: cluster totals corrected (6,823 → ~1,200 shell + ~2,400–2,900 viewers).
- **Tree ↔ 07**: capability rows unchanged — verified each still matches a contract in `tree.md` (Zoom/Search/TextSelection/Outline/Annotations/Thumbnails).
- **Tree ↔ 08**: AF-02 boundary (WinUI media OK, controls not) encoded in layer table; AF-05 permanent Translation exception; prefix rule updated.
- **Tree ↔ 09**: exhaustive map rewritten against the final tree; counts in §1 above match `09`'s per-folder tables.
- **Tree ↔ 10**: every phase carries a "Completes" list of exact tree paths; dropped Rust phases (AF-09) renumbered to 8 phases.
- **README ↔ files**: all 14 linked files exist (`ls docs/architecture` = README, 01–10, tree, findings, validation — validated post-write).
- **FFI contracts ↔ actual exports**: `tree.md` Interop section lists 27+14+11+12+5 exports with prefixes matching `grep 'pub extern "C" fn'` per crate; binding files map 1:1 to crates.
- **Viewer contracts ↔ lifecycle/state model**: `IDocumentViewer.CleanupAsync` replaces the per-format teardown catalogued in `02 §2.6`; `ViewerViewModel` sole-ownership removes the dual-state issue in `02 §5` — verified the contract set covers every capability the shell UI needs (toolbar, sidebar, search panel, copy, translation source).

## 4. Unresolved items (explicit)

| Item | Status | Evidence needed |
|---|---|---|
| 8 Phase-7 deletions (`IFileScanner` upgraded to DELETE this pass — AF-07; CodeRenderer, FileWatcherService, Browser ×3, PageThumbnailSidebar ×2) | Provisions in ledger | pre-delete grep + one manual run each (`09 §12` protocol) |
| `.xls`/`.csv` fallback equivalence | Asserted from code reading (AF-06) | `DocumentViewerRegistryTests` acceptance in Phase 4 |
| `XlsxRenderer`-claimed `.xls` icon/type description parity in FallbackViewer | Detail to preserve | manual `.xls` open comparison during Phase 4 |
| Hidden runtime consumers (reflection/native/XAML binding) of any moved type | None known; compiler catches C#/XAML | standard full-matrix run each phase |
| `docs/reliability/*` header annotations | Scheduled Phase 7 | — |

## 5. Scope confirmation

**No application source files were modified.** All writes this pass were under `docs/architecture/` (3 new docs: findings, validation, tree rewritten; 10 updated: 01–05, 06, 08–10, README). `git status` at close of work shows only `docs/architecture/` changes; no `.cs`, `.xaml`, `.rs`, `.csproj`, manifest, installer, or build-script files were touched.
