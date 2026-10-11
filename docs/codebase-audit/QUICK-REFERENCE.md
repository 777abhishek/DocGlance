# QuickView Audit — Quick Reference (2026-10-11)

**Current phase:** audit complete → awaiting approval to start Phase A.
**Baseline:** build 0/0 · tests 148/148 · cargo clean @ `40cdfcd`.

## Commands

```powershell
dotnet build QuickView.csproj -c Debug          # 0 warnings gate
dotnet test QuickView.Tests/QuickView.Tests.csproj
# Rust (each crate):
cd native/gridcraft_ffi; cargo check            # also deckcraft_ffi, wordcraft_ffi
./build.ps1                                      # full build + launch (interactive)
```

## Fix next (ordered)

1. **T-001** delete dead tests + Markdig (XS)
2. **T-002** .sln + CI + README (S)
3. **T-010** native lifecycle leaks FIND-001 (S) ← highest-value fix
4. **T-011** dispose races FIND-002 (S, after T-010)

## Top risks (details in findings.md)

FIND-001 tab-close native leaks · FIND-002 dispose races · FIND-011 DOCX dead path · FIND-031/032 test integrity · FIND-023 size guards · FIND-018 unpinned git deps.

## Pending decisions (owner input welcome, recommendations in decision-log.md)

D-03 wire-or-delete DI · D-04 wire-or-remove WordCraft DOCX.

## Where things are

Canonical findings → [findings.md](./findings.md) · plan → [implementation-plan.md](./implementation-plan.md) · tasks → [tasks/](./tasks/) · verification status → [verification-matrix.md](./verification-matrix.md) · decisions → [decision-log.md](./decision-log.md).
