<#
.SYNOPSIS
    Generates the winget installer manifest with the exact SHA-256 hash
    of the built installer (installer\output\DocGlance-Setup-x64.exe).

.EXAMPLE
    .\installer\generate-winget-manifest.ps1
    .\installer\generate-winget-manifest.ps1 -Version "1.0.1" -DownloadUrl "https://github.com/myorg/DocGlance/releases/download/v1.0.1/DocGlance-Setup-x64.exe"
#>

[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$DownloadUrl = "https://github.com/your-org/DocGlance/releases/download/v1.0.0/DocGlance-Setup-x64.exe",
    [string]$InstallerPath = "$PSScriptRoot\output\DocGlance-Setup-x64.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $InstallerPath)) {
    Write-Error "Installer not found at '$InstallerPath'. Please build it first with .\build-installer.ps1."
}

Write-Host "Computing SHA-256 hash for $InstallerPath..." -ForegroundColor Cyan
$hash = (Get-FileHash -Path $InstallerPath -Algorithm SHA256).Hash
Write-Host "SHA-256: $hash" -ForegroundColor Green

$manifestDir = "$PSScriptRoot\winget\manifests\q\DocGlance\DocGlance\$Version"
New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null

$installerYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json

PackageIdentifier: DocGlance.DocGlance
PackageVersion: $Version
MinimumOSVersion: 10.0.17763.0
InstallerType: inno
InstallModes:
  - interactive
  - silent
  - silentWithProgress
Installers:
  - Architecture: x64
    Scope: user
    InstallerUrl: $DownloadUrl
    InstallerSha256: $hash
    InstallerSwitches:
      Custom: /CURRENTUSER
  - Architecture: x64
    Scope: machine
    InstallerUrl: $DownloadUrl
    InstallerSha256: $hash
    InstallerSwitches:
      Custom: /ALLUSERS
ManifestType: installer
ManifestVersion: 1.6.0
"@

$targetFile = "$manifestDir\DocGlance.DocGlance.installer.yaml"
Set-Content -Path $targetFile -Value $installerYaml -Encoding UTF8
Write-Host "Generated: $targetFile" -ForegroundColor Green
Write-Host ""
Write-Host "To test locally with winget:" -ForegroundColor Yellow
Write-Host "  winget install --manifest `"$manifestDir`"" -ForegroundColor White
