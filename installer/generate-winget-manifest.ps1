<#
.SYNOPSIS
    Generates versioned WinGet manifests for a GitHub release installer.

.EXAMPLE
    .\installer\generate-winget-manifest.ps1
    .\installer\generate-winget-manifest.ps1 -Version "1.0.1" -InstallerPath ".\GitHubRelease\v1.0.1\DocGlance-1.0.1-Setup-x64.exe"
#>

[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$DownloadUrl = "",
    [string]$InstallerPath = "",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$projectXml = Get-Content (Join-Path $repositoryRoot "DocGlance.csproj") -Raw
    $Version = $projectXml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='Version']").InnerText
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use the three-part format MAJOR.MINOR.PATCH."
}

$installerFileName = "DocGlance-$Version-Setup-x64.exe"
$defaultReleaseDirectory = Join-Path $repositoryRoot "GitHubRelease\v$Version"
if ([string]::IsNullOrWhiteSpace($DownloadUrl)) {
    $DownloadUrl = "https://github.com/777abhishek/DocGlance/releases/download/v$Version/$installerFileName"
}

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $defaultReleaseDirectory $installerFileName
}

if (-not (Test-Path $InstallerPath)) {
    throw "Installer not found at '$InstallerPath'. Build a GitHub release first with .\build-installer.ps1 -GitHubRelease."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = $repositoryRoot
}

Write-Host "Computing SHA-256 hash for $InstallerPath..." -ForegroundColor Cyan
$hash = (Get-FileHash -Path $InstallerPath -Algorithm SHA256).Hash
Write-Host "SHA-256: $hash" -ForegroundColor Green

$manifestDir = Join-Path $OutputDirectory "manifests\d\DocGlance\DocGlance\$Version"
New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null

$installerYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json

PackageIdentifier: DocGlance.DocGlance
PackageVersion: $Version
MinimumOSVersion: 10.0.19041.0
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

$defaultLocaleYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json

PackageIdentifier: DocGlance.DocGlance
PackageVersion: $Version
PackageLocale: en-US
Publisher: 777abhishek
PublisherUrl: https://github.com/777abhishek/DocGlance
PublisherSupportUrl: https://github.com/777abhishek/DocGlance/issues
Author: 777abhishek
PackageName: DocGlance
PackageUrl: https://github.com/777abhishek/DocGlance
License: MIT
LicenseUrl: https://github.com/777abhishek/DocGlance/blob/main/LICENSE
Copyright: Copyright (c) 2026 777abhishek
ShortDescription: Native Windows viewer for documents, spreadsheets, presentations, and text files
Description: DocGlance is a 64-bit Windows desktop application built with .NET 8, WinUI 3, and native Rust rendering engines.
Moniker: DocGlance
Tags:
  - viewer
  - pdf
  - preview
  - document-viewer
  - file-preview
  - winui3
ReleaseNotesUrl: https://github.com/777abhishek/DocGlance/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@

$versionYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json

PackageIdentifier: DocGlance.DocGlance
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@

$installerManifest = Join-Path $manifestDir "DocGlance.DocGlance.installer.yaml"
$localeManifest = Join-Path $manifestDir "DocGlance.DocGlance.locale.en-US.yaml"
$versionManifest = Join-Path $manifestDir "DocGlance.DocGlance.yaml"
Set-Content -Path $installerManifest -Value $installerYaml -Encoding UTF8
Set-Content -Path $localeManifest -Value $defaultLocaleYaml -Encoding UTF8
Set-Content -Path $versionManifest -Value $versionYaml -Encoding UTF8
Write-Host "Generated Winget manifests under: $manifestDir" -ForegroundColor Green
Write-Host ""
Write-Host "To test locally with winget:" -ForegroundColor Yellow
Write-Host "  winget install --manifest `"$manifestDir`"" -ForegroundColor White
