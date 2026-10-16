# DocGlance Architecture Documentation

Re-audit set, 2026-10-16 — reconciled against the repository at commit `10db6a8` (Phases 1–4 + half of Phase 5 of the original plan already implemented). Supersedes the pre-implementation audit set deleted in the same commit.

## Reading order

1. [`01-repository-inventory.md`](01-repository-inventory.md) — verified inventory: 150 app files / 28,766 LOC, 17 test files, 5 Rust crates (intact).
2. [`02-current-architecture.md`](02-current-architecture.md) — what is built today, including the hybrid viewer dispatch.
3. [`03-target-architecture.md`](03-target-architecture.md) — ownership boundaries, capability contracts, lifecycle.
4. [`04-naming-conventions.md`](04-naming-conventions.md) — naming rules (adopts the implemented `Formats/` layout).
5. [`05-proposed-file-tree.md`](05-proposed-file-tree.md) — planning view; defers to tree.md.
6. [`06-file-size-and-modularity.md`](06-file-size-and-modularity.md) — measured sizes and per-file modularity decisions.
7. [`07-shared-components-matrix.md`](07-shared-components-matrix.md) — shared vs. format-specific ownership.
8. [`08-dependency-and-interop-rules.md`](08-dependency-and-interop-rules.md) — layer table + native-interop contract.
9. [`09-file-migration-map.md`](09-file-migration-map.md) — every current file → final disposition.
10. [`10-refactoring-roadmap.md`](10-refactoring-roadmap.md) — remaining phases 1–5, dependency-ordered.
11. [`tree.md`](tree.md) — **the authoritative final repository tree**; wins on any structural disagreement.
12. [`architecture-review-findings.md`](architecture-review-findings.md) — findings register RA-01…RA-12 + carried AF decisions.
13. [`architecture-validation-report.md`](architecture-validation-report.md) — counts, acceptance checks, unresolved items.

## Status

Approved design, **partially implemented** (see 02 §5 and the roadmap). Documentation only — no build or runtime validation was performed by this re-audit; each roadmap phase carries its own validation gates.
