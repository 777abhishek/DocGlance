# QuickView — Implementation Plan (2026-10-11)

Phases are dependency-ordered. Each task file in [tasks/](./tasks/) carries full steps, acceptance criteria, and rollback. **Entry rule for every task:** confirm the finding still exists at your HEAD before patching; **exit rule:** acceptance criterion verified with an executed command, recorded in the task file and [verification-matrix.md](./verification-matrix.md).

## Phase A — Baseline & release blockers (prerequisite for everything)

| Task | FIND | Deliverable |
|---|---|---|
| [T-001](./tasks/T-001-remove-dead-tests-and-deps.md) | 031, 014(partial) | Dead tests + Markdig removed; suite green |
| [T-002](./tasks/T-002-solution-ci-readme.md) | 033, 037 | `QuickView.sln`, CI workflow, root README |
| Baseline record | — | Done in this audit (build 0/0, 148/148, cargo clean) — re-verify at Phase G |

## Phase B — Correctness & resource safety (fix order = risk order)

| Task | FIND | Deliverable |
|---|---|---|
| [T-010](./tasks/T-010-native-lifecycle-leaks.md) | 001 | One teardown owner; tab churn memory flat |
| [T-011](./tasks/T-011-dispose-races.md) | 002 | Dispose-under-load test passes ×3 wrappers |
| [T-012](./tasks/T-012-size-guards-office-paths.md) | 023 | Oversize XLSX/PPTX → error card |
| [T-013](./tasks/T-013-small-ffi-correctness.md) | 005, 006, 009 | DPI math, FS_RECTF order + consumer audit, Rust scale clamp |
| [T-014](./tasks/T-014-ffi-buffer-protocol.md) | 007 | Two-call length protocol or 1 MB interim + tests |

**Exit criteria:** all High FINDs verified fixed; soak tests in CI; no new warnings.

## Phase C — Structural simplification (only after B is green)

| Task | FIND | Deliverable |
|---|---|---|
| [T-020](./tasks/T-020-docx-decision.md) | 011 | WordCraft wired (preferred) or removed; dispatch test |
| [T-021](./tasks/T-021-di-decision.md) | 013 | DI wired or deleted; no half-state |
| [T-022](./tasks/T-022-dead-code-sweep.md) | 014(rest), 008, 017 | Dead paths/comments/Tree.md resolved |
| [T-023](./tasks/T-023-viewerpage-split.md) | 012, 015, 016 | PPTX+PDF extraction per Spreadsheet pattern; ViewerPage < 800 lines |

## Phase D — Performance (measure → fix → re-measure)

Scenarios in [performance-audit.md](./performance-audit.md) are the harness. Tasks: T-030 text-format cache (027), T-031 log gating (028), T-032 thumbnail access tracking (030), T-033 PPTX thumbnail parallelism (029). Each requires before/after numbers from the same scenario.

## Phase E — Test coverage & resilience

T-040 FFI wrapper suite (032/034), T-041 dispatch+validator characterization, T-042 update-service verify tests, T-043 malformed-file fixtures (corrupt zip/PDF/JSON-from-Rust).

## Phase F — Documentation & hygiene

Mostly completed by this audit (stale set replaced). Remaining: T-050 final doc reconciliation after C/D/E land; retire superseded docs per decision-log.

## Phase G — Release readiness

T-060 clean Release build (packaged + unpackaged), installer + winget validation, full feature regression matrix recorded, unresolved-risk list, go/no-go note in [AUDIT-COMPLETE.md](./AUDIT-COMPLETE.md).

## Rollback strategy

Every task is a small, individually revertable commit (task file specifies `git revert` scope). No task rewires more than one subsystem. The riskiest change (T-023 viewer split) is last and lands behind unchanged public behavior; if it regresses, revert the extraction commits — Phases B fixes are independent of it.
