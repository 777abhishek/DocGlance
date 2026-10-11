# T-010 — Fix native-document lifecycle leaks on tab close and format switch

- **FIND:** FIND-001 (High) · Prereq: none · **In-scope:** `Features/Viewer/ViewerPage.xaml.cs`, `Features/Viewer/Partials/ViewerPage.Pdf.cs`, `Features/Viewer/Spreadsheet/SpreadsheetViewer.xaml.cs` (Cleanup only)
- **Current behavior (evidence @40cdfcd):** `CancelLoad()` (`ViewerPage.xaml.cs:101-128`) cancels CTSs and nulls `_pdfState` but never calls `_pdfService.CloseDocument()` and never disposes `_spreadsheetDocument`/`_pptxDocument`. `LoadXlsxAsync` nulls `_pdfState` (`Pdf.cs:783`) without closing. Native docs freed only on same-format reload (`Pdf.cs:442,792`). `SpreadsheetViewer.Cleanup()` nulls `_doc`.

## Steps
1. Add one private `ReleaseNativeDocuments()` in ViewerPage: `_pdfService.CloseDocument(); _spreadsheetDocument?.Dispose(); _spreadsheetDocument = null; _pptxDocument?.Dispose(); _pptxDocument = null; _pdfState = null;` (guard each in try/catch that logs).
2. Call it from `CancelLoad()` (replacing the bare `_pdfState = null`) **and** at the top of every format loader for *foreign* formats (PDF loader releases pptx/xlsx docs; XLSX loader releases pdf/pptx; PPTX loader releases pdf/xlsx). The existing `ClearPptxCache()` calls may subsume part of this — unify rather than duplicate.
3. Coordinate with D-03: if DI is wired first, resolution stays per-tab; `Dispose()` on the page's service (implements IDisposable → CloseDocument) is the same seam.
4. Tests: add `PdfViewerService`-level soak (open/close ×20 → assert service reports zero live documents — add an internal counter or expose via test hook) + a UI-dispatch smoke if the seam allows.

## Verification
- `dotnet build` 0/0; full suite green.
- New soak test passes; manual: open/close 20 PDF tabs in the running app, working set returns to ≈pre-open level (±10 %) after each close (record numbers).

## Acceptance criteria
No code path retains a native document after its tab closes or its format is replaced; soak test in suite; manual memory numbers recorded below.

## Regression risks
Double-dispose if two paths release — mitigated by null-guard pattern in `ReleaseNativeDocuments`; watch for `ObjectDisposedException` from service reuse after close (service must tolerate Close→Load again — it already does, `LoadDocumentAsync` calls `CloseDocument` first).

## Rollback
Single revert; leak returns, no corruption risk.

## Completion checklist
- [ ] Release path unified · [ ] Soak test added · [ ] Manual memory evidence · [ ] FIND-001 → Fixed + verification-matrix row

## Actual results
_(fill on completion)_
