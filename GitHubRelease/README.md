# GitHub release artifacts

The Microsoft Store is the recommended public distribution channel for
DocGlance. To create a Store submission bundle, run this from the repository
root:

```powershell
.\build-installer.ps1 -StoreUpload
```

This produces an `.msixupload` bundle under `installer\output\`; upload it to
Partner Center. It is not an MSI and is not a customer installer. Microsoft
signs the package accepted for Store distribution, and users install it from
the [DocGlance Store listing](https://apps.microsoft.com/detail/9P6KFLR429Z6?hl=en-us&gl=IN).

## Optional GitHub EXE installer

The GitHub release workflow creates a separate direct-download setup EXE:

```powershell
.\build-installer.ps1 -GitHubRelease
```

It creates an unsigned versioned release package under
`GitHubRelease\v<version>\`. For a trusted signed EXE, import a CA-issued
Authenticode code-signing certificate with its private key into
`Cert:\CurrentUser\My` and pass its thumbprint. A self-signed Store/MSIX
publisher certificate does not establish trust for the public EXE installer:

```powershell
.\build-installer.ps1 -GitHubRelease -SigningCertificateThumbprint "<thumbprint>"
```

The script signs both `DocGlance.exe` and the setup installer, then verifies
their signatures and certificate chain before creating the release bundle. Add
or update the matching source notes at `release-notes\v<version>.md` before
building.

Each version folder contains the x64 setup executable (signed when a trusted
signing certificate was supplied), release notes, SHA-256 checksum, and
generated WinGet manifests. Upload the versioned setup executable and release
notes to the matching GitHub Release.

The WinGet manifests are also generated at
`manifests\d\DocGlance\DocGlance\<version>\` in the repository root for
submission to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).
Submit that version directory as a pull request after uploading and verifying the
release installer. Keep generated release files out of Git; this folder is
ignored except for this guide.

Versions use stable Semantic Versioning (`MAJOR.MINOR.PATCH`), and the version
in `DocGlance.csproj` is the source of truth. Windows package metadata uses the
corresponding four-part numeric version.
