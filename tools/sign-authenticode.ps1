param(
    [Parameter(Mandatory = $true)]
    [string]$SignTool,
    [Parameter(Mandatory = $true)]
    [string]$Thumbprint,
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $SignTool)) {
    Write-Error "SignTool was not found: $SignTool"
    exit 1
}
if (-not (Test-Path -LiteralPath $Path)) {
    Write-Error "File to sign was not found: $Path"
    exit 1
}

$timestampServers = @(
    "http://timestamp.digicert.com",
    "http://timestamp.sectigo.com",
    "http://timestamp.globalsign.com/tsa/r6advanced1"
)

foreach ($timestampServer in $timestampServers) {
    Write-Host "Signing $Path using timestamp server $timestampServer..."
    & $SignTool sign /sha1 $Thumbprint /fd SHA256 /tr $timestampServer /td SHA256 $Path
    if ($LASTEXITCODE -eq 0) {
        exit 0
    }
    Write-Warning "Timestamp server failed: $timestampServer"
}

Write-Error "Signing failed with all timestamp servers. Check network, proxy, and firewall access to the RFC 3161 timestamp services."
exit 1
