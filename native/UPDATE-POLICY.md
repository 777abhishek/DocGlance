# Native FFI Dependency Update Record

## Review date: 2026-11-04

Reviewed the official upstream releases for all native document engines.
`translation_ffi` was explicitly excluded.

| FFI project | Previous lock | Official release selected | Notable upstream changes |
|---|---|---|---|
| `deckcraft_ffi` | `0.6.1` at `688c7ef5` | [DeckCraft v0.15.0](https://github.com/storytold/deckcraft/releases/tag/v0.15.0) | Shape rendering fixes, text inset layout improvements, color alpha handling, PPTX tag import limits, and invalid slide-index validation. |
| `gridcraft_ffi` | `0.6.1` at `fc45b7f6` | [GridCraft v0.16.0](https://github.com/storytold/gridcraft/releases/tag/v0.16.0) | New formula/equation support and fixes/features for charts, calculation, and spreadsheet data handling. |
| `wordcraft_ffi` | `0.6.1` at `159b84f5` | [WordCraft v0.15.1](https://github.com/storytold/wordcraft/releases/tag/v0.15.1) | Parser hardening for malformed TIFF and WOFF2/font metadata, plus DOC/DOCX import, layout, and proofing improvements. |
| `pdfcraft_ffi` | `0.7.1` at `7163ba3d` | [PdfCraft v0.18.1](https://github.com/storytold/pdfcraft/releases/tag/v0.18.1) | PDF rendering/open performance improvements, fixes across redaction/accessibility/forms, and export/optimization improvements. |

Each FFI project's `Cargo.toml` now pins the selected tag, and its lockfile
records the corresponding upstream commit. Translation dependencies were not
changed. `pdfcraft_ffi` also carries the active path-patched renderer/parser
crates from PdfCraft v0.18.1; these were synchronized with that release to
resolve API incompatibilities and retain its decoding/resource-limit fixes.

## Ongoing policy

- Keep native engine dependencies pinned to reviewed upstream release tags.
- Do not fetch latest branch heads or run `cargo update` during normal builds.
- For an update, review the official release notes, update only the affected
  FFI crate, build it, and exercise representative documents before committing
  the manifest and lockfile.
- Keep the engine-specific strategy in [DEPENDENCY-STRATEGY.md](./DEPENDENCY-STRATEGY.md).
