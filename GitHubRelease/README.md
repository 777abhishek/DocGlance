# GitHub release artifacts

Run `.\build-installer.ps1 -GitHubRelease` from the repository root to create a
versioned release package under `GitHubRelease\v<version>\`. Add or update the
matching source notes at `release-notes\v<version>.md` before building.

Each version folder contains the unsigned x64 setup executable, release notes,
SHA-256 checksum, and generated WinGet manifests. Upload the versioned setup
executable and release notes to the matching GitHub Release. Keep generated
release files out of Git; this folder is ignored except for this guide.

Versions use stable Semantic Versioning (`MAJOR.MINOR.PATCH`), and the version
in `DocGlance.csproj` is the source of truth. Windows package metadata uses the
corresponding four-part numeric version.
