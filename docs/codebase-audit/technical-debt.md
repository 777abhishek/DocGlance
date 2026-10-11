# QuickView — Technical Debt Inventory (2026-10-11)

Canonical defect list lives in [findings.md](./findings.md). This file tracks *debt* — cost of change, confusion risk, deferred structural work — with priority for planning.

## P0 — pay now (blocks reliable change)

| Debt | FIND | Why now | Effort |
|---|---|---|---|
| No FFI tests; dead Web tests | 031/032 | Every native fix is unverifiable without them | M |
| Native lifecycle leaks + dispose races | 001/002 | Worsens per format added; tab UX is core | S+M |
| No `.sln`/CI | 033 | Nothing enforces green; regressions ship silently | S |
| XLSX/PPTX size-validation gap | 023 | Trivial fix, real DoS | XS |

## P1 — structural debt (Phase B/C)

| Debt | FIND | Notes | Effort |
|---|---|---|---|
| `ViewerPage.Pdf.cs` coordinates 3 formats; family ~3.4k lines | 012 | Extract per Phase C rules (tests first); Spreadsheet pattern is the template | L |
| DOCX dead integration (wire-or-remove) | 011 | Decision D-04 pending | M/S |
| DI half-wired | 013 | Decision D-03 pending | S |
| Dead render paths (~350+ lines) + unused Markdig + empty dirs | 014 | Pure deletion, verified callers=0 | S |
| Rust git-deps unpinned | 018 | Pin revs; document upgrade path | S |
| FS_RECTF latent swap; thumbnail DPI math; FFI truncation | 005/006/007 | Small, correctness | S |

## P2 — accepted/deferred (with interest rates)

| Debt | FIND | Interest rate | Trigger to repay |
|---|---|---|---|
| ViewModel dual-state sync | 015 | Low — pattern is documented | Phase C viewer split |
| UI in Services/Rendering (`UpdateBanner`, `PptxViewerControl`) | 016 | Low | Next touch of those files |
| Old `PDFiumCore` carrier version | 024 | Rises with PDFium CVEs | Scheduled upgrade or CVE alert |
| Diagnostic log flood | 028 | Medium during dogfooding | Before release build |
| Per-frame `CanvasTextFormat` | 027 | Medium on scroll-heavy sheets | Phase D with measurement |
| Stale Tree.md / in-code phase comments | 017/008 | Low confusion cost | Phase F docs pass |
| Thumbnail LRU on LastAccessTime | 030 | Unknown until measured | Phase D scenario 3 |

## Quick wins (≤ half day total)

Delete `WebSecurityTests` + `Markdig` ref + dead render paths (FIND-014/031) · add size guards (023) · fix thumbnail DPI `/96→/72` (005) · reorder `FS_RECTF` + consumer audit (006) · README + `.sln` (033/037).

## Debt we are deliberately NOT taking on

- Rewriting the render loop for architecture aesthetics (works, measured-design).
- Unifying the three FFI surfaces or introducing a generic document abstraction.
- MVVM-completing ViewerPage without a feature need.
