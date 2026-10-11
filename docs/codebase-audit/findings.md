# QuickView — Canonical Findings Register

> **Audit date:** 2026-10-11 · **HEAD audited:** `40cdfcd` (working tree clean)
> **Method:** Read-only static audit of actual source; build, `cargo check`, and `dotnet test` executed and inspected.
> **Status:** This is the single deduplicated register. Specialist summaries that existed before this audit were consolidated here; `FINDINGS-SUMMARY.md` and `refactoring-roadmap.md` were removed as superseded (see [decision-log.md](./decision-log.md)).

**Verified baseline (2026-10-11):** `dotnet build` → 0 errors / 0 warnings · `dotnet test` → **133 passed, 0 failed** (1 s) · `cargo check` → all three crates clean. **Update (T-001):** 15 dead tests removed.

---

## Severity definitions

| Severity | Meaning |
|---|---|
| Critical | Demonstrable severe security, data-integrity, or stability risk |
| High | Major functional failure, significant resource leak, or release-blocking defect |
| Medium | Meaningful correctness, reliability, performance, or maintainability problem |
| Low | Localized cleanup or low-impact improvement |
| Informational | Observation without an established defect |

---

## High severity

### FIND-001 — Native documents leak on tab close and format switch · **Confirmed defect**
- **Evidence:** `Features/Viewer/ViewerPage.xaml.cs:101-128` (`CancelLoad` nulls `_pdfState`, never calls `_pdfService.CloseDocument()`; never disposes `_spreadsheetDocument` / `_pptxDocument`); `Features/Viewer/Partials/ViewerPage.Pdf.cs:783` (`_pdfState = null` on XLSX load path); `Features/Viewer/Spreadsheet/SpreadsheetViewer.xaml.cs` `Cleanup()` nulls `_doc` without disposing.
- **Observed:** `_pdfService.CloseDocument()` has **no caller outside `PdfViewerService` itself** (grep across `Features/`). Native docs are freed only when the *same* format is re-loaded in the *same* tab (`ViewerPage.Pdf.cs:442,792`). `TitleBarWindow.CloseTab` (`UI/Shell/TitleBarWindow.xaml.cs:256-275`) calls `vp.CancelLoad()` — which frees nothing native.
- **Impact:** Every closed tab leaks one FPDF document (+ its pinned byte[] when bytes-loaded), one `Box<GcDocument>`, or one `Box<DcDocument>`. A session opening/closing 50 PDF tabs retains 50 PDFium documents indefinitely (native heap + possibly pinned managed arrays). Switching a tab from PDF→XLSX leaks the PDF document in the same way.
- **Root cause:** "Service owns the document lifecycle" comment (`ViewerPage.xaml.cs:125`) was never paired with an actual call on teardown; teardown paths grew per-format without a single owner.
- **Minimal fix:** In `CancelLoad()` (and a new per-format guard in `LoadDocumentAsync` before dispatch): call `_pdfService.CloseDocument()`, `_spreadsheetDocument?.Dispose()`, `_pptxDocument?.Dispose()`, null all three.
- **Acceptance:** Test: open/close 20 tabs of one PDF; assert PDFium document count returns to 0 (observable via a `PdfViewerService` counter or working-set plateau). No behavior change otherwise; existing 148 tests stay green.
- **Effort:** S · **Status:** Confirmed

### FIND-002 — Dispose race in all three native document wrappers · **Confirmed defect**
- **Evidence:** `Rendering/Document/SpreadsheetDocument.cs:109-140,236-248`; `Rendering/Document/DocxDocument.cs:90-119,194-206`; `Rendering/Presentation/PptxDocument.cs:94-118,143-154`.
- **Observed:** `Dispose()` frees the native handle and then `_renderLock.Dispose()` / `_exportLock.Dispose()` **without acquiring the semaphore**. `RenderPageAsync`/`ExportToPdfAsync` check `_disposed` *before* awaiting the semaphore, then run `Task.Run(...)` callbacks that use `_handle` — a `Dispose` in that window frees the handle mid-call (native use-after-free), and the `finally { _renderLock.Release() }` throws `ObjectDisposedException` after the semaphore is disposed.
- **Impact:** Crash or heap corruption when a tab closes while a render/export is in flight (large XLSX export, slow PPTX slide render on busy thread pool). Probability grows with document size — exactly when renders are slow.
- **Root cause:** Guard pattern (`ObjectDisposedException.ThrowIf` at entry) protects entry, not the async body; semaphore not shared with Dispose.
- **Minimal fix:** In each wrapper: `Dispose()` acquires the semaphore (`Wait()` — renders are bounded), sets `_disposed = true`, frees handle, and does **not** dispose the semaphore (or disposes it only via a finalizer-safe path); entry checks move inside the semaphore.
- **Acceptance:** New unit test: start `RenderSlideAsync` on a large fixture, `Dispose()` concurrently 100×, no exceptions/crash (needs native DLLs present — same pattern as `PdfViewerServiceTests`). Existing tests green.
- **Effort:** S · **Status:** Confirmed

### FIND-011 — WordCraft DOCX renderer is dead code; `.docx` still renders an info card · **Confirmed defect (feature gap / misleading integration)**
- **Evidence:** `grep -rn "DocxDocument\|WordCraftNative" Features/ UI/ Services/` → zero references. Dispatch chain `Features/Viewer/Partials/ViewerPage.Pdf.cs:67-90` handles `.pdf/.pptx/.xlsx/code/text` then falls to `RendererRegistry`, where `Rendering/Office/DocxRenderer.cs` returns a `ViewerInfoCard`. Commit `371b7de` ("integrate … WordCraft DOCX renderer") built `wordcraft_ffi.dll` and the wrapper but never wired the dispatch. `native/build-native.ps1` still builds/copies the DLL.
- **Impact:** Advertised capability absent; dead wrapper + DLL (~size) shipped; build time wasted; misleading commit/docs.
- **Root cause:** Integration stopped one layer short (no `LoadDocxAsync` branch, no viewport host).
- **Minimal fix (recommended):** Wire DOCX through the existing PPTX viewport pattern (a `LoadDocxAsync` branch + page/slide host reusing `PptxDocument`-style rendering). Alternative: delete `Rendering/Document/{DocxDocument,WordCraftNative}.cs` + crate from build until needed. Decide in [decision-log.md](./decision-log.md) before Phase C.
- **Acceptance:** Opening a `.docx` shows rendered pages (or the crate is fully removed and `.docx` shows a truthful info card); test covering dispatch added.
- **Effort:** M (wire) / S (remove) · **Status:** Confirmed — decision required

### FIND-031 — 15 tests assert a copy of a regex from a deleted class · **FIXED (2026-10-11)**
- **Evidence:** `QuickView.Tests/Rendering/Web/WebSecurityTests.cs:1-15` re-implements `SanitizeUrls` "since the method is private" — but `MarkdownRenderer` no longer exists anywhere in the tree (WebView2 rendering fully removed; only stale mentions remain in `App.xaml.cs:142` comment, `IFileRenderer.cs` doc comment).
- **Impact:** 15 green tests provide false confidence about sanitization that no longer ships; they test the test file's own copy.
- **Resolution (T-001, commit 538fcb9):** Deleted file and empty parent directories. Cleaned up stale App.xaml.cs comment. Suite now 133 tests (148 - 15).
- **Effort:** XS · **Status:** Fixed

### FIND-032 — Zero test coverage for the new native-backed subsystems · **Confirmed gap**
- **Evidence:** Tests exist for `PdfViewerService` (real `pdfium.dll` pattern, 22 facts) but nothing covers `Rendering/Document/SpreadsheetDocument.cs`, `Rendering/Document/SheetData.cs` (`FromJson` error paths), `Rendering/Presentation/PptxDocument.cs`, or the GridCraft/DeckCraft interop layer. A `SheetDataJsonTests.cs` existed untracked during development and was deleted before ever being committed (visible in session history; absent from HEAD).
- **Impact:** The newest, least-proven code (FFI marshalling, JSON contract, buffer frees) has no regression net; FIND-002-class races cannot be caught.
- **Minimal fix:** Resurrect/extend `SheetDataJsonTests` (fixture JSON incl. merges/colors/control-char text), add wrapper tests gated on `GridCraftNative.IsAvailable` (skip gracefully when DLL absent, like a conditional fact), plus Dispose-under-load tests from FIND-002.
- **Acceptance:** ≥ 25 new facts covering JSON parse, wrapper open/search/dispose, and dispatch; suite stays < 10 s.
- **Effort:** M · **Status:** Confirmed

---

## Medium severity

### FIND-003 — `App_UnhandledException` swallows every XAML exception · Confirmed defect
- **Evidence:** `App.xaml.cs:61-65` sets `e.Handled = true` unconditionally after logging.
- **Impact:** App continues after arbitrary UI-thread failures (broken invariants, disposed native refs) — freezes/corrupted UI instead of a clean crash with the crash log.
- **Fix:** Keep `Handled = true` only for known-benign categories, or fail fast; at minimum record frequency in log. Decide policy ( XS ).
- **Acceptance:** Documented policy + log field; no silent-repeat loop observable in dogfooding. · **Effort:** XS · **Status:** Confirmed

### FIND-004 — Buffer-page re-insert race after eviction · Risk (Low confidence in user impact)
- **Evidence:** `Features/Viewer/Partials/ViewerPage.Viewport.cs:320-331` launches buffer renders on `Task.Run`; `CreateImageOnUI` (441-462) checks `ct` *before* enqueue — a pass can enqueue an Image after a newer `RenderPagesAsync` already evicted that page index (275-289).
- **Impact:** Rare stale page image re-appearing until the next scroll; self-corrects.
- **Fix:** Re-check generation/ct inside `CreateImageOnUI`, or remove cache entry only via the owning generation.
- **Acceptance:** Stress-scroll test (synthetic 500-page PDF) shows no stale insert in logs. · **Effort:** S · **Status:** Needs Investigation (repro)

### FIND-005 — Thumbnail render DPI math off by 4/3 · Confirmed defect
- **Evidence:** `Rendering/Pdf/PdfRenderer.cs:245-246` computes `ptW * dpi / 96.0`; every other path uses `/ 72.0` (e.g. `ViewerPage.Viewport.cs:369-370`, and `RenderViewportAsync` at `PdfRenderer.cs:404-405`). `RenderPageAsync`'s only live caller is thumbnail rendering (`Services/Documents/PdfViewerService.cs:188`).
- **Impact:** Thumbnails rasterize at 75% of intended resolution (612 px instead of 816 px for letter @96 DPI) — soft sidebar thumbs; wasted correctness, no crash.
- **Fix:** Change to `/ 72.0`. · **Acceptance:** Unit-test `renderW == round(ptW * dpi / 72)`; visual spot-check. · **Effort:** XS · **Status:** Confirmed

### FIND-006 — `FS_RECTF` marshalling field order swapped · Confirmed latent defect
- **Evidence:** `Rendering/Pdf/Pdfium.cs:261-268` declares `Left, Bottom, Right, Top`; PDFium's `FS_RECTF` is `left, top, right, bottom` (fpdf_view.h). Byte offsets 4 and 12 therefore carry the wrong semantic labels.
- **Observed masking:** `PdfAnnotationService.cs:82,155` builds rects from min/max (interval survives); `PdfLinkService.cs:133-135` uses opposite corners (interval survives). No current consumer reads orientation from a *native-filled* rect where the swap would misplace pixels.
- **Impact:** Any future consumer that trusts `.Top`/`.Bottom` from `FPDFAnnot_GetRect`/`FPDFLink_GetRect` gets mirrored values; annotation *appearance* may already be subtly wrong for asymmetric rects (Needs Investigation — visual check).
- **Fix:** Reorder fields to `Left, Top, Right, Bottom`; audit the two consumers; visual regression on an annotated PDF. · **Acceptance:** Link/annot hit-tests still pass manual matrix; struct matches header. · **Effort:** XS+S · **Status:** Confirmed (mismatch) / Needs Investigation (visible impact)

### FIND-007 — Fixed FFI result buffers silently truncate search/extract · Confirmed defect (degraded results)
- **Evidence:** `DocxDocument.cs:128,151` use 64 KB buffers; `wordcraft_ffi/src/lib.rs:344` allows 20 000 matches (~40+ bytes each ≈ up to ~800 KB result string); `SpreadsheetDocument.cs:149,171` use 128/256 KB vs gridcraft's 5 000-match cap (`gridcraft_ffi/src/lib.rs:308`). Rust copies `min(len, buf_len)` and returns the byte count — C# cannot distinguish full from truncated.
- **Impact:** Word searches on large DOCX silently drop most matches; truncated final record is dropped by the lenient parser (no crash).
- **Fix:** Protocol change (Rust): return required length when `buf` is null (two-call pattern), or return JSON/length-prefixed; C# grows buffer accordingly. Interim: raise C# buffers to 1 MB and cap matches so worst case fits.
- **Acceptance:** Test: document with >20k matches returns exactly the capped count; no partial records. · **Effort:** S (interim) / M (protocol) · **Status:** Confirmed

### FIND-009 — Unbounded `scale` in `wc_render_page`; PPTX render scale unaudited · Risk
- **Evidence:** `native/wordcraft_ffi/src/lib.rs:157` accepts any `f64` scale (a huge value allocates width×height×4 natively). PPTX path computes `renderScale` in `ViewerPage.Pdf.cs:568+` (not verified against a cap in this audit); DOCX path is dead (FIND-011).
- **Fix:** Clamp scale server-side in Rust (e.g. ≤ 8.0) and/or verify PPTX caller clamp. · **Acceptance:** Fuzz test scale=1e9 returns null buffer, not OOM. · **Effort:** XS · **Status:** Needs Investigation → likely Confirmed after check

### FIND-010 — Spreadsheet sheet-switch load race · Confirmed defect (minor)
- **Evidence:** `SpreadsheetViewer.xaml.cs` `RebuildTabBar` click handlers fire `LoadSheetAsync(idx)` with no cancellation of the previous load; `_data`/`_cellMap` assignments can interleave with draws.
- **Impact:** Rapid sheet clicking can briefly render sheet A's cells under sheet B's tab styling; last write wins.
- **Fix:** Per-load `CancellationTokenSource`, cancel previous, tag results. · **Acceptance:** Rapid-switch stress shows consistent tab/content pairing. · **Effort:** S · **Status:** Confirmed (logic), Low impact

### FIND-012 — ViewerPage partial-class family is a ~3.4k-line god class; `Pdf.cs` coordinates three formats · Maintainability
- **Evidence:** Partials: Pdf 826, Viewport 774, Text 388, Search 299, Sidebar 246, Zoom 241, Toolbar 196, Helpers+shell 154 (`wc -l`). `ViewerPage.Pdf.cs:67-90` dispatches `.pdf/.pptx/.xlsx/code/text`; Pdf.cs also owns PPTX viewport rendering (519-690) and XLSX hosting (774-826).
- **Impact:** Untestable rendering logic; every format change touches the same partial family; naming lies ("Pdf.cs" owns spreadsheets).
- **Fix (Phase C, after tests):** Extract per-format controllers following the proven `SpreadsheetViewer` pattern; keep `ViewerPage` as dispatcher + shared chrome. Do **not** rewrite working render paths (rule 4).
- **Acceptance:** Each format's load/render/dispose lives in one file; ViewerPage < 800 lines; 148+ tests green. · **Effort:** L · **Status:** Confirmed — deferred to Phase C

### FIND-013 — DI container exists but is never configured · Maintainability (dead infrastructure)
- **Evidence:** `Infrastructure/DependencyInjection/ServiceContainer.cs` — `Configure()` has zero callers; `PdfViewerService` is hand-constructed at `Features/Viewer/Partials/ViewerPage.Pdf.cs:30`. Old audit's "no DI" claim is *half*-true: the container arrived (commit history) but was never switched on.
- **Fix:** Either call `ServiceContainer.Configure()` in `App` ctor and resolve per-tab `IPdfViewerService` (registration already `AddTransient` — matches per-tab design), or delete the file. Recommendation: wire it (FIND-001 fix benefits from a single ownership seam).
- **Acceptance:** No direct `new PdfViewerService()` outside DI; tests unchanged. · **Effort:** S · **Status:** Confirmed — decision required

### FIND-014 — Dead render paths and unused dependencies · Maintainability
- **Evidence (all verified zero callers):**
  - `Rendering/Pdf/PdfRenderer.cs:372-494` `RenderViewportAsync` (superseded by `RenderFullPage` in Viewport.cs)
  - `Features/Viewer/Partials/ViewerPage.Viewport.cs:610-737` `RenderFullPageProgressive` (~128 lines; progressive rendering disabled by design, commit `68a3592`)
  - `Services/Documents/PdfViewerService.cs:131-168` `RenderPageProgressiveAsync` + its `IPdfViewerService` member + `_renderedPages` set
  - ~~`Markdig 1.4.0` package reference (`QuickView.csproj:65`) — zero `using Markdig` after Web renderers were removed~~ **FIXED (T-001, commit 538fcb9)**
  - `Rendering/Data/` and `Common/Extensions/` empty directories
- **Fix:** Remove each after a build+test run stays green (rule 7: verified no reflection/XAML/native-export consumers — these are ordinary methods/packages).
- **Acceptance:** Build 0/0, 133 tests pass (Markdig removal complete), grep confirms absence. · **Effort:** S · **Status:** Partially Fixed (Markdig done; render paths remain)

### FIND-018 — All nine Rust engine crates are floating `git` dependencies · Supply-chain / reproducibility risk
- **Evidence:** `native/{gridcraft,wordcraft,deckcraft}_ffi/Cargo.toml` — `gridcraft-*`, `wordcraft-*`, `deckcraft-*` from `github.com/storytold/*` with **no `rev` pin**; only `Cargo.lock` (tracked ✓) holds the commit.
- **Impact:** A `cargo update` or lock regeneration silently moves the entire XLSX/DOCX/PPTX parsing core; builds depend on repository availability; no integrity anchor besides GitHub TLS.
- **Fix:** Pin `rev = "<locked-sha>"` per dependency; document the deliberate upgrade procedure. · **Acceptance:** `cargo update --dry-run` reports nothing to move for these crates. · **Effort:** S · **Status:** Confirmed

### FIND-023 — File-size validation skipped on XLSX/PPTX load paths · Confirmed defect (DoS)
- **Evidence:** `ViewerPage.Pdf.cs:71-74` routes `.pptx/.xlsx` to `LoadPptxAsync/LoadXlsxAsync` — neither calls `FileSizeValidator.IsFileValidAndSafe` (contrast: PDF path via `PdfRenderer.OpenDocumentAsync:53`, text via `TextFileReader.ReadCapped:17`, activation via `App.OpenFilePath:235`). `SpreadsheetDocument.OpenAsync`/`PptxDocument.OpenAsync` call `File.ReadAllBytesAsync` unbounded.
- **Impact:** A multi-GB `.xlsx` (user error or hostile link) is slurped into managed memory before parsing — freeze/OOM. Local-app threat model keeps this Medium, not High.
- **Fix:** One `IsFileValidAndSafe` guard at the top of each loader (caps already exist per-extension). · **Acceptance:** Oversized fixture shows the same error card as PDF; unit test on validator call path. · **Effort:** XS · **Status:** Confirmed

### FIND-027 — `CanvasTextFormat` allocated per visible cell per frame · Performance
- **Evidence:** `SpreadsheetViewer.xaml.cs` cell-text loop (≈ lines 394-440): `using var fmt = new CanvasTextFormat {...}` inside the row/col loops.
- **Impact:** ~10³ native-object alloc/dispose per frame while scrolling; GC and Win2D churn on a hot path.
- **Fix:** Cache a small set of formats keyed by (bold, italic, align, wrap) — at most a dozen variants. · **Acceptance:** Scroll test shows flat managed alloc rate; visuals unchanged. · **Effort:** S · **Status:** Confirmed

### FIND-033 — No solution file, no CI · Process gap
- **Evidence:** No `*.sln` in repo root; no `.github/`; build/test orchestration exists only in `build.ps1` (which also launches the app — not CI-usable).
- **Fix:** Add `QuickView.sln` including test project; GitHub Actions (windows-latest): `dotnet build` + `dotnet test` + `cargo check` for the three crates.
- **Acceptance:** Green pipeline run on a feature branch. · **Effort:** S · **Status:** Confirmed

### FIND-034 — No lifecycle/soak regression tests · Test gap
- **Evidence:** Nothing exercises repeated open/close/switch (the exact class of FIND-001/002). `PdfViewerServiceTests` proves real-native-DLL testing works in this repo.
- **Fix:** Loop tests at service level (open N, close, assert counters) + one UI-adjacent dispatcher test if a seam exists after FIND-012 Phase C. · **Acceptance:** FIND-001/002 fixes demonstrably caught red→green by these tests. · **Effort:** M · **Status:** Confirmed

---

## Low severity

| ID | Finding | Evidence | Fix / Acceptance | Effort | Status |
|---|---|---|---|---|---|
| FIND-008 | Stale "Phase 1 skeleton / NotImplementedException" class docs on `PdfViewerService`; "Phase 2" comments in ViewerPage | `PdfViewerService.cs:10-13` vs real impl | Rewrite comments to describe behavior | XS | Confirmed |
| FIND-015 | `ViewerViewModel` dual state: fields (`_page/_total/_zoom`) authoritative, VM kept in sync via `SyncToViewModel()`; only 4 XAML bindings | `ViewerPage.xaml.cs:26-83`; 4 `x:Bind`/`Binding` hits in ViewerPage.xaml | Document transitional pattern or finish migration (Phase C, optional) | M | Confirmed — accepted for now |
| FIND-016 | Layering quirks: `Services/Updates/UpdateBanner.xaml` (UI in Services); `Rendering/Presentation/PptxViewerControl.xaml` (UI in Rendering) | file listing | Move to UI/Controls or Features when touched next | S | Confirmed — deferred |
| FIND-017 | `Tree.md` stale: lists deleted `.agents/`/`.kiro/`, `FileSystem/` at root; missing `Spreadsheet/`, `Rendering/Document|Presentation`, new crates, new models | `Tree.md` vs `git ls-files` | Regenerate from tree; or delete in favor of codebase-overview.md | S | Confirmed |
| FIND-019 | GridCraft hand-rolled JSON escaping covers exactly `\ " \n \r \t` — correct for XML-derived text (XML forbids other control chars) but an undocumented fragile contract | `gridcraft_ffi/src/lib.rs:466,551-556` | Add contract test with control chars; comment the invariant; prefer `serde_json` on next touch | S | Confirmed (fragility) |
| FIND-020 | `shrink_to_fit` + `mem::forget` + `Vec::from_raw_parts(ptr,len,len)` assumes capacity==len | `gridcraft lib.rs:187-196,605-611`; `wordcraft lib.rs:182-195` | Wrap in one `into_raw_buffer()` helper with SAFETY comment | XS | Confirmed (latent) |
| FIND-022 | `FontDb::with_font_dirs(vec![])` — confirm it still discovers system fonts; if not, PPTX text may render with fallback fonts | `deckcraft_ffi/src/lib.rs:86` | Verify rendered slide text vs PowerPoint; adjust font dirs | XS | Needs Investigation |
| FIND-024 | `PDFiumCore 97.0.4690` used **only** as a `pdfium.dll` carrier (no API usage); build is years old | `QuickView.csproj:71`; grep zero `using` | Upgrade to current PDFiumCore (or pdfium binaries + copy task); re-run render matrix | S | Confirmed |
| FIND-028 | Diagnostic logging flood in render hot path (~15 msgs/page; every scroll) — 5 MB log churn + string allocs (commit `5a20bb6` "diag:") | `PdfRenderer.cs` passim; `ViewerPage.Viewport.cs:236-343` | Gate behind `Logger.Verbose` flag or strip to entry/exit | S | Confirmed |
| FIND-029 | PPTX sidebar thumbnails render sequentially, up to 50 slides at open | `ViewerPage.Pdf.cs:749-757` | Parallelize with small semaphore or defer to idle | S | Confirmed (minor) |
| FIND-030 | `ThumbnailCache` LRU relies on `File.SetLastAccessTime` — Windows often disables last-access updates (non-system volumes) | `ThumbnailCache.cs:51,92,148` | Verify on target volume; else sidecar access-index | S | Needs Investigation |
| FIND-037 | No root `README.md`: prerequisites (.NET 8 SDK, rustup, Windows App SDK), build/test commands undocumented | repo root | Write README linking build.ps1, native build, tests | S | Confirmed |

## Informational / verified-healthy

- **FIND-021** — Rust panic hygiene is *good*: every export wrapped in `catch_unwind` with null fallbacks (all three crates). Stack-overflow/OOM abort remains inherent — document, don't fix.
- **FIND-025** — Old-audit security findings now **Fixed**: SEC-01/02 (WebView2 XSS) — WebView2 fully removed; SEC-03 — SHA-256 checksum verified before Authenticode (`UpdateService.cs:113-131`); SEC-04 — explicit-path `pdfium.dll` load (`Pdfium.cs:21-37`); SEC-05 — `FolderScanner` validates paths (`FolderScanner.cs:119`).
- **FIND-026** — Unassessed this run: `WinVerifyTrust` revocation flags (old SEC-07), installer-script hardening details, `Package.appxmanifest` capability review. Listed for the next security pass.
- **FIND-035** — Test suite health: 148/148 in ~1 s; `PdfViewerServiceTests` demonstrates the repo's best pattern (real native DLL, temp-file fixtures, IDisposable test class).
- **FIND-036** — PDF viewport pipeline is verified **healthy** design-wise: viewport+2-page cache with eviction, DPI cap 192, pixel cap 2600, `ArrayPool` buffers, O(1) page-top cache, Gate-locked PDFium access (post-`e7e7698`). The remaining risks are the lifecycle findings above, not the render loop.
- **FIND-039** — Repo hygiene verified good: `installer/output/`, `native/*/target`, `bin/obj/publish` ignored; `Cargo.lock` tracked; no secrets found in tracked files; `tools/rcedit.exe` (1.3 MB) tracked intentionally for icon stamping.
- **FIND-040** — `Logger` design verified good: bounded async queue (2048), background writer, 5 MB rotation, username sanitization (`Logger.cs:1-80`).

---

## Status legend
`Confirmed` (evidence in source) · `Needs Investigation` (hypothesis, repro required) · `Accepted`/`Deferred` (decision logged) · `Fixed` (verified fixed — verification recorded in [verification-matrix.md](./verification-matrix.md))
