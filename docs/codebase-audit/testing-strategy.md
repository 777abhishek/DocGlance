# QuickView — Testing Strategy (2026-10-11)

## Current state (verified)

- **Framework:** xUnit 2.9.2 + FluentAssertions 6.12.2, `QuickView.Tests` references the WinUI project directly (`net8.0-windows10.0.19041.0`, `Win2DWarnNoPlatform=false`). 148 tests, ~1 s, all passing at HEAD `40cdfcd`.
- **Coverage distribution:** ViewerViewModel 39 · HomeViewModel 26 · FileSizeFormatter 15 · WebSecurity **15 (dead — FIND-031)** · PdfViewerService 22 (real `pdfium.dll`!) · DateFormatter 13 · FileSizeValidator 4.
- **Proven pattern:** `PdfViewerServiceTests` — programmatic minimal PDF fixture in temp dir, real native DLL, `IDisposable` cleanup. This is the template for all native-layer tests.
- **Blind spots:** FFI wrappers (`SpreadsheetDocument`, `PptxDocument`, `DocxDocument`), `SheetData.FromJson`, lifecycle/soak behavior, dispatch, FolderScanner/index, UpdateService verify logic.

## Target pyramid

```
        ┌─ E2E smoke (manual matrix, Phase G) ─┐
      ┌─┴─ Integration: real native DLLs, temp fixtures, open→render→search→dispose ─┴┐
    ┌─┴─ Unit: SheetData JSON, parsers, validators, ViewModels, buffer math ──────────┴┐
```

No UI automation framework is proposed this cycle — WinUI 3 appium/WinAppDriver is disproportionate; UI behavior is pinned indirectly through controller/view-model seams created in Phase C.

## Concrete additions (mapped to findings)

| Priority | Test work | Unblocks/guards | Effort |
|---|---|---|---|
| P0 | Replace `WebSecurityTests` (delete) with `SheetDataTests`: parse full-shape JSON fixture (merges, colors, borders, wrap), malformed JSON → null, control-char round-trip vs Rust escaping contract | FIND-031, FIND-019 | S |
| P0 | `NativeWrapperTests` (skippable when `*.IsAvailable == false`): open fixture XLSX/PPTX → count/text/search → dispose ×100 concurrent with renders | FIND-002, FIND-032 | M |
| P0 | Lifecycle soak: open/close N documents through `PdfViewerService` (+ dispatch seam when extracted); assert document-count returns to zero | FIND-001, FIND-034 | S |
| P1 | Dispatch characterization: extension → loader map (table-driven; catches FIND-011-class wiring misses) | FIND-011 regression | S |
| P1 | Validator coverage on XLSX/PPTX oversize fixtures → error card path | FIND-023 | XS |
| P1 | `IPdfViewerService` contract tests (password prompt path, cancel mid-open) | PDF robustness | S |
| P2 | Buffer-truncation tests (search >cap) once FIND-007 protocol chosen | FIND-007 | S |
| P2 | `ThumbnailCache` eviction test with fake clock/filesystem seam (needs tiny seam: inject dir) | FIND-030 | M |

## Fixtures policy

Small **programmatic** fixtures in-test (PDF bytes written inline like `PdfViewerServiceTests`; minimal XLSX/PPTX zips via `System.IO.Compression`) — no binary blobs in git except where a real-world corrupt file is irreplaceable (then under `QuickView.Tests/Fixtures/` with provenance comment).

## CI gates (FIND-033)

`windows-latest`: `dotnet build` (0 warn gate) → `dotnet test` (148+ green) → `cargo check` ×3 → optionally `cargo build --release` + smoke `dotnet build -p:Platform=x64` unpackaged. Native-DLL-dependent tests self-skip when DLLs absent; CI copies them from a build step to keep them running.

## Release gates (Phase G)

All pyramid green · tab-churn memory scenario within ±10 % of baseline · feature regression matrix (open each supported type; search in PDF/XLSX; zoom/rotate PDF; sheet switch; settings persist; update-decline path) executed and recorded in [verification-matrix.md](./verification-matrix.md).
