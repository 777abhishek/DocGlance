# Native Rust Dependencies - Update Policy

## Overview
All native Rust FFI libraries in this project are configured to automatically fetch and use the latest commits from their upstream repositories on every build.

## Updated Cargo.toml Files

### 1. deckcraft_ffi
- **Repository**: https://github.com/storytold/deckcraft
- **Branch**: `main`
- **Dependencies**:
  - deckcraft-pptx
  - deckcraft-render
  - deckcraft-model
  - deckcraft-fonts
  - deckcraft-geom

### 2. gridcraft_ffi
- **Repository**: https://github.com/storytold/gridcraft
- **Branch**: `main`
- **Dependencies**:
  - gridcraft-core
  - gridcraft-model
  - gridcraft-xlsx
  - gridcraft-engine

### 3. wordcraft_ffi
- **Repository**: https://github.com/storytold/wordcraft
- **Branch**: `main`
- **Dependencies**:
  - wordcraft-docx
  - wordcraft-layout
  - wordcraft-render
  - wordcraft-doc
  - wordcraft-geom

## Build Process

The `build-native.ps1` script now includes `cargo update` before each build:

1. **cargo update** - Fetches the latest commits from git repositories and updates Cargo.lock
2. **cargo build --release** - Compiles with the latest dependencies

This ensures every build uses the most recent upstream changes.

## Latest Update Summary

**Date**: 2026-10-12

### deckcraft_ffi
- Updated from commit `84acb498` to `688c7ef5`
- All packages updated to version `0.6.1`

### gridcraft_ffi
- Updated from commit `47dcd84e` to `fc45b7f6`
- All packages updated to version `0.6.1`

### wordcraft_ffi
- Updated from commit `dbfa2af6` to `159b84f5`
- All packages already at version `0.6.1`, fetched latest commits

## Manual Update

To manually update dependencies without building:

```powershell
cd native\deckcraft_ffi
cargo update

cd ..\gridcraft_ffi
cargo update

cd ..\wordcraft_ffi
cargo update
```

## Version Pinning

If you need to pin to a specific commit instead of tracking the latest:

```toml
[dependencies]
package-name = { git = "https://github.com/org/repo", rev = "commit-hash" }
```

## Notes

- Git dependencies are cached in `~/.cargo/git/`
- `Cargo.lock` files track exact commit hashes and are updated by `cargo update`
- The `branch = "main"` specification ensures tracking the main branch head
