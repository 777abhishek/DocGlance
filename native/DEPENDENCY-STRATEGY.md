# Native FFI Dependency Strategy

## Engines

DocGlance's native FFI projects use four external document engines:

- `deckcraft_ffi` — PPTX parsing and rendering
- `gridcraft_ffi` — XLSX parsing, calculation, and rendering
- `wordcraft_ffi` — DOCX parsing, layout, and rendering
- `pdfcraft_ffi` — PDF rendering and document operations

The engines are maintained in the [storytold organization](https://github.com/storytold).
`translation_ffi` is separate and is not included in this engine-update policy.

## Pinned upstream releases

As of 2026-11-04, the FFI manifests and lockfiles pin:

| FFI project | Upstream repository | Release tag | Locked commit |
|---|---|---|---|
| `deckcraft_ffi` | [deckcraft](https://github.com/storytold/deckcraft) | `v0.15.0` | `3e178d8f` |
| `gridcraft_ffi` | [gridcraft](https://github.com/storytold/gridcraft) | `v0.16.0` | `85e51e00` |
| `wordcraft_ffi` | [wordcraft](https://github.com/storytold/wordcraft) | `v0.15.1` | `08afab50` |
| `pdfcraft_ffi` | [pdfcraft](https://github.com/storytold/pdfcraft) | `v0.18.1` | `8bb0528b` |

Use release tags in `Cargo.toml` and commit each FFI project's `Cargo.lock`.
Do not track upstream `main` or run `cargo update` automatically during normal
builds: updates must be deliberate, reviewed, and tested.

PdfCraft's release also supplies path-patched renderer and parser crates. The
active `pdfcraft_ffi` vendor copies and `[patch.crates-io]` entries must stay
aligned with the selected PdfCraft tag; do not update the engine tag while
leaving those copies on an older release.

## Updating

1. Check the upstream releases and compare their notes with the feature and bug
   fixes needed by DocGlance.
2. Change the dependency tag in the relevant FFI project's `Cargo.toml`.
3. Run `cargo update --manifest-path native\<project>\Cargo.toml`.
4. Build the affected FFI crate and run the corresponding DocGlance tests.
5. Test representative documents and review the generated lockfile before
   committing the manifest and lockfile together.

The `native\build-native.ps1` script builds the FFI projects. Do not use it as
an update mechanism; dependency resolution is controlled by the committed
manifests and lockfiles.
