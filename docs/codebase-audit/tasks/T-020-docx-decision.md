# T-020 — Execute the WordCraft DOCX decision (wire or remove)

- **FIND:** FIND-011 (High) · Decision **D-04** (recommendation: wire) · Prereq: T-010/T-011 landed (lifecycle correctness first) · **In-scope:** `Features/Viewer/Partials/ViewerPage.Pdf.cs`, new `Features/Viewer/` host files, `Rendering/Document/DocxDocument.cs`; removal alternative also touches `native/wordcraft_ffi/` + `native/build-native.ps1`
- **Evidence:** `DocxDocument`/`WordCraftNative` have zero references outside `Rendering/Document/`; `.docx` → `DocxRenderer` info card; commit `371b7de` claims integration.

## Steps — Option A: wire (recommended)
1. Add `else if (ext == ".docx")` branch → `LoadDocxAsync` mirroring `LoadPptxAsync`: `WordCraftNative.IsAvailable` guard → `DocxDocument.OpenAsync` (with T-012 size guard) → page-count/viewport init → render loop using `RenderPageAsync(scale)` into the same vertical-canvas pattern PPTX uses (`GetPageSize`/`PageCount` map cleanly to the slide loop).
2. Thumbnails/sidebar/search reuse the PPTX implementations' shape (`ExtractPageText`, `SearchText` already exist on the wrapper).
3. Dispatch characterization test (T-041 hooks in here): `.docx` → DocxDocument path, not registry.
4. Manual matrix: multi-page DOCX with images/tables/headers; zoom; search.

## Steps — Option B: remove
Delete `Rendering/Document/{DocxDocument,WordCraftNative}.cs`, `native/wordcraft_ffi/`, its block in `build-native.ps1`; ensure `.docx` info card is truthful ("DOCX preview not yet supported"). Log the reversal in D-04.

## Acceptance criteria
Either: real DOCX pages render + tests + matrix recorded; or: no wordcraft artifacts remain and build/test/cargo all green. **No third state** (dead DLL shipping) may survive this task.

## Rollback
Revert commit(s).

## Completion checklist
- [ ] D-04 decision executed · [ ] (A) render+search+matrix OR (B) full removal · [ ] Dispatch test · [ ] FIND-011 → Fixed

## Actual results
_(fill on completion)_
