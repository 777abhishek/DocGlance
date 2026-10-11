# QuickView — Performance & Memory Audit (2026-10-11)

**Method:** static analysis only — no profiler was run. Claims below distinguish *verified structure* from *hypothesis needing measurement*. FIND ids refer to [findings.md](./findings.md).

## Verified healthy (do not churn these)

- **PDF viewport pipeline** (`ViewerPage.Viewport.cs`): renders only visible ± 2 pages; evicts beyond; DPI capped at 192 and pixel dimension at 2600 (≈25 MB worst-case buffer); `ArrayPool<byte>.Shared` for all raster buffers; O(1) `_pageTopCache` layout lookups; UI-thread-only canvas mutations. Effective per-page memory at 100 %/150 % zoom ≈ 3.5/8 MB — matches the design comments.
- **`ThumbnailCache`:** disk-backed, 500 MB ceiling with LRU-ish eviction to 80 %, debounced, background.
- **`TextFileReader`:** capped reads, no whole-file slurp.
- **`Logger`:** enqueue-only hot path, bounded queue (drops under pressure), background writer.
- **Spreadsheet grid:** viewport-culled Win2D drawing — only visible rows/cols drawn per frame.

## Confirmed issues

| ID | Severity | Issue | Evidence |
|---|---|---|---|
| FIND-001 | High | Native-document leak per tab close / format switch (FPDF doc + pinned arrays + Rust `Box` handles) — the dominant unbounded growth vector | `ViewerPage.xaml.cs:101-128` et al. |
| FIND-027 | Medium | `CanvasTextFormat` allocated+disposed per visible cell per frame in the grid (~10³ native objects/frame while scrolling) | `SpreadsheetViewer.xaml.cs` text loop |
| FIND-028 | Low | ~15 `Logger.Log` calls per page render + per scroll event (diag logging from `5a20bb6` never trimmed) — string churn + 5 MB log rotation pressure | `PdfRenderer.cs`, `Viewport.cs` |
| FIND-029 | Low | PPTX sidebar builds up to 50 thumbnails sequentially at open (0.3 scale) — slow open on big decks | `ViewerPage.Pdf.cs:749-757` |
| FIND-030 | Low/inv. | LRU eviction keyed on `File.SetLastAccessTime` — frequently disabled by Windows on non-system volumes → degrades toward FIFO-by-creation | `ThumbnailCache.cs:51,92,148` |

## Hypotheses requiring measurement (Phase D entry criteria)

| H | Hypothesis | How to measure |
|---|---|---|
| H1 | Working-set growth in a 30-min scroll session is dominated by FIND-001 leaks, not managed GC lag | ETW/`PerfView` GC + native heap counters; compare before/after FIND-001 fix |
| H2 | `SpreadsheetViewer` first-load latency scales with used-range size (Rust caps at 2000×500) — JSON string up to ~10⁵ cells | Synthesize 2000×500 sheet; time `GetSheetDataJsonAsync` + `FromJson` + `BuildCellMap` |
| H3 | Search buffer truncation (FIND-007) is observable as "missing results" on big DOCX | Fixture with >20k matches (blocked on FIND-011 wiring decision) |
| H4 | Startup cost of opening N tabs grows linearly with live FPDF documents (leak) | Open/close 50 tabs; chart `WorkingSet` + `PrivateBytes` per tab |

## Benchmark scenarios to institutionalize (Phase D)

1. **PDF scroll:** 500-page synthetic PDF; scripted scroll; record frame time p95, working set, GC alloc rate.
2. **Tab churn:** open/close 20 tabs each of PDF/XLSX/PPTX; native+managed memory must return to baseline ±10 % after each close (this is FIND-001's acceptance test).
3. **Large XLSX:** 2000×500 populated sheet; time-to-first-paint + scroll p95.
4. **Cold start:** process start → Home interactive, packaged and unpackaged.

**Rule:** no optimization lands without a before/after number from the same scenario (audit rule 5/10).
