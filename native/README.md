# DocGlance Native FFI Libraries

## Overview

DocGlance uses four Rust FFI libraries to render documents:

- **pdfcraft_ffi** - PDF rendering, text extraction, search, merge/split, PNG export
- **deckcraft_ffi** - PowerPoint (PPTX) rendering, text extraction, search
- **gridcraft_ffi** - Excel (XLSX) rendering, text extraction, search  
- **wordcraft_ffi** - Word (DOCX) rendering, text extraction, search

## Building

Build an individual FFI from the repository root with its committed lockfile:

```powershell
cargo build --release --locked --manifest-path native\pdfcraft_ffi\Cargo.toml
```

Replace `pdfcraft_ffi` with `deckcraft_ffi`, `gridcraft_ffi`, or
`wordcraft_ffi` as needed. `build-native.ps1` also builds and copies
`translation_ffi`; do not use it when validating a document-engine-only update.

## Reviewed upstream releases

As of 2026-11-04, the FFI projects pin these releases:

| FFI | Release | Commit |
|---|---|---|
| `deckcraft_ffi` | `v0.15.0` | `3e178d8f` |
| `gridcraft_ffi` | `v0.16.0` | `85e51e00` |
| `wordcraft_ffi` | `v0.15.1` | `08afab50` |
| `pdfcraft_ffi` | `v0.18.1` | `8bb0528b` |

The official release review and notable changes are recorded in
[UPDATE-POLICY.md](./UPDATE-POLICY.md). `translation_ffi` is intentionally
outside this update.

## Updating

1. Review the engine's official release notes and confirm the changes address a
   DocGlance need.
2. Change only that FFI's dependency tag in its `Cargo.toml`; keep its
   `Cargo.lock` committed.
3. For PdfCraft, also update the active path-patched crates under
   `pdfcraft_ffi\vendor` to the corresponding official release copies.
4. Run `cargo update --manifest-path native\<project>\Cargo.toml`, then
   `cargo check --locked` and `cargo build --release --locked` for that FFI.
5. Test representative documents and review the manifest, lockfile, and any
   vendored source changes together.

Do not follow upstream `main` or update dependencies automatically during
normal builds.

## References

- [DeckCraft](https://github.com/storytold/deckcraft) — PPTX renderer
- [GridCraft](https://github.com/storytold/gridcraft) — XLSX engine
- [WordCraft](https://github.com/storytold/wordcraft) — DOCX layout engine
- [PdfCraft](https://github.com/storytold/pdfcraft) — PDF renderer
