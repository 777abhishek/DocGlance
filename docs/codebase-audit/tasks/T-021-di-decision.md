# T-021 — Execute the DI decision (wire or delete ServiceContainer)

- **FIND:** FIND-013 · Decision **D-03** (recommendation: wire) · Prereq: T-010 (ownership pattern settled) · **In-scope:** `Infrastructure/DependencyInjection/ServiceContainer.cs`, `App.xaml.cs`, `Features/Viewer/Partials/ViewerPage.Pdf.cs` (one line)
- **Evidence:** `Configure()` never called; `ViewerPage.Pdf.cs:30` hand-`new`s `PdfViewerService`; registration already `AddTransient<IPdfViewerService, PdfViewerService>` (correct per-tab lifetime).

## Steps — Option A: wire
1. `ServiceContainer.Configure()` as the first statement of `App` constructor (after `InitializeComponent`).
2. `ViewerPage.Pdf.cs:30`: `private IPdfViewerService _pdfService = ServiceContainer.Services.GetRequiredService<IPdfViewerService>();`
3. Constructor-time resolution only (no service-locator sprinkling in method bodies — that's the anti-pattern the container would enable if abused; keep the seam at construction).

## Steps — Option B: delete
Remove the file; note in D-03 that static-registry + hand-construction is the accepted pattern.

## Verification
Build 0/0; full suite green (test project resolves nothing from the container; if it constructs pages it must keep working — verify `ViewerViewModelTests` unaffected).

## Acceptance criteria
No half-wired container: either `Configure()` called exactly once and zero direct `new PdfViewerService()` outside tests, or the file is gone.

## Rollback
Revert single commit.

## Completion checklist
- [ ] D-03 executed · [ ] No half-state · [ ] Build/test green · [ ] FIND-013 → Fixed

## Actual results
_(fill on completion)_
