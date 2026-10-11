# T-022 — Dead-code and stale-comment sweep

- **FINDs:** FIND-014 (rest), FIND-008, FIND-017 · Prereq: T-001 (Markdig already gone) · **In-scope:** `Rendering/Pdf/PdfRenderer.cs`, `Features/Viewer/Partials/ViewerPage.Viewport.cs`, `Services/Documents/{IPdfViewerService,PdfViewerService}.cs`, `Rendering/Pdf/*` stale comments, `Tree.md`, empty dirs
- **Evidence (all verified zero callers @40cdfcd):** `PdfRenderer.RenderViewportAsync` (372-494) · `ViewerPage.Viewport.RenderFullPageProgressive` (610-737) · `PdfViewerService.RenderPageProgressiveAsync` + `_renderedPages` + interface member · misleading "Phase 1 skeleton / NotImplementedException" docs (`PdfViewerService.cs:10-13`) · the concurrency-claim comment above `RenderFullPage` (`Viewport.cs:526-534`) contradicting the Gate lock below it · `Tree.md` stale · empty `Rendering/Data/`, `Common/Extensions/`.

## Steps
1. Remove the three dead method groups + interface member (decision D-06).
2. `PdfViewerService` class doc → describe actual behavior; ViewerPage "Phase 2" comments → plain descriptions.
3. Fix the wrong PDFium-thread-safety comment in `Viewport.cs`.
4. Regenerate `Tree.md` from the actual tree (`tree /F` output trimmed to source) **or** replace its body with a pointer to `docs/codebase-audit/codebase-overview.md` (pick one; log in the file).
5. Delete empty dirs.
6. After each removal group: build + full tests + `grep` for the removed symbol.

## Verification
`dotnet build` 0/0 · suite green · greps empty · `wc -l` shows ~500 lines removed net.

## Acceptance criteria
No unreachable render paths; comments match behavior; Tree.md truthful; nothing else changed.

## Rollback
Revert per-group commits.

## Completion checklist
- [ ] Dead paths removed · [ ] Comments fixed · [ ] Tree.md resolved · [ ] Empty dirs gone · [ ] FIND-008/014/017 → Fixed

## Actual results
_(fill on completion)_
