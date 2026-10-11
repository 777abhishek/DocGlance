# QuickView — Implementation Checklist (master)

> Tick only with executed verification recorded in the task file + [verification-matrix.md](./verification-matrix.md).
> Re-verify baseline before Phase G: `dotnet build` 0/0 · `dotnet test` all green · `cargo check` ×3 clean.

## Phase A — Baseline & blockers
- [ ] **T-001** Remove dead tests & unused Markdig (FIND-031, 014) — acceptance: suite green minus 15 tests, grep-clean
- [ ] **T-002** Add `.sln` + CI + README (FIND-033, 037) — acceptance: green CI run on a branch
- [x] Baseline recorded 2026-10-11 (`40cdfcd`): build 0/0 · 148/148 · cargo clean

## Phase B — Correctness & resource safety
- [ ] **T-010** Native lifecycle leaks (FIND-001) — acceptance: 20-tab churn returns to baseline memory; counter = 0
- [ ] **T-011** Dispose races ×3 wrappers (FIND-002) — acceptance: concurrent dispose/render test ×100 no-fault
- [ ] **T-012** Size guards on XLSX/PPTX (FIND-023) — acceptance: oversize fixture → error card + unit test
- [ ] **T-013** DPI math + FS_RECTF + scale clamp (FIND-005/006/009) — acceptance: unit tests + link/annot visual matrix unchanged-or-fixed
- [ ] **T-014** FFI buffer protocol (FIND-007) — acceptance: >20k-match fixture returns exact capped count
- [ ] **FIND-003** unhandled-exception policy decided & applied (XS)

## Phase C — Structure
- [ ] **T-020** DOCX decision executed (FIND-011) — wired-with-tests or fully removed
- [ ] **T-021** DI decision executed (FIND-013) — no half-wired container remains
- [ ] **T-022** Dead-code sweep (FIND-014/008/017) — dead paths, stale comments, Tree.md
- [ ] **T-023** ViewerPage split (FIND-012/015/016) — per-format controllers; ViewerPage < 800 lines; behavior identical

## Phase D — Performance (each task = before/after numbers)
- [ ] **T-030** CanvasTextFormat cache (FIND-027)
- [ ] **T-031** Render-path log gating (FIND-028)
- [ ] **T-032** Thumbnail access-time solution (FIND-030)
- [ ] **T-033** PPTX thumbnail parallelism (FIND-029)

## Phase E — Tests
- [ ] **T-040** FFI wrapper suite + soak (FIND-032/034) — ≥25 new facts
- [ ] **T-041** Dispatch + validator characterization
- [ ] **T-042** UpdateService verification tests
- [ ] **T-043** Malformed-input fixtures (zip/PDF/JSON)

## Phase F — Docs
- [x] Audit doc set rebuilt (this audit, 2026-10-11)
- [ ] **T-050** Post-remediation reconciliation (links, statuses, Tree.md or its removal)

## Phase G — Release
- [ ] **T-060** Release gate: clean builds (packaged+unpackaged), installer/winget validation, regression matrix, risk list, go/no-go

## Cross-task rules
1. One subsystem per commit; task-file updates in the same commit.
2. Never mark complete on "code edited" — only on verified acceptance.
3. Findings whose fix is intentionally skipped get `Accepted`/`Deferred` in findings.md + a decision-log entry.
