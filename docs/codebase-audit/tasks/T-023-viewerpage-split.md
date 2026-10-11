# T-023 — Split ViewerPage format logic into per-format controllers

- **FINDs:** FIND-012 (primary), 015, 016 · Prereq: **T-010, T-011, T-020, T-021 + Phase E characterization tests (T-041)** — do not start earlier · **In-scope:** `Features/Viewer/**` (partials, Components), `Services/Updates/UpdateBanner.xaml*`, `Rendering/Presentation/PptxViewerControl.xaml*`
- **Evidence:** ViewerPage partial family ~3.4k lines; `ViewerPage.Pdf.cs` (826) dispatches 3 formats and hosts PPTX viewport code (519-690); XLSX already extracted (`SpreadsheetViewer`) proving the pattern.

## Steps (order matters; land as separate commits)
1. **PPTX first:** move slide-viewport render/thumbnail logic out of `ViewerPage.Pdf.cs` into `Features/Viewer/Presentation/PptxViewport.cs` controller following the Spreadsheet shape (wrapper + host control + controller; ViewerPage keeps dispatch + chrome).
2. **Rename dispatch:** `ViewerPage.Pdf.cs` → `ViewerPage.Dispatch.cs` after step 1 makes it honest (PDF load + dispatch only).
3. **PDF extraction (optional, largest):** only if tests pin current behavior; move render orchestration to `PdfViewportController` keeping `ViewerPage.Viewport.cs` as the thin UI adapter.
4. Move `UpdateBanner` → `UI/Controls/` (FIND-016) in its own commit.
5. After each step: build, full tests, manual smoke of affected format.

## Guardrails
- Zero behavior change (rule 6): same render order, same DPI math, same cache keys. Diff review checks for logic moves, not rewrites.
- Public API used by `TitleBarWindow` (`LoadDocument`, `CancelLoad`, `CurrentPage`, …) unchanged.
- Stop condition: if a step balloons, land what's clean and re-plan the rest — don't force it.

## Acceptance criteria
`ViewerPage*` family < 800 lines per file and < 2.5k total; each format's load/render/dispose in one place; suite + matrix green; `git diff --stat` per commit reviewable in one sitting.

## Rollback
Per-commit revert; extraction commits are moves, so conflicts are mechanical.

## Completion checklist
- [ ] PPTX extracted · [ ] Dispatch renamed · [ ] (opt) PDF extracted · [ ] UpdateBanner moved · [ ] Size targets met · [ ] FIND-012/016 → Fixed, FIND-015 closed

## Actual results
_(fill on completion)_
