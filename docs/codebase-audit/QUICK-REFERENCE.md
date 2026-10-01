# QuickView Audit — Quick Reference Card

> **One-page summary for rapid triage and decision-making**

---

## 🚨 Critical Issues (Fix Immediately)

| ID | Issue | File | Fix Time | Risk |
|----|-------|------|----------|------|
| **SEC-01** | JavaScript enabled in WebView2 | `Rendering/Web/HtmlRenderer.cs` | 0.5 days | High |
| **SEC-02** | XSS in custom Markdown parser | `Rendering/Web/MarkdownRenderer.cs` | 2 days | High |
| **SEC-03** | No SHA-256 verification on installer | `Infrastructure/Update/UpdateService.cs` | 1 day | High |

**Total critical fix time: 3.5 days**

---

## 📊 Health Score

| Category | Score | Status |
|----------|-------|--------|
| **Security** | 6/10 | ⚠️ 3 critical vulnerabilities |
| **Architecture** | 4/10 | ❌ God classes, no DI, unused ViewModels |
| **Testability** | 2/10 | ❌ No unit tests, logic in UI |
| **Maintainability** | 5/10 | ⚠️ Large classes, static services |
| **Performance** | 7/10 | ✅ Good caching, some memory leaks |
| **Documentation** | 6/10 | ⚠️ Limited XML docs, no diagrams |

**Overall: 5.0/10** — Solid foundations, critical gaps in security and testing

---

## 🎯 Top 5 Quick Wins (Low Risk, High Impact)

| # | Task | Effort | Benefit | Report Section |
|---|------|--------|---------|----------------|
| 1 | Disable JavaScript in HtmlRenderer | 0.5 days | Closes SEC-01 | Security Audit |
| 2 | Replace Markdown parser with Markdig | 2 days | Closes SEC-02 | Refactoring Phase 5 |
| 3 | Add SHA-256 installer verification | 1 day | Closes SEC-03 | Refactoring Phase 9 |
| 4 | Fix pdfium.dll loading to app directory | 1 day | Closes SEC-04 | Refactoring Phase 8 |
| 5 | Add PathValidator to FolderScanner | 0.5 days | Closes SEC-05 | Refactoring Phase 10 |

**Total: 5 days to close 5 security vulnerabilities**

---

## 📈 Refactoring Investment

| Priority | Focus | Time | ROI |
|----------|-------|------|-----|
| **P0** | Security fixes | 5 days | Immediate risk reduction |
| **P1** | ViewModels + testing | 10 days | Enable unit testing |
| **P2** | Service extraction + DI | 25 days | Long-term maintainability |
| **P3** | Performance + polish | 15 days | User experience improvements |

**Recommended first sprint: P0 + P1 = 15 days**

---

## 🏗️ Architecture At-A-Glance

### Current Problems
```
❌ ViewerPage (1,500 lines)
   ├── PDF rendering logic
   ├── Search state
   ├── Bookmark management
   ├── Zoom computation
   └── Sidebar coordination

❌ Static services everywhere
   └── Cannot test, cannot mock

❌ ViewerViewModel exists but unused
   └── State lives in code-behind
```

### Target State
```
✅ ViewerPage (150 lines UI only)
   └── Binds to ViewerViewModel

✅ ViewerViewModel (200 lines)
   └── Page/zoom/doc state

✅ PdfViewerService (400 lines)
   └── Rendering orchestration

✅ Dependency Injection
   └── All services injectable
```

---

## 🔍 Where to Find Things

### "I need to fix a security issue"
→ [Security Audit](./security-audit.md)

### "I need a step-by-step refactoring plan"
→ [Refactoring Roadmap](./refactoring-roadmap.md)

### "I need to understand current architecture"
→ [Codebase Overview](./codebase-overview.md)

### "I need to track technical debt"
→ [Technical Debt Inventory](./technical-debt.md)

### "I need to pitch this to leadership"
→ [Executive Summary](./executive-summary.md)

### "I need to design the future architecture"
→ [Architecture Recommendation](./architecture-recommendation.md)

---

## 📋 Technical Debt by Category

| Category | P0 | P1 | P2 | P3 | P4 | Total |
|----------|----|----|----|----|-------|
| Security | 3 | 2 | 1 | 0 | 2 | **8** |
| Architecture | 0 | 2 | 2 | 1 | 1 | **6** |
| Testing | 0 | 0 | 3 | 0 | 0 | **3** |
| Performance | 0 | 0 | 1 | 5 | 1 | **7** |
| Code Quality | 0 | 1 | 0 | 0 | 3 | **4** |
| Maintainability | 0 | 0 | 2 | 1 | 1 | **4** |
| Documentation | 0 | 0 | 0 | 1 | 2 | **3** |
| **Total** | **3** | **6** | **11** | **10** | **9** | **39** |

---

## 🛡️ Security Findings Summary

### By Severity
- **High:** 3 findings (JavaScript in WebView2, XSS parser, unverified installer)
- **Medium:** 5 findings (DLL hijacking, path validation bypass, no CSP, etc.)
- **Low:** 4 findings (logging, temp directory, registry validation, disposal)
- **Informational:** 4 findings (constants, unsafe blocks, docs)

### By Component
- **Web Rendering (WebView2):** 3 findings
- **Update System:** 2 findings
- **Native Interop:** 1 finding
- **File Scanning:** 1 finding
- **Storage:** 1 finding
- **Logging:** 1 finding
- **Other:** 7 findings

---

## 💡 Key Insights

### ✅ What's Going Well
- Feature-based organization is clean
- Path validators explicitly reject symlinks/junctions
- File size limits enforced per type
- Authenticode verification on downloads
- Recent refactoring has improved infrastructure layer
- No hardcoded secrets

### ⚠️ What Needs Attention
- ViewModels exist but aren't used
- Business logic embedded in UI code
- No dependency injection
- Custom Markdown parser with security flaws
- WebView2 allows arbitrary JavaScript execution
- No unit tests anywhere

### 🔧 What to Do Next
1. **Week 1:** Fix 3 critical security issues (3.5 days)
2. **Week 2:** Add test project + HomeViewModel (4 days)
3. **Week 3-4:** Activate ViewerViewModel (4 days)
4. **Month 2:** Extract services and introduce DI (25 days)

---

## 📞 Decision Matrix

| If you're wondering... | Then... |
|------------------------|---------|
| "Should we fix security issues first?" | **YES** — 3 critical vulnerabilities, 5 days total |
| "Can we ship without fixing architecture?" | **YES** — but technical debt will grow |
| "Do we need dependency injection?" | **YES** — required for unit testing |
| "Should we add tests now?" | **YES** — start with Phase 0 (test project setup) |
| "Can we skip ViewModels?" | **NO** — they're already defined, just need activation |
| "Is the custom Markdown parser worth keeping?" | **NO** — replace with Markdig library |
| "Should we refactor ViewerPage?" | **YES** — but after ViewerViewModel is active |
| "Can we do this incrementally?" | **YES** — roadmap designed for incremental delivery |

---

## 🎓 Key Learnings

1. **ViewModels exist but are unused** — low-hanging fruit to activate MVVM pattern
2. **Security foundations are strong** — explicit validators, but gaps in web rendering
3. **Recent refactoring shows positive trajectory** — team knows how to improve code
4. **Testing gap is critical** — business logic in UI makes testing impossible
5. **Single project is fine** — no need to split into multiple assemblies
6. **Static services prevent testing** — DI introduction is high-leverage investment

---

**Last Updated:** 2026-10-01  
**For full details, see:** [README.md](./README.md)
