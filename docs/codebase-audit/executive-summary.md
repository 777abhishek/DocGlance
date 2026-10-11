# QuickView — Executive Summary (2026-10-11)

> Audit of HEAD `40cdfcd`. Baseline verified: build 0/0, 148/148 tests, `cargo check` clean. Read-only audit; no source modified.

## Overall health: **moderate — solid core, leaky edges, stale docs**

QuickView has matured substantially since the 2026-10-01 audit. The scary findings of that round are gone: WebView2 (and its XSS surface) is fully removed, the updater verifies SHA-256 **and** Authenticode, `pdfium.dll` loads from an explicit path, the scanner validates paths, and a real test project (148 tests, 1 s) exists. The PDF viewport pipeline — the app's heart — is now well-engineered: viewport-bounded page cache with eviction, DPI/pixel caps, pooled buffers, and a single process-wide PDFium lock.

The 2026-10-11 round found its problems at the **new edges**: the three Rust-backed renderers (GridCraft XLSX, DeckCraft PPTX, WordCraft DOCX) integrated in the last two days ship with lifecycle defects, a dead DOCX path, and zero test coverage; and the entire `docs/codebase-audit/` set (3,155 lines) describes a codebase that no longer exists.

## Top risks (all evidence in [findings.md](./findings.md))

| # | Finding | One-line impact |
|---|---|---|
| 1 | **FIND-001** (High) | Every closed tab leaks its FPDF/GridCraft/DeckCraft native document; PDF→XLSX switch leaks too. |
| 2 | **FIND-002** (High) | Disposing the three native wrappers mid-render frees handles still in use → crash/corruption on slow renders. |
| 3 | **FIND-011** (High) | WordCraft DOCX renderer committed as "integrated" but never wired — `.docx` still shows an info card. |
| 4 | **FIND-031/032** (High) | 15 tests assert a copy of a regex from deleted code; the new FFI layer has **no** tests. |
| 5 | **FIND-023** (Med) | XLSX/PPTX loaders skip file-size validation → multi-GB file is fully slurped into memory. |
| 6 | **FIND-018** (Med) | All nine Rust engine crates are unpinned `git` deps — `cargo update` silently moves the parsing core. |

## Biggest simplification opportunities (verified, not cosmetic)

- **~350+ lines of dead render paths** (`RenderViewportAsync`, `RenderFullPageProgressive`, `RenderPageProgressiveAsync` + service member) and the unused `Markdig` package — FIND-014.
- **`ServiceContainer` (DI) exists but is never `Configure()`d** — dead infrastructure; wire it or delete it (FIND-013).
- **`Rendering/Office/*Renderer` info-card stubs** are now reachable only for legacy extensions (`.doc`, `.xls`, `.csv`, `.ppt`) — their role should be documented or folded into the registry fallback (FIND-016-related).
- **`ViewerPage.Pdf.cs` (826 lines) coordinates three formats** — the proven `SpreadsheetViewer` extraction pattern should be applied to PPTX/PDF in Phase C, not before tests exist (FIND-012).

## Release blockers (for any Store/installer release)

1. FIND-001 + FIND-002 (native leaks/crashes — stability under normal tab usage).
2. FIND-011 decision (either wire DOCX or remove the dead path and stop shipping `wordcraft_ffi.dll`).
3. FIND-023 (size validation) — one-line guards.
4. FIND-031 (delete false-confidence tests) and FIND-032 (minimum coverage for FFI layer).

## Recommended order of work

**Phase A (now, ≤ half a day):** delete dead tests/files, add `.sln` + CI, write README, record baseline. → **Phase B (correctness/resources, ~2–3 days):** FIND-001, 002, 023, 005, 006, 007, 003 with regression tests. → **Phase C (structure, ~3–5 days):** DOCX decision, DI decision, dead-path removal, ViewerPage split *after* characterization tests. → **Phase D (perf):** measure first (FIND-027, 028). → **Phase E/F (tests + docs):** FFI coverage, doc reconciliation (mostly done in this audit). → **Phase G:** packaging + regression matrix, release decision.

## Numbers

- ~16.4k LOC C# (113 files), ~2.4k XAML, ~1.4k Rust across 3 cdylib crates.
- ViewerPage family: 9 files, ~3.4k lines (largest single class family).
- Findings: **5 High, 15 Medium, 13 Low, 6 Informational/verified-healthy** (one register, deduplicated).
- Tests: 148 passing — but concentrated in ViewModels/formatters/PDF service; **zero** on the FFI layer added 2026-10-11.
