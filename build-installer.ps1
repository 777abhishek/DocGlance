# ─────────────────────────────────────────────────────────────────────────────
#  QuickView — Package build script
#
#  Produces BOTH distribution formats:
#    1. MSIX package:   installer\output\QuickView_1.0.0.0_x64.msix (for Store)
#    2. EXE installer:  installer\output\QuickView-Setup-x64.exe (for GitHub)
#
#  Usage:
#    .\build-installer.ps1                     (build both MSIX + EXE installer)
#    .\build-installer.ps1 -MsixOnly           (build only MSIX package)
#    .\build-installer.ps1 -InstallerOnly      (build only EXE installer)
#    .\build-installer.ps1 -SkipPublish        (reuse existing publish folder)
#    .\build-installer.ps1 -Version 1.2.0      (override version string)
#
#  Requirements:
#    - For MSIX: Windows SDK with makeappx.exe (usually installed with Visual Studio)
#    - For EXE:  Inno Setup 6 (auto-installs if missing)
# ─────────────────────────────────────────────────────────────────────────────
param(
    [switch]$SkipPublish,
    [switch]$MsixOnly,
    [switch]$InstallerOnly,
    [string]$Version = "1.0.0.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root          = $PSScriptRoot
$Project       = "$Root\QuickView.csproj"
$PublishDir    = "$Root\publish"
$Installer     = "$Root\installer"
$IssFile       = "$Installer\QuickView.iss"
$OutputDir     = "$Installer\output"
$rcedit        = "$Root\tools\rcedit.exe"
$Ico           = "$Root\Assets\AppIcon.ico"
$Manifest      = "$Root\Package.appxmanifest"

# ── Helpers ───────────────────────────────────────────────────────────────────
function Write-Step([string]$msg) { Write-Host "`n  $msg" -ForegroundColor Cyan }
function Write-OK  ([string]$msg) { Write-Host "  ✓  $msg" -ForegroundColor Green }
function Write-Fail([string]$msg) { Write-Host "  ✗  $msg" -ForegroundColor Red; exit 1 }
function Write-Info([string]$msg) { Write-Host "     $msg" -ForegroundColor Gray }

Write-Host ""
Write-Host "  ╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "  ║  QuickView Package Builder                                 ║" -ForegroundColor Magenta
Write-Host "  ╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Magenta

if ($MsixOnly) {
    Write-Info "Mode: MSIX only (for Microsoft Store)"
} elseif ($InstallerOnly) {
    Write-Info "Mode: EXE installer only (for GitHub releases)"
} else {
    Write-Info "Mode: Both MSIX + EXE installer"
}

# ── 1. Publish (for unpackaged EXE installer) ─────────────────────────────────
if ($InstallerOnly -or (-not $MsixOnly)) {
    if (-not $SkipPublish) {
        Write-Step "Publishing QuickView for unpackaged EXE (Release, x64)…"

        if (Test-Path $PublishDir) {
            Remove-Item $PublishDir -Recurse -Force
        }

        # Clean stale Release obj to prevent duplicate-attribute errors
        $releaseObj = "$Root\obj\x64\Release"
        if (Test-Path $releaseObj) {
            Write-Info "Cleaning stale Release obj…"
            Remove-Item $releaseObj -Recurse -Force
        }

        $publishArgs = @(
            "publish", $Project,
            "-c", "Release",
            "-r", "win-x64",
            "--self-contained", "true",                  # bundle .NET 8 runtime
            "-p:Platform=x64",
            "-p:PublishSingleFile=false",                # WinUI 3 requires DLLs separate
            "-p:PublishReadyToRun=true",                 # AOT for faster startup
            "-p:DebugType=embedded",                     # embed PDB for crash reports
            "-p:DebugSymbols=true",
            "-p:GenerateAssemblyInfo=false",             # avoid duplicate attributes
            "-p:GenerateTargetFrameworkAttribute=false",
            "-p:DefineConstants=`"TRACE%3BDISABLE_XAML_GENERATED_MAIN`"",  # No PACKAGED; keep custom entry point
            "--output", $PublishDir,
            "--nologo",
            "-v", "minimal"
        )

        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet publish failed (exit $LASTEXITCODE)" }
        Write-OK "Published to $PublishDir"
    } else {
        Write-Info "Skipping publish (using existing $PublishDir)"
        if (-not (Test-Path $PublishDir)) { Write-Fail "Publish folder not found: $PublishDir" }
    }

    # ── 2. Patch exe icon ─────────────────────────────────────────────────────
    $exePath = "$PublishDir\QuickView.exe"
    if ((Test-Path $rcedit) -and (Test-Path $Ico) -and (Test-Path $exePath)) {
        Write-Step "Patching icon into QuickView.exe…"
        & $rcedit $exePath --set-icon $Ico 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-OK "Icon patched" }
        else { Write-Info "rcedit warning (non-fatal)" }
    }
}

# ── 3. Build MSIX package ─────────────────────────────────────────────────────
if ($MsixOnly -or (-not $InstallerOnly)) {
    Write-Step "Building MSIX package…"

    # Ensure Windows SDK tools are available
    $makeappx = $null
    $sdkPaths = @(
        "C:\NuGet\microsoft.windows.sdk.buildtools\*\bin\*\x64\makeappx.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe",
        "${env:ProgramFiles}\Windows Kits\10\bin\*\x64\makeappx.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools\*\bin\*\x64\makeappx.exe"
    )
    foreach ($pattern in $sdkPaths) {
        $found = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue | Select-Object -Last 1
        if ($found) {
            $makeappx = $found.FullName
            break
        }
    }

    if (-not $makeappx) {
        Write-Fail "makeappx.exe not found. Install Windows SDK via Visual Studio Installer."
    }
    Write-Info "Found makeappx: $makeappx"

    # Use dotnet build to produce the MSIX package
    Write-Info "Building MSIX via dotnet build…"

    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    $dotnetArgs = @(
        "build", $Project,
        "-c", "Release",
        "-p:Platform=x64",
        "-p:AppxPackageDir=$OutputDir\",
        "-p:UapAppxPackageBuildMode=SideloadOnly",
        "-p:AppxBundle=Never",
        "-p:GenerateAppxPackageOnBuild=true",
        "--nologo",
        "-v", "minimal"
    )

    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "dotnet build MSIX packaging failed (exit $LASTEXITCODE)"
    }

    # Find the generated MSIX
    $msixFiles = Get-ChildItem -Path $OutputDir -Filter "*.msix" -Recurse -ErrorAction SilentlyContinue
    if ($msixFiles) {
        $msix = $msixFiles | Select-Object -First 1
        # Copy to output root for easy access (skip if already there)
        $msixDest = "$OutputDir\QuickView_$Version`_x64.msix"
        if ($msix.FullName -ne $msixDest) {
            Copy-Item $msix.FullName $msixDest -Force
        }
        $sizeMb = [math]::Round((Get-Item $msixDest).Length / 1MB, 1)
        Write-OK "MSIX package: $msixDest  ($sizeMb MB)"
    } else {
        Write-Info "Note: MSIX not found in expected location. Check build output."
    }
}

# ── 4. Build EXE installer (Inno Setup) ───────────────────────────────────────
if ($InstallerOnly -or (-not $MsixOnly)) {
    Write-Step "Checking for Inno Setup 6…"

    $iscc = $null

    # Common install locations
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\iscc.exe",
        "C:\Program Files\Inno Setup 6\iscc.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $iscc = $c; break }
    }

    # Also check PATH
    if (-not $iscc) {
        $found = Get-Command iscc -ErrorAction SilentlyContinue
        if ($found) { $iscc = $found.Source }
    }

    if (-not $iscc) {
        Write-Info "Inno Setup 6 not found — downloading and installing silently…"

        $isInstaller = "$env:TEMP\innosetup6-installer.exe"
        $isUrls = @(
            "https://files.jrsoftware.org/is/6/innosetup-6.3.3.exe",
            "https://github.com/jrsoftware/issrc/releases/download/is-6_3_3/innosetup-6.3.3.exe"
        )

        $downloaded = $false
        foreach ($isUrl in $isUrls) {
            Write-Info "Downloading from $isUrl …"
            try {
                Invoke-WebRequest -Uri $isUrl -OutFile $isInstaller -UseBasicParsing
                if ((Test-Path $isInstaller) -and (Get-Item $isInstaller).Length -gt 100000) {
                    $downloaded = $true
                    break
                }
            } catch {
                Write-Info "  URL failed: $_"
            }
        }

        if (-not $downloaded) {
            Write-Fail "Could not download Inno Setup 6. Install manually from https://jrsoftware.org/isdl.php"
        }

        Write-Info "Installing Inno Setup 6 silently…"
        Start-Process $isInstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -Wait -ErrorAction Stop
        Remove-Item $isInstaller -Force -ErrorAction SilentlyContinue

        # Re-check
        foreach ($c in $candidates) {
            if (Test-Path $c) { $iscc = $c; break }
        }
        if (-not $iscc) { Write-Fail "Inno Setup installed but iscc.exe not found." }
        Write-OK "Inno Setup 6 installed"
    } else {
        Write-OK "Found iscc at: $iscc"
    }

    # ── 5. Compile the installer ──────────────────────────────────────────────
    Write-Step "Compiling EXE installer…"

    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    # Inject version into .iss at build time
    & $iscc `
        "/DAppVersion=$Version" `
        $IssFile

    if ($LASTEXITCODE -ne 0) { Write-Fail "iscc failed (exit $LASTEXITCODE)" }

    $exeInstaller = "$OutputDir\QuickView-Setup-x64.exe"
    if (-not (Test-Path $exeInstaller)) {
        Write-Fail "EXE installer not produced at: $exeInstaller"
    }

    $sizeMb = [math]::Round((Get-Item $exeInstaller).Length / 1MB, 1)
    Write-OK "EXE installer: $exeInstaller  ($sizeMb MB)"
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "  ║  Build Complete!                                           ║" -ForegroundColor Green
Write-Host "  ╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

if ($MsixOnly -or (-not $InstallerOnly)) {
    Write-Info "MSIX package → $OutputDir"
    Write-Info "  Use for: Microsoft Store submission"
}

if ($InstallerOnly -or (-not $MsixOnly)) {
    Write-Info "EXE installer → $OutputDir\QuickView-Setup-x64.exe"
    Write-Info "  Use for: GitHub releases, direct distribution"
}

Write-Host ""
