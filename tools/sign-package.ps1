# ─────────────────────────────────────────────────────────────────────────────
#  QuickView one-time signing setup + sign script
#
#  Creates a self-signed code-signing certificate (CN=QuickView), exports
#  QuickView.pfx (private, gitignored) and QuickView.cer (public, share this),
#  and signs the built MSIX.
#
#  Usage:
#    .\tools\sign-package.ps1                     # create cert (if missing) + sign
#    .\tools\sign-package.ps1 -RecreateCert       # force a new certificate
#    .\tools\sign-package.ps1 -Trust              # also import CER into TrustedPeople (needs admin)
#    .\tools\sign-package.ps1 -Path <msix path>   # sign a specific package
# ─────────────────────────────────────────────────────────────────────────────
param(
    [switch]$RecreateCert,
    [switch]$Trust,
    [string]$Path
)

$ErrorActionPreference = "Stop"
$Root      = Split-Path -Parent $PSScriptRoot
$PfxPath   = "$Root\QuickView.pfx"
$CerPath   = "$Root\QuickView.cer"
$PfxPassword = "QuickView2026!"   # keep in sync with QuickView.csproj
$Subject     = "CN=QuickView"

function Write-OK([string]$m)   { Write-Host "  OK  $m" -ForegroundColor Green }
function Write-Fail([string]$m) { Write-Host "  !!  $m" -ForegroundColor Red }
function Write-Info([string]$m) { Write-Host "      $m" -ForegroundColor Gray }

# ── 1. Certificate ────────────────────────────────────────────────────────────
$cert = Get-ChildItem "Cert:\CurrentUser\My" |
    Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending | Select-Object -First 1

if ($RecreateCert -or -not $cert) {
    Write-Host "Creating self-signed certificate $Subject ..."
    $cert = New-SelfSignedCertificate -Type Custom `
        -Subject $Subject `
        -KeyUsage DigitalSignature `
        -FriendlyName "QuickView Signing Cert" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(3) `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    Write-OK "Created certificate $($cert.Thumbprint)"
} else {
    Write-OK "Reusing certificate $($cert.Thumbprint)"
}

# ── 2. Export PFX (private, for signing) and CER (public, for trusting) ──────
$pw = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $pw | Out-Null
Export-Certificate  -Cert $cert -FilePath $CerPath | Out-Null
Write-OK "Exported $PfxPath (keep private) and $CerPath (share this)"

# ── 3. Locate the MSIX ────────────────────────────────────────────────────────
if (-not $Path) {
    $candidates = @(
        "$Root\installer\output",
        "$Root\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64",
        "$Root\bin\Release\net8.0-windows10.0.19041.0\win-x64",
        "$Root\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64",
        "$Root\bin\Debug\net8.0-windows10.0.19041.0\win-x64"
    )
    $msix = $candidates |
        ForEach-Object { Get-ChildItem $_ -Filter *.msix -ErrorAction SilentlyContinue } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $msix) { Write-Fail "No .msix found. Build first: dotnet build -c Release -p:Platform=x64"; exit 1 }
    $Path = $msix.FullName
}
Write-Info "Package: $Path"

# ── 4. Sign with signtool ─────────────────────────────────────────────────────
$kits = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d+\.' } | Sort-Object Name -Descending
if (-not $kits) { Write-Fail "Windows SDK signtool not found. Install the Windows SDK."; exit 1 }

$signed = $false
foreach ($kit in $kits) {
    $signtool = Join-Path $kit.FullName "x64\signtool.exe"
    if (-not (Test-Path $signtool)) { continue }
    & $signtool sign /fd SHA256 /f $PfxPath /p $PfxPassword $Path
    if ($LASTEXITCODE -eq 0) {
        Write-OK "Signed with $signtool"
        & $signtool verify /pa /v $Path
        $signed = $true
        break
    }
}
if (-not $signed) { Write-Fail "Signing failed"; exit 1 }

# ── 5. Trust the certificate ──────────────────────────────────────────────────
if ($Trust) {
    $already = Get-ChildItem "Cert:\LocalMachine\TrustedPeople" -ErrorAction SilentlyContinue |
        Where-Object Thumbprint -eq $cert.Thumbprint
    if ($already) {
        Write-OK "Certificate already trusted"
    } else {
        Import-Certificate -FilePath $CerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
        Write-OK "Imported CER into LocalMachine\TrustedPeople"
    }
    Write-Info "You can now double-click the .msix to install."
} else {
    Write-Info "Not trusting yet. Run as Administrator:"
    Write-Info "  Import-Certificate -FilePath `"$CerPath`" -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'"
}
