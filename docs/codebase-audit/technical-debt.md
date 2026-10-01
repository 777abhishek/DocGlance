# QuickView — Technical Debt Inventory

> Audit date: 2026-10-01  
> Read-only analysis. No source files were modified.

---

## Debt Categories

| ID | Area | Finding | Severity | Effort | Impact | Priority |
|---|---|---|---|---|---|---|\
| **ARCH-01** | Architecture | ViewerPage is a god class (~1,500 lines across 8 partials) | High | High | High | P1 |
| **ARCH-02** | Architecture | ViewerViewModel exists but is inactive; state lives in code-behind | High | Medium | High | P1 |
| **ARCH-03** | Architecture | HomePage has no ViewModel; all logic in code-behind (~500 lines) | Medium | Low | Medium | P2 |
| **ARCH-04** | Architecture | No dependency injection container; all services are static | High | High | High | P2 |
| **ARCH-05** | Architecture | IFileRenderer returns UIElement (UI framework coupling) | Medium | Medium | Medium | P3 |
| **ARCH-06** | Architecture | Static TitleBarWindow.Current singleton | Low | Low | Low | P4 |
| **SEC-01** | Security | HtmlRenderer loads file:// with JavaScript enabled | **Critical** | Low | High | **P0** |
| **SEC-02** | Security | Custom Markdown parser susceptible to XSS | **Critical** | Low | High | **P0** |
| **SEC-03** | Security | Installer download without SHA-256 verification | **Critical** | Low | High | **P0** |
| **SEC-04** | Security | pdfium.dll loaded by short name (hijacking risk) | Medium | Low | Medium | P1 |
| **SEC-05** | Security | FolderScanner bypasses path validation | Medium | Low | Medium | P1 |
| **SEC-06** | Security | BookmarkStore writes unvalidated paths | Medium | Low | Low | P3 |
| **SEC-07** | Security | Revocation checking disabled in WinVerifyTrust | Low | Low | Low | P4 |
| **SEC-08** | Security | Markdown HTML has no Content Security Policy | Medium | Low | Medium | P2 |
| **TEST-01** | Testing | No unit tests exist in codebase | High | High | High | P2 |
| **TEST-02** | Testing | Business logic in UI code prevents testing | High | High | High | P2 |
| **TEST-03** | Testing | Static services cannot be mocked | High | High | High | P2 |
| **PERF-01** | Performance | Prefetch strategy hard-coded (±2 pages) | Low | Low | Medium | P3 |
| **PERF-02** | Performance | Eviction based on page distance, not memory usage | Low | Low | Medium | P3 |
| **PERF-03** | Performance | Thumbnail loading synchronous in UI thread | Medium | Low | Medium | P3 |
| **PERF-04** | Performance | No virtualization in thumbnail grid | Medium | Medium | High | P3 |
| **PERF-05** | Performance | WebView2 instances never disposed (memory leak) | Medium | Low | Medium | P2 |
| **PERF-06** | Performance | Markdown rendering blocks UI thread | Low | Low | Low | P4 |
| **PERF-07** | Performance | FolderScanner unbounded parallelism | Low | Low | Low | P4 |
| **CODE-01** | Code quality | Custom Markdown parser (300+ lines) prone to edge cases | Medium | Low | Medium | P1 |
| **CODE-02** | Code quality | Duplicate cache logic in ThumbnailService and ThumbnailCache | Low | Low | Low | P4 |
| **CODE-03** | Code quality | PDF-specific models leaked into shared Models/ folder | Low | Low | Low | P4 |
| **CODE-04** | Code quality | Extension sets duplicated across HomePage and FileTypeDetector | Low | Low | Low | P4 |
| **DOC-01** | Documentation | File size limits documented as "adjustable" but are const | Low | Low | Low | P4 |
| **DOC-02** | Documentation | No XML doc comments on public interfaces | Low | Low | Medium | P3 |
| **DOC-03** | Documentation | No architecture diagrams | Low | Low | Medium | P4 |
| **MAINT-01** | Maintainability | ViewerPage.Pdf.cs (~500 lines) mixes rendering + UI logic | High | High | High | P2 |
| **MAINT-02** | Maintainability | HomePage.xaml.cs (~500 lines) procedural scan logic | High | Low | Medium | P2 |
| **MAINT-03** | Maintainability | TitleBarWindow.xaml.cs (~400+ lines) mixes chrome + document logic | Medium | Medium | Medium | P3 |
| **MAINT-04** | Maintainability | Logger path sanitisation regex is Windows-only | Low | Low | Low | P4 |

---

## Priority Definitions

- **P0 (Critical):** Security vulnerabilities with confirmed exploit paths; fix immediately
- **P1 (High):** High-impact architectural or security issues; fix within 1-2 sprints
- **P2 (Medium):** Significant maintainability or testability problems; fix within 1-2 months
- **P3 (Low):** Performance or code quality improvements; fix within 6 months
- **P4 (Nice-to-have):** Documentation or minor improvements; fix opportunistically

---

## Effort Definitions

- **Low:** 0.5-2 days
- **Medium:** 2-5 days
- **High:** 5-10 days

---

## Impact Definitions

- **High:** Prevents testing, causes security risk, or blocks feature development
- **Medium:** Reduces code quality or complicates maintenance
- **Low:** Minor inconvenience; does not block work

---

## Recommended Debt Reduction Sequence

### Sprint 1 (Security P0)
- **SEC-01:** Disable JavaScript in HtmlRenderer (0.5 days)
- **SEC-02:** Replace Markdown parser with Markdig (2 days)
- **SEC-03:** Add SHA-256 installer verification (1 day)
- **SEC-04:** Restrict pdfium.dll loading (1 day)
- **SEC-05:** Apply PathValidator in FolderScanner (0.5 days)

**Total:** 5 days

### Sprint 2 (Architecture P1)
- **ARCH-03:** Extract HomeViewModel (2-3 days)
- **CODE-01:** Validate Markdig replaces custom parser (already done in Sprint 1)
- **TEST-01:** Add xUnit project + basic tests (1 day)

**Total:** 4 days

### Sprint 3 (Architecture P1 continued)
- **ARCH-02:** Activate ViewerViewModel (3-4 days)
- **TEST-02:** Add tests for ViewerViewModel (1 day)

**Total:** 5 days

### Sprint 4 (Architecture P2)
- **ARCH-01:** Extract PdfViewerService from ViewerPage (4-5 days)
- **MAINT-01:** Shrink ViewerPage.Pdf.cs via service extraction (covered by ARCH-01)

**Total:** 5 days

### Sprint 5 (Architecture P2 continued)
- **ARCH-04:** Introduce dependency injection (5-7 days)
- **TEST-03:** Update tests to inject mocks (1-2 days)

**Total:** 8 days

### Sprint 6 (Performance & Polish P2-P3)
- **PERF-05:** Dispose WebView2 instances (0.5 days)
- **SEC-08:** Add CSP to Markdown HTML (0.5 days)
- **ARCH-05:** Fix IFileRenderer interface (3-4 days)

**Total:** 5 days

---

## Total Estimated Effort by Priority

| Priority | Count | Estimated Effort |
|---|---|---|\
| P0 | 3 | 5 days |
| P1 | 6 | 9 days |
| P2 | 11 | 25 days |
| P3 | 10 | 15 days |
| P4 | 9 | 5 days |
| **Total** | **39 items** | **59 days** |

---

## Debt Accumulation Trends

### Recent improvements
- Created `Shared/` folder with formatters, validators, threading helpers
- Introduced provider pattern for thumbnails
- Refactored `PdfOperationBase` to eliminate duplicate lock/page-load patterns
- Moved `SearchHighlighter` from `Rendering/` to `Features/`
- Deleted orphaned duplicate files

**Observation:** Recent refactoring work has **reduced** technical debt in the infrastructure layer. The remaining debt is concentrated in the presentation layer (ViewerPage, HomePage).

### Risk of accumulation
- **ViewerPage:** Every new feature increases the god class
- **No tests:** Changes cannot be validated automatically
- **Static services:** Every new service continues the anti-pattern
- **Custom Markdown parser:** Every edge case fix adds complexity

**Recommendation:** Address ARCH-01, ARCH-02, ARCH-04 before adding major new features.

---

## Dependencies Between Debt Items

```
TEST-01 (add test project)
  └── prerequisite for any test-related fixes

ARCH-03 (HomeViewModel)
  └── validates ViewModel pattern; prerequisite for ARCH-02

ARCH-02 (ViewerViewModel)
  └── prerequisite for ARCH-01 (PdfViewerService extraction)

ARCH-01 (PdfViewerService)
  └── prerequisite for ARCH-04 (DI)

SEC-02 (Markdig)
  └── prerequisite for SEC-08 (CSP)

ARCH-04 (DI)
  └── resolves TEST-03 (static services cannot be mocked)
```

---

## Debt That Can Be Fixed Independently

- **SEC-01, SEC-03, SEC-04, SEC-05** (security fixes) — no dependencies
- **PERF-05** (WebView2 disposal) — no dependencies
- **CODE-04** (extension set duplication) — no dependencies
- **DOC-01, DOC-02, DOC-03** (documentation) — no dependencies
- **MAINT-04** (logger regex) — no dependencies

**Quick wins:** These 9 items can be fixed opportunistically without blocking other work.
