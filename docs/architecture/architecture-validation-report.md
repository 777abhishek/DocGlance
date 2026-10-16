# Architecture Validation Report — Re-audit (2026-10-16)

Scope of validation: documentation-only re-audit of the repository at commit `10db6a8` (clean working tree). **No application code was modified and no build/test run was performed**; the proposed remaining structure requires build and runtime validation during migration (roadmap global validation per phase).

## 1. Counts

### Audited
| Category | Files | LOC |
|---|---|---|
| App source (.cs + .xaml) | 150 | 28,766 |
| Test source | 17 | 2,146 |
| Build/dev tools | 4 source + 2 csproj | — |
| Rust crates | 5 (lib.rs + manifests + lockfiles) | 4,984 |
| Documentation (this set) | 14 | — |

### Dispositions (150 app files, one disposition each)
| Action | Count |
|---|---|
| FINAL (already at final path) | 125 |
| EDIT (in-place scope work pending) | 4 |
| SPLIT (survives; parts move out) | 5 |
| DISSOLVE (removed; responsibilities mapped) | 5 |
| MERGE | 1 |
| MOVE | 2 |
| DELETE | 8 |
| **Sum** | **150** |

NEW files proposed: 6 app (`Formats/Pdf` ×4, `Formats/Docx` ×2) + 1 test (`Architecture/NamespaceDependencyTests.cs`).

Final tree: **142 app files** (150 − 8 deleted − 6 dissolved/merged + 6 new) + **18 test files**.

Category overlaps, per counting rules: none — every file carries exactly one action; EDIT/SPLIT files are counted at their current path (which is their final path).

## 2. Acceptance checks (from the audit brief)

| # | Check | Result |
|---|---|---|
| 1 | Every maintained current file has exactly one final disposition | ✔ `09` enumerates all 150 + 17 |
| 2 | Every retained/moved file appears at its final destination | ✔ `tree.md` §1 |
| 3 | Every new file has a justified responsibility | ✔ `tree.md` §4 |
| 4 | Every deletion has evidence + explicit reason | ✔ `tree.md` §3 (grep-verified zero consumers, 2026-10-16) |
| 5 | No file listed twice under different final paths | ✔ checked (UpdateBanner, bindings, models, SpreadsheetViewer) |
| 6 | Every XAML file has code-behind or documented reason | ✔ all paired; code-only by design: `FallbackViewer.cs`, `DocumentInfoDialog.cs`, `TranslationDialog.cs`, `TranslationModelsDialog.cs`; resource dictionaries none by definition |
| 7 | Project/resource/test/native-build relationships valid | ✔ single app csproj + test csproj; SDK globbing covers all moves; native Content items unchanged; only one XAML URI update in scope (`UpdateBanner` in `TitleBarWindow.xaml`) |
| 8 | Tree agrees with naming/dependency/matrix/map/roadmap docs | ✔ reconciled in this pass; deviations logged as RA-02/03/07/15 |
| 9 | No placeholders hide files | ✔ Assets enumerated; `vendor/` labeled third-party; installer folders labeled |
| 10 | Feasible within existing project structure | ✔ folder moves + in-place edits only; no new projects; Rust untouched |

## 3. Consistency cross-checks

- tree ↔ 09 migration map: dispositions identical (125/4/5/5/1/2/8 + 7 NEW) ✔
- tree ↔ 10 roadmap: every phase "Completes" line names only paths present in tree §1/§3/§4 ✔
- tree ↔ 04 naming: `Formats/` rule, `ViewerPage.*` partial naming, Interop folder naming agree ✔
- tree ↔ 08 dependency rules: Interop consumer list, Rendering boundary, Services/XAML rule agree ✔
- tree ↔ 07 matrix: capability implementations match grep evidence (Text: Search+TextSelection; Spreadsheet: Zoom+Search+TextSelection; Pdf/Docx/Pptx per phase plan) ✔
- tree ↔ 06 modularity: LOC targets use measured current values ✔
- README links ↔ files on disk: all 14 exist ✔
- Deletion ledger ↔ dispositions: 8 rows, each file's only mention is its DELETE row and ledger ✔
- FFI contracts ↔ native exports: pc_×27 / wc_×14 / gc_×11 / dc_×12 / qv_×5 verified by grep; `Interop/README.md` ownership table matches ✔
- Viewer contracts ↔ lifecycle/state model: `IDocumentViewer.CleanupAsync` is the single cleanup seam; `ViewerPage.CancelLoad` already calls it per viewer ✔

## 4. Unresolved items

| Item | Status | Evidence needed |
|---|---|---|
| Build/test greenness at `10db6a8` | Assumed from commit validation; not re-run here | `build.ps1` + `dotnet test DocGlance.Tests` run |
| Runtime behavior of dormant `PptxViewer` | Unverified (never constructed) | Phase 2 manual PPTX matrix |
| Exact final LOC of `ViewerPage.xaml.cs` / viewers | Estimates (tree §2.1) | Measured at phase completion |
| Restoration of `docs/offline-translation.md` | User decision | User request |

## 5. Limitations

- No implementation was performed; per the brief, nothing here claims the architecture compiles or passes tests — it records a verified inventory and a reconciled target.
- `Assets/` and `installer/` contents are inventoried at directory level (binary sets enumerated by family: logo ladders, 12 FileTypeIcon pairs, fonts); individual scale-variant files were not listed one-by-one (stable, manifest-referenced, out of refactoring scope).
- Vendored Rust code under `native/pdfcraft_ffi/vendor/` is labeled third-party and was not audited (out of scope by decision AF-09).
