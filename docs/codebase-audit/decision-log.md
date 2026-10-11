# QuickView — Decision Log

Decisions are append-only. Each entry: context → options → decision → consequence.

---

**D-01 · 2026-10-11 · Replace the 2026-10-01 audit set instead of patching it**
- Context: all 11 existing docs predate GridCraft/WordCraft integration, the test project, WebView2 removal, and DI arrival; nearly every quantitative claim was stale (0 tests, no DI, WebView2 XSS, 1500-line ViewerPage).
- Options: incremental edit per file vs. rewrite the set against verified evidence.
- Decision: **Rewrite.** Superseded files `FINDINGS-SUMMARY.md` (→ findings.md) and `refactoring-roadmap.md` (→ implementation-plan.md) deleted after content migration; the other nine filenames were reused with new content.
- Consequence: git history preserves the old audit; links from old commits may 404 inside docs/ — accepted.

**D-02 · 2026-10-11 · Task files live in `docs/codebase-audit/tasks/`, not `.agents/tasks/`**
- Context: the repo owner deleted `.agents/` and `.kiro/` in commit `371b7de` and added `.agents` to `.gitignore` — clearly retiring that convention. The audit spec's default home for task files would be untracked (invisible in version control, violating the "version-controlled Markdown" rule).
- Options: un-ignore `.agents` (overrides owner intent) vs. relocate.
- Decision: **Relocate** to `docs/codebase-audit/tasks/`. No `.agents/skills/` or `.kiro/` files created; shared agent guidance lives in [agent-workflow.md](./agent-workflow.md).
- Consequence: if the owner wants `.agents` back, move the folder and update three links.

**D-03 · pending · DI container: wire or delete (FIND-013)**
- Context: `ServiceContainer` exists, registers `IPdfViewerService` as transient (matches per-tab design), but `Configure()` is never called; pages hand-`new` services.
- Options: (a) wire in `App` ctor + resolve per tab — gives one construction seam, eases FIND-001 ownership fix; (b) delete the file — honest static-registy status quo.
- Recommendation: **(a) wire**. Decision owner: repo owner. Task T-021.

**D-04 · pending · WordCraft DOCX: wire or remove (FIND-011)**
- Context: commit `371b7de` message says "integrate … WordCraft DOCX renderer" but no dispatch/UI wiring exists; `.docx` shows info card; `wordcraft_ffi.dll` is built and shipped.
- Options: (a) wire via PPTX-viewport pattern (`LoadDocxAsync` + page host) — delivers the intended feature, M effort; (b) remove wrapper + crate from build until scheduled — S effort, shrinks attack/maintenance surface.
- Recommendation: **(a) wire** (matches owner's evident intent; renderer is complete and tested upstream). If wiring slips past Phase C, execute (b) rather than shipping dead DLLs. Task T-020.

**D-05 · 2026-10-11 · Keep `PDFiumCore` despite zero API usage (FIND-024 context)**
- Context: package's only role is carrying `pdfium.dll` into the output; hand-written `Pdfium.cs` does all interop.
- Options: keep as carrier vs. switch to raw pdfium binaries + copy task.
- Decision: **keep for now**, upgrade the package version on the next maintenance pass (T-013/T-022 window); re-evaluate if PDFiumCore's asset layout ever blocks a needed version. Not worth a bespoke DLL-fetching task today.

**D-06 · 2026-10-11 · Progressive rendering stays dead**
- Context: `RenderFullPageProgressive` + service progressive API are unreachable (disabled deliberately in `68a3592` after blank-square bugs); ~350 lines dead.
- Decision: **remove in T-022** rather than keep "for later" — git history preserves them; the current render pipeline has different invariants (DPI caps, Gate discipline). Re-introduction would be a fresh design, not a resurrection.
