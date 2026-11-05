# ─────────────────────────────────────────────────────────────────────────────
#  DocGlance one-time signing setup + sign script
#
#  Creates a self-signed code-signing certificate (CN=777abhishek), exports
#  DocGlance.pfx (private, gitignored) and DocGlance.cer (public), and signs
#  the built MSIX. Self-signed certs are for local testing, not public trust.
#
#  Usage:
#    .\tools\sign-package.ps1                     # create cert (if missing) + sign
#    .\tools\sign-package.ps1 -RecreateCert       # force a new certificate
#    .\tools\sign-package.ps1 -CertificateOnly    # create/export without requiring an MSIX
#    .\tools\sign-package.ps1 -Trust              # trust on this test PC (needs admin)
#    .\tools\sign-package.ps1 -Path <msix path>   # sign a specific package
# ─────────────────────────────────────────────────────────────────────────────
param(
    [switch]$RecreateCert,
    [switch]$CertificateOnly,
    [switch]$Trust,
    [string]$Path
)

$ErrorActionPreference = "Stop"
$Root      = Split-Path -Parent $PSScriptRoot
$PfxPath   = "$Root\DocGlance.pfx"
$CerPath   = "$Root\DocGlance.cer"
$PfxPassword = "DocGlance2026!"   # keep in sync with DocGlance.csproj
$Subject     = "CN=777abhishek"

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
        -FriendlyName "DocGlance Signing Cert" `
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
Write-OK "Exported $PfxPath (keep private) and $CerPath (public certificate)"

# ── 3. Trust the certificate on this test machine ─────────────────────────────
if ($Trust) {
    foreach ($store in @("Root", "TrustedPeople", "TrustedPublisher")) {
        $storePath = "Cert:\LocalMachine\$store"
        $already = Get-ChildItem $storePath -ErrorAction SilentlyContinue |
            Where-Object Thumbprint -eq $cert.Thumbprint
        if (-not $already) {
            Import-Certificate -FilePath $CerPath -CertStoreLocation $storePath | Out-Null
            Write-OK "Imported CER into LocalMachine\$store"
        } else {
            Write-OK "Certificate already trusted in LocalMachine\$store"
        }
    }
    Write-Info "Local test machine is configured for this self-signed certificate."
    Write-Info "Do not distribute trust in this certificate to machines you do not control."
} elseif ($CertificateOnly) {
    Write-Info "For local testing, trust this certificate with .\tools\sign-package.ps1 -CertificateOnly -Trust (run as Administrator)."
}

if ($CertificateOnly) {
    Write-OK "Certificate setup complete; skipped MSIX signing."
    exit 0
}

# ── 4. Locate the MSIX ────────────────────────────────────────────────────────
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

# ── 5. Sign with signtool ─────────────────────────────────────────────────────
$kits = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d+\.' } | Sort-Object Name -Descending
if (-not $kits) { Write-Fail "Windows SDK signtool not found. Install the Windows SDK."; exit 1 }

$signed = $false
$signingScript = Join-Path $PSScriptRoot "sign-authenticode.ps1"
foreach ($kit in $kits) {
    $signtool = Join-Path $kit.FullName "x64\signtool.exe"
    if (-not (Test-Path $signtool)) { continue }
    & "$PSHOME\powershell.exe" -NoProfile -ExecutionPolicy Bypass `
        -File $signingScript -SignTool $signtool -Thumbprint $cert.Thumbprint -Path $Path
    if ($LASTEXITCODE -eq 0) {
        Write-OK "Signed with $signtool"
        & $signtool verify /pa /v $Path
        $signed = $true
        break
    }
}
if (-not $signed) { Write-Fail "Signing failed"; exit 1 }

if (-not $Trust) {
    Write-Info "Certificate is not trusted on this machine. For local testing, run as Administrator:"
    Write-Info "  .\tools\sign-package.ps1 -CertificateOnly -Trust"
    Write-Info "This self-signed certificate will still be untrusted on public users' PCs."
}
