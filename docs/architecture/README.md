# DocGlance — Architecture Documentation

**This directory is the authoritative source for the target architecture.** It was produced by a full-repository audit on 2026-10-16 (baseline commit `6fa6515`, clean tree) and supersedes structural guidance found elsewhere.

## Reading order

| Doc | Content | Answers |
|---|---|---|
| [01-repository-inventory.md](01-repository-inventory.md) | Complete file inventory, responsibilities, LOC, dependencies, findings F1–F12 | What exists and what does it do? |
| [02-current-architecture.md](02-current-architecture.md) | Workflow traces (startup → open → render → close), actual component & dependency diagrams | How does it work today? |
| [03-target-architecture.md](03-target-architecture.md) | Target design: viewer contracts, capability interfaces, registry selection, lifecycle | Where are we going? |
| [04-naming-conventions.md](04-naming-conventions.md) | Rules per language/folder + rename mapping | How do we name things? |
| [05-proposed-file-tree.md](05-proposed-file-tree.md) | Concise planning view of the tree decisions — defers to `tree.md` | Why this shape? |
| [06-file-size-and-modularity.md](06-file-size-and-modularity.md) | LOC targets per file, split/keep verdicts | How big should files be? |
| [07-shared-components-matrix.md](07-shared-components-matrix.md) | Capability-by-capability shared/format-specific decisions | What is shared vs format-specific? |
| [08-dependency-and-interop-rules.md](08-dependency-and-interop-rules.md) | Layer rules, C#↔Rust boundary contract, prohibited deps | How may modules depend? |
| [09-file-migration-map.md](09-file-migration-map.md) | Every existing file → destination, with confidence | How do we get there file-by-file? |
| [10-refactoring-roadmap.md](10-refactoring-roadmap.md) | 10 incremental phases with validation & rollback | In what order? |
| [tree.md](tree.md) | **The complete, authoritative final repository tree** — every file annotated, split maps, deletion + new-file ledgers, acceptance checks | What does the end state look like exactly? |
| [architecture-review-findings.md](architecture-review-findings.md) | Contradiction register AF-01…AF-15: conflicts found across the doc set and their evidence-based resolutions | What changed in reconciliation, and why? |
| [architecture-validation-report.md](architecture-validation-report.md) | Coverage checks, file-disposition counts, acceptance-check results, unresolved items | Is the tree complete and internally consistent? |

## In one paragraph

DocGlance is a single-project WinUI 3 document viewer (~28.6k LOC C#/XAML + 5 Rust FFI engines) whose engines, services, and shell are fundamentally sound but whose ViewerPage has accreted into a 17-partial, ~8.8k-LOC god class with a dead parallel renderer pipeline. The target keeps all working code and the single-project layout, and dissolves the god class by giving every format its own viewer control behind small capability interfaces (`ISupportsZoom/Search/…`) selected through a `DocumentViewerRegistry`, with FFI bindings isolated in `Interop/`, engines organized per format in `Rendering/{Pdf,Docx,Xlsx,Pptx}/`, and shell concerns (activation, session, hotkey) extracted from `App`/`TitleBarWindow` into services. Implementation proceeds in 10 dependency-ordered phases, each independently shippable.

## Relationship to pre-existing docs

- `docs/reliability/*` — reliability investigation (2026-10-13), **historical**, uses the old "QuickView" name and describes `QuickView.Tests`; its defect findings (e.g. D1 native gate) are addressed by current code and its layering observations by this set. Do not use it for structure decisions.
- `docs/offline-translation.md`, `docs/android-kotlin-integration.md` — feature notes; translation doc's `qv_` prefix references are updated in roadmap Phase 8.
- `.agents/` is empty; the `.agents/tasks/*` and `.kiro/*` files referenced in older plans do not exist in this repository.

## Status

**Design reconciled and internally consistent; awaiting implementation approval — no application code has been modified.** `tree.md` is the single authoritative final tree; `10-refactoring-roadmap.md` moves the repository to exactly that tree in 8 phases (Rust untouched per decision AF-09). Decisions that still need your sign-off: the Phase 7 REVIEW-gated deletions (8 dead files — evidence in `tree.md` §3) and the `Services/Translation → Interop/Translation` permanent exception (AF-05). The proposed structure still requires build and runtime validation **during** migration — no implementation has been performed.
