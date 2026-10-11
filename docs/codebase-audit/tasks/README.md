# Task files — conventions

- One independently executable task per file, named `T-NNN-slug.md` matching the plan/checklist ids.
- Every task carries: FIND ids, evidence, in-scope/out-of-scope, steps, verification commands, acceptance criteria, rollback, completion checklist, and an *Actual results* section to fill on completion.
- Phase D/E/F/G task files (T-030…T-060) are created **when their phase starts** — their shape depends on Phase B/C outcomes; their intended scope is defined in [../implementation-plan.md](../implementation-plan.md). Do not pre-write them speculatively.
- House rules live in [../agent-workflow.md](../agent-workflow.md); decisions in [../decision-log.md](../decision-log.md).

## Current tasks

| Task | Phase | FIND | Status |
|---|---|---|---|
| [T-001](./T-001-remove-dead-tests-and-deps.md) | A | 031, 014 | open |
| [T-002](./T-002-solution-ci-readme.md) | A | 033, 037 | open |
| [T-010](./T-010-native-lifecycle-leaks.md) | B | 001 | open |
| [T-011](./T-011-dispose-races.md) | B | 002 | open (after T-010) |
| [T-012](./T-012-size-guards-office-paths.md) | B | 023 | open |
| [T-013](./T-013-small-ffi-correctness.md) | B | 005/006/009 | open |
| [T-014](./T-014-ffi-buffer-protocol.md) | B | 007 | open |
| [T-020](./T-020-docx-decision.md) | C | 011 | open (needs D-04) |
| [T-021](./T-021-di-decision.md) | C | 013 | open (needs D-03) |
| [T-022](./T-022-dead-code-sweep.md) | C | 008/014/017 | open (after T-001) |
| [T-023](./T-023-viewerpage-split.md) | C | 012/015/016 | open (last) |
