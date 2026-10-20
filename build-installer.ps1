# ─────────────────────────────────────────────────────────────────────────────
#  DocGlance — Package build script
#
#  Creates a Store upload package, a signed sideload package, and/or the
#  unpackaged EXE installer. Store packages use Microsoft's re-signing flow;
#  sideload packages are signed with a local cert matching manifest Publisher.
#
#  Usage:
#    .\build-installer.ps1                     (sideload MSIX + EXE installer)
#    .\build-installer.ps1 -StoreUpload        (Store-ready .msixupload only)
#    .\build-installer.ps1 -MsixOnly           (signed sideload MSIX only)
#    .\build-installer.ps1 -InstallMsix        (build, trust, replace, and install sideload MSIX)
#    .\build-installer.ps1 -InstallerOnly      (EXE installer only)
#    .\build-installer.ps1 -SkipPublish        (reuse existing publish folder)
#
#  Requirements:
#    - Windows SDK and .NET 8 SDK
#    - Sideload install trusts its self-signed root and signer on this PC (UAC prompt)
#    - Inno Setup 6 for the EXE installer (auto-installs if missing)
# ─────────────────────────────────────────────────────────────────────────────
param(
    [switch]$SkipPublish,
    [switch]$MsixOnly,
    [switch]$StoreUpload,
    [switch]$InstallMsix,
    [switch]$InstallerOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root          = $PSScriptRoot
$Project       = "$Root\DocGlance.csproj"
$PublishDir    = "$Root\publish"
$Installer     = "$Root\installer"
$IssFile       = "$Installer\DocGlance.iss"
$OutputDir     = "$Installer\output"
$rcedit        = "$Root\tools\rcedit.exe"
$Ico           = "$Root\Assets\AppIcon.ico"
$Manifest      = "$Root\Package.appxmanifest"

# ── Helpers ───────────────────────────────────────────────────────────────────
function Write-Step([string]$msg) { Write-Host "`n  $msg" -ForegroundColor Cyan }
function Write-OK  ([string]$msg) { Write-Host "  ✓  $msg" -ForegroundColor Green }
function Write-Fail([string]$msg) { Write-Host "  ✗  $msg" -ForegroundColor Red; exit 1 }
function Write-Info([string]$msg) { Write-Host "     $msg" -ForegroundColor Gray }

function Get-DocGlanceSigningCertificate {
    Get-ChildItem "Cert:\CurrentUser\My" |
        Where-Object {
            $_.Subject -eq "CN=777abhishek" -and
            $_.HasPrivateKey -and
            $_.NotAfter -gt (Get-Date) -and
            $_.EnhancedKeyUsageList.ObjectId -contains "1.3.6.1.5.5.7.3.3"
        } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

function Get-OrCreateDocGlanceSigningCertificate {
    $certificate = Get-DocGlanceSigningCertificate
    if ($certificate) { return $certificate }

    Write-Step "Creating local EXE signing certificate for CN=777abhishek…"
    New-SelfSignedCertificate -Type Custom `
        -Subject "CN=777abhishek" `
        -KeyUsage DigitalSignature `
        -FriendlyName "DocGlance EXE Development Signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(3) `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}

function Get-WindowsSdkSignTool {
    $sdkBin = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    Get-ChildItem -Path "$sdkBin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Get-PackageSigningCertificate([string]$publisher) {
    Get-ChildItem "Cert:\CurrentUser\My" |
        Where-Object {
            $_.Subject -eq $publisher -and
            $_.HasPrivateKey -and
            $_.NotAfter -gt (Get-Date) -and
            $_.EnhancedKeyUsageList.ObjectId -contains "1.3.6.1.5.5.7.3.3"
        } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

function Get-OrCreatePackageSigningCertificate([string]$publisher) {
    $certificate = Get-PackageSigningCertificate $publisher
    if ($certificate) { return $certificate }

    Write-Step "Creating local MSIX signing certificate for $publisher…"
    New-SelfSignedCertificate -Type Custom `
        -Subject $publisher `
        -KeyUsage DigitalSignature `
        -FriendlyName "DocGlance MSIX Development Signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(3) `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}

function Trust-PackageCertificateForInstall([string]$certificatePath, [string]$thumbprint) {
    $trustedRoot = Get-ChildItem "Cert:\LocalMachine\Root" -ErrorAction SilentlyContinue |
        Where-Object Thumbprint -eq $thumbprint
    $trustedPeople = Get-ChildItem "Cert:\LocalMachine\TrustedPeople" -ErrorAction SilentlyContinue |
        Where-Object Thumbprint -eq $thumbprint
    if ($trustedRoot -and $trustedPeople) { return }

    Write-Info "Windows requires the self-signed package root to be trusted on this PC."
    Write-Info "Approve the UAC prompt to add the signer to Local Machine Root and Trusted People."
    $escapedPath = $certificatePath.Replace("'", "''")
    $storeCommands = @()
    if (-not $trustedRoot) {
        $storeCommands += "Import-Certificate -FilePath '$escapedPath' -CertStoreLocation 'Cert:\LocalMachine\Root' -ErrorAction Stop | Out-Null"
    }
    if (-not $trustedPeople) {
        $storeCommands += "Import-Certificate -FilePath '$escapedPath' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' -ErrorAction Stop | Out-Null"
    }
    $trustCommand = "try { $($storeCommands -join '; '); exit 0 } catch { Write-Error 'Certificate import failed'; exit 1 }"
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($trustCommand))
    try {
        $trustProcess = Start-Process -FilePath "powershell.exe" -Verb RunAs `
            -ArgumentList @("-NoProfile", "-EncodedCommand", $encodedCommand) -Wait -PassThru
    } catch {
        Write-Fail "Could not elevate to trust the sideload certificate: $($_.Exception.Message)"
    }
    if ($trustProcess.ExitCode -ne 0) {
        Write-Fail "Could not trust the sideload root. Rerun as Administrator or import $certificatePath into Local Computer > Root and Trusted People."
    }
}

Write-Host ""
Write-Host "  ╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "  ║  DocGlance Package Builder                                 ║" -ForegroundColor Magenta
Write-Host "  ╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Magenta

if (($StoreUpload -and ($InstallMsix -or $MsixOnly -or $InstallerOnly)) -or
    ($InstallMsix -and $InstallerOnly) -or
    ($MsixOnly -and $InstallerOnly)) {
    Write-Fail "Choose one packaging mode: -StoreUpload, -InstallMsix, -MsixOnly, -InstallerOnly, or the default combined build."
}

$BuildMsix = $StoreUpload -or $InstallMsix -or $MsixOnly -or (-not $InstallerOnly)
$BuildInstaller = -not ($StoreUpload -or $InstallMsix -or $MsixOnly)

[xml]$manifestXml = Get-Content -Path $Manifest -Raw
$manifestIdentity = $manifestXml.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
if (-not $manifestIdentity) { Write-Fail "Package Identity was not found in $Manifest" }
$PackageName = $manifestIdentity.GetAttribute("Name")
$PackagePublisher = $manifestIdentity.GetAttribute("Publisher")
$PackageVersion = $manifestIdentity.GetAttribute("Version")
$publisherDisplayNode = $manifestXml.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='PublisherDisplayName']")
$PackagePublisherDisplayName = if ($publisherDisplayNode) { $publisherDisplayNode.InnerText } else { "" }
if (-not $PackageName -or -not $PackagePublisher -or $PackageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    Write-Fail "Manifest identity must define Name, Publisher, and a four-part numeric Version."
}

if ($StoreUpload) {
    if ($PackageName -ne "777abhishek.DocGlance" -or
        $PackagePublisher -ne "CN=BBE5B101-6F9F-4DD1-B36A-16425B608E84" -or
        $PackagePublisherDisplayName -ne "777abhishek") {
        Write-Fail "Manifest identity or PublisherDisplayName does not match this Store product (777abhishek.DocGlance / CN=BBE5B101-6F9F-4DD1-B36A-16425B608E84 / 777abhishek)."
    }
    Write-Info "Mode: Microsoft Store upload (.msixupload)"
} elseif ($InstallMsix) {
    Write-Info "Mode: Build and install signed sideload MSIX"
} elseif ($MsixOnly) {
    Write-Info "Mode: Signed sideload MSIX only"
} elseif ($InstallerOnly) {
    Write-Info "Mode: EXE installer only"
} else {
    Write-Info "Mode: Signed sideload MSIX + EXE installer"
}
Write-Info "Package identity: $PackageName | $PackagePublisher | $PackageVersion"

# ── 0. Build native libraries ────────────────────────────────────────────────
Write-Step "Building native Rust libraries…"
$nativeBuildScript = "$Root\native\build-native.ps1"
if (-not (Test-Path $nativeBuildScript)) {
    Write-Fail "Native build script not found: $nativeBuildScript"
}
& $nativeBuildScript
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Native library build failed (exit $LASTEXITCODE)"
}
Write-OK "Native libraries built and copied to the .NET output locations"

# ── 1. Publish (for unpackaged EXE installer) ─────────────────────────────────
if ($BuildInstaller) {
    $signingCertificate = Get-OrCreateDocGlanceSigningCertificate
    if (-not $signingCertificate -or -not $signingCertificate.HasPrivateKey) {
        Write-Fail "Could not create or find the CN=777abhishek EXE signing certificate."
    }

    $signtool = Get-WindowsSdkSignTool
    if (-not $signtool) {
        Write-Fail "Windows SDK signtool.exe not found. Install the Windows SDK."
    }

    if (-not $SkipPublish) {
        Write-Step "Publishing DocGlance for unpackaged EXE (Release, x64)…"

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
    $exePath = "$PublishDir\DocGlance.exe"
    if ((Test-Path $rcedit) -and (Test-Path $Ico) -and (Test-Path $exePath)) {
        Write-Step "Patching icon into DocGlance.exe…"
        & $rcedit $exePath --set-icon $Ico 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-OK "Icon patched" }
        else { Write-Info "rcedit warning (non-fatal)" }
    }

    if (-not (Test-Path $exePath)) {
        Write-Fail "Published app executable not found: $exePath"
    }

    Write-Step "Signing DocGlance.exe…"
    & $signtool sign /sha1 $signingCertificate.Thumbprint /fd SHA256 `
        /tr http://timestamp.digicert.com /td SHA256 $exePath
    if ($LASTEXITCODE -ne 0) { Write-Fail "Signing DocGlance.exe failed (exit $LASTEXITCODE)" }
    Write-OK "Signed DocGlance.exe as $($signingCertificate.Subject)"
    Write-Info "This is a self-signed test certificate; other PCs must explicitly trust it."
}

# ── 3. Build the requested MSIX package ────────────────────────────────────────
if ($BuildMsix) {
    $msixOutputDir = if ($StoreUpload) {
        Join-Path $OutputDir "store-upload"
    } else {
        Join-Path $OutputDir "sideload"
    }

    # Avoid accidentally returning artifacts from an older build.
    if (Test-Path $msixOutputDir) {
        Remove-Item $msixOutputDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $msixOutputDir | Out-Null

    $packageBuildMode = if ($StoreUpload) { "StoreUpload" } else { "SideloadOnly" }
    $packageSigningEnabled = if ($StoreUpload) { "false" } else { "true" }
    $packageDebugType = if ($StoreUpload) { "portable" } else { "embedded" }
    $packageCertificateThumbprint = ""
    $packageCertificate = $null
    $pdbCmfExe = $null

    if ($StoreUpload) {
        $pdbCmfExe = Get-ChildItem -Path "$env:ProgramFiles\Microsoft Visual Studio\*\*\VC\Tools\MSVC\*\bin\Hostx64\x64\mspdbcmf.exe" -File -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
        if (-not $pdbCmfExe) {
            Write-Fail "Store upload symbol generation requires Visual Studio's x64 mspdbcmf.exe. Install the MSVC C++ build tools."
        }
        Write-Info "Store symbol conversion tool: $pdbCmfExe"
    }

    if (-not $StoreUpload) {
        $packageCertificate = Get-OrCreatePackageSigningCertificate $PackagePublisher
        if (-not $packageCertificate -or -not $packageCertificate.HasPrivateKey) {
            Write-Fail "Could not create or find a signing certificate matching $PackagePublisher."
        }
        $packageCertificateThumbprint = $packageCertificate.Thumbprint
        $certificatePath = Join-Path $OutputDir "$PackageName-sideload.cer"
        if (Test-Path $certificatePath) { Remove-Item $certificatePath -Force }
        Export-Certificate -Cert $packageCertificate -FilePath $certificatePath | Out-Null
        Write-Info "Sideload signer: $($packageCertificate.Subject) ($($packageCertificate.Thumbprint))"
    } else {
        Write-Info "Store uploads are built unsigned; Partner Center signs accepted packages."
    }

    Write-Step "Building $packageBuildMode MSIX via dotnet…"
    $dotnetArgs = @(
        "build", $Project,
        "-c", "Release",
        "-p:Platform=x64",
        "-p:AppxPackageDir=$msixOutputDir\",
        "-p:UapAppxPackageBuildMode=$packageBuildMode",
        "-p:AppxBundle=Never",
        "-p:AppxPackageSigningEnabled=$packageSigningEnabled",
        "-p:PackageCertificateThumbprint=$packageCertificateThumbprint",
        "-p:DebugSymbols=true",
        "-p:DebugType=$packageDebugType",
        "-p:GenerateAppxPackageOnBuild=true",
        "--nologo",
        "-v", "minimal"
    )

    if ($StoreUpload) {
        $dotnetArgs += "-p:PdbCmfx64ExeFullPath=$pdbCmfExe"
    }

    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "dotnet build $packageBuildMode packaging failed (exit $LASTEXITCODE)"
    }

    if ($StoreUpload) {
        $uploadArtifact = Get-ChildItem -Path $msixOutputDir -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in ".msixupload", ".appxupload" } |
            Select-Object -First 1
        if (-not $uploadArtifact) {
            Write-Fail "StoreUpload build completed without an .msixupload/.appxupload artifact."
        }

        $uploadPath = Join-Path $OutputDir "${PackageName}_${PackageVersion}_x64$($uploadArtifact.Extension)"
        Copy-Item -Path $uploadArtifact.FullName -Destination $uploadPath -Force
        $sizeMb = [math]::Round((Get-Item $uploadPath).Length / 1MB, 1)
        Write-OK "Store upload artifact: $uploadPath ($sizeMb MB)"
    } else {
        $msixArtifact = Get-ChildItem -Path $msixOutputDir -Recurse -File -Filter "*.msix" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/]Dependencies[\\/]' } |
            Select-Object -First 1
        if (-not $msixArtifact) {
            Write-Fail "Sideload build completed without an installable .msix artifact."
        }

        $msixPath = Join-Path $OutputDir "${PackageName}_${PackageVersion}_x64.msix"
        Copy-Item -Path $msixArtifact.FullName -Destination $msixPath -Force
        $sizeMb = [math]::Round((Get-Item $msixPath).Length / 1MB, 1)
        Write-OK "Signed sideload package: $msixPath ($sizeMb MB)"
        Write-Info "For another PC, install $PackageName-sideload.cer into Trusted People before installing this MSIX."

        if ($InstallMsix) {
            Trust-PackageCertificateForInstall $certificatePath $packageCertificate.Thumbprint

            $dependencyPackages = Get-ChildItem -Path $msixOutputDir -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.DirectoryName -match '[\\/]Dependencies[\\/]' -and
                    $_.Extension -in ".appx", ".msix"
                }
            try {
                $installedPackages = @(Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue)
                if ($installedPackages.Count -gt 0) {
                    $runningProcesses = @(Get-Process -Name "DocGlance" -ErrorAction SilentlyContinue)
                    if ($runningProcesses.Count -gt 0) {
                        Write-Info "Stopping the running DocGlance process before replacing the package…"
                        $runningProcesses | Stop-Process -Force -ErrorAction Stop
                        $runningProcesses | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
                    }

                    foreach ($installedPackage in $installedPackages) {
                        Write-Info "Removing existing package $($installedPackage.PackageFullName)…"
                        Remove-AppxPackage -Package $installedPackage.PackageFullName -ErrorAction Stop
                    }
                    Write-OK "Removed the existing DocGlance package for the current user."
                }

                if ($dependencyPackages) {
                    Add-AppxPackage -Path $msixPath -DependencyPath @($dependencyPackages.FullName)
                } else {
                    Add-AppxPackage -Path $msixPath
                }
                Write-OK "Installed $PackageName for the current user."
            } catch {
                Write-Fail "MSIX install failed: $($_.Exception.Message). If the same-identity/different-content error remains, remove $PackageName from every Windows user account (Remove-AppxPackage -AllUsers requires an elevated PowerShell) or increment the package version. If Windows reports an untrusted package, import $certificatePath into Local Computer > Trusted People as Administrator, then rerun -InstallMsix."
            }
        }
    }
}

# ── 4. Build EXE installer (Inno Setup) ───────────────────────────────────────
if ($BuildInstaller) {
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

    # Inno Setup signs both Setup and the generated uninstaller with the same
    # certificate used for the published app executable.
    $signToolCommand = '$q' + $signtool + '$q sign /sha1 ' +
        $signingCertificate.Thumbprint +
        ' /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $f'

    # Inject version and the SDK signing command at compile time.
    & $iscc `
        "/DAppVersion=$PackageVersion" `
        "/SDocGlanceSign=$signToolCommand" `
        $IssFile

    if ($LASTEXITCODE -ne 0) { Write-Fail "iscc failed (exit $LASTEXITCODE)" }

    $exeInstaller = "$OutputDir\DocGlance-Setup-x64.exe"
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

if ($StoreUpload) {
    Write-Info "Upload this file in Partner Center: $uploadPath"
} elseif ($BuildMsix) {
    Write-Info "Sideload package: $OutputDir\${PackageName}_${PackageVersion}_x64.msix"
}

if ($BuildInstaller) {
    Write-Info "EXE installer → $OutputDir\DocGlance-Setup-x64.exe"
    Write-Info "  Use for: GitHub releases, direct distribution"
}

Write-Host ""
