# QuickView — Agent Workflow (multi-agent / single-agent execution rules)

Applies to any coding agent (human or AI) executing tasks from [implementation-plan.md](./implementation-plan.md). Read [findings.md](./findings.md) for the defect being fixed before touching code.

## Roles

| Role | May edit | May not |
|---|---|---|
| **Auditor** | docs/codebase-audit/* only | any application/native source (read-only audit) |
| **Implementer** | files listed in its task's *In-scope* section | anything else; never another task's files |
| **Reviewer** | nothing | inspects diffs of substantial changes, checks acceptance evidence |

## Assignment & ownership rules

1. One task per agent at a time; task files are the contract. Pick the lowest-numbered open task whose prerequisites are ticked in [IMPLEMENTATION-CHECKLIST.md](./IMPLEMENTATION-CHECKLIST.md).
2. **File ownership is exclusive while a task is open.** Overlaps by design: T-010/T-011 both touch the three native wrappers — sequence them (T-010 first), never parallel.
3. Parallel-safe groupings: (T-001, T-002), (T-012, T-013), (T-030, T-031, T-033). Everything involving `ViewerPage.*` partials is serial (T-023 last).
4. No agent deletes code another task's *In-scope* list names.

## Per-task protocol (mandatory)

1. Read task file + linked findings. `git status` — if the tree is dirty with changes you didn't make, stop and report.
2. **Re-confirm the finding exists at your HEAD** (evidence lines move); if fixed/absent, mark and close the task instead of patching.
3. Smallest safe patch. No drive-by refactors outside scope; unrelated changes belong in the diff review finding, not the diff.
4. Add/extend the tests the task requires *before or with* the fix.
5. `dotnet build` (0 warnings gate) → `dotnet test` → task-specific commands (e.g. `cargo check` for Rust tasks).
6. Update: task file (*Actual results*, checklist), IMPLEMENTATION-CHECKLIST tick, findings.md status (`Fixed` + how verified), verification-matrix row.
7. Commit message references the task and FIND ids, e.g. `fix(lifecycle): release native docs on tab close (T-010, FIND-001)`.

## Verification honesty

- A check that was not executed is recorded **Not run** — never inferred.
- Failed verification → investigate; either correct the patch or record a blocker in the task file. Never weaken a test to pass (audit rule 9).
- Benchmarks (Phase D) require before/after from the same scenario on the same machine.

## Handoffs & conflicts

- Completing agent posts: task id, files touched, commands run with exit status, deviations from the task file.
- Conflicting recommendations between agents → orchestrator decides and appends to [decision-log.md](./decision-log.md); losers' rationale is recorded there too.
- If a task uncovers a new defect: add a FIND-0NN entry (evidence required) — don't silently expand scope.
