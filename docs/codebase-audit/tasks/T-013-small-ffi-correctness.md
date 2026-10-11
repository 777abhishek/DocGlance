# T-013 — Small FFI correctness fixes (DPI math, FS_RECTF, Rust scale clamp)

- **FINDs:** FIND-005, FIND-006, FIND-009 · Prereq: none · **In-scope:** `Rendering/Pdf/PdfRenderer.cs`, `Rendering/Pdf/Pdfium.cs`, `Rendering/Pdf/PdfLinkService.cs`, `Rendering/Pdf/PdfAnnotationService.cs`, `native/wordcraft_ffi/src/lib.rs` (+ deckcraft/gridcraft only if the same clamp is missing — check `dc_render_slide`/pdf export scale)
- **Evidence:** (a) `PdfRenderer.cs:245-246` `pt * dpi / 96` should be `/ 72` (thumbnails 75 % res); (b) `Pdfium.cs:261-268` `FS_RECTF` fields `Left,Bottom,Right,Top` vs native `left,top,right,bottom`; (c) `wordcraft lib.rs:157` `wc_render_page` scale unbounded.

## Steps
1. **DPI:** change to `/ 72.0`; add unit test asserting `renderW == round(612 * 96 / 72) == 816` for letter @96 (test the formula via a small internal helper or pin via thumbnail-size observable).
2. **FS_RECTF:** reorder to `Left, Top, Right, Bottom` (fixing the doc comment too). Audit consumers: `PdfAnnotationService.cs:82,155` (sets min/max — after reorder, `Top` must receive max Y: swap the values assigned), `PdfLinkService.cs:133-135` (uses corners — verify hit-test still correct). Run the link/annot manual matrix: PDF with links + highlight annotation; confirm click-targets and rendered rects.
3. **Rust clamp:** in `wc_render_page`: `let scale = scale.clamp(0.1, 8.0);` (and same in `dc_render_slide` if unbounded). `cargo check` ×3.
4. Note: FIND-006's *visible* impact is uncertain — if the manual matrix shows annotations/links were already pixel-correct, record that the swap was latent and the fix is prophylactic; if they were mirrored, the fix corrects them (evidence for findings.md).

## Verification
Build 0/0 · tests green · cargo check clean · link/annot matrix executed and recorded.

## Acceptance criteria
Struct matches `fpdf_view.h`; consumers audited with in-code note; scale clamp proven by a quick `scale=1e9` call returning null (manual or test).

## Rollback
Revert; FS_RECTF consumer swaps are the sensitive part — keep them in the same commit as the reorder.

## Completion checklist
- [ ] DPI fix + test · [ ] FS_RECTF reorder + consumer audit + matrix · [ ] Rust clamps + cargo check · [ ] FIND-005/006/009 → Fixed

## Actual results
_(fill on completion)_
