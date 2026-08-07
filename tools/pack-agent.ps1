#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes DesktopOps.Agent and packs a Velopack installer/portable release.

.EXAMPLE
  .\tools\pack-agent.ps1 -Version 0.3.0

.EXAMPLE
  .\tools\pack-agent.ps1 -Version 0.3.1 -CertThumbprint "ABC123..." 
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $Configuration = "Release",

    [string] $Runtime = "win-x64",

    [string] $PublishDir = "",

    [string] $OutputDir = "",

    [string] $PackId = "DesktopOps.Agent",

    [string] $PackTitle = "DesktopOps Agent",

    [string] $CertThumbprint = "",

    [string] $TimestampUrl = "http://timestamp.digicert.com",

    [string] $SignToolPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $PublishDir) {
    $PublishDir = Join-Path $root "artifacts\agent-publish"
}
if (-not $OutputDir) {
    $OutputDir = Join-Path $root "artifacts\agent-releases"
}

function Resolve-SignTool {
    param([string] $ExplicitPath)
    if ($ExplicitPath) {
        if (-not (Test-Path -LiteralPath $ExplicitPath)) {
            throw "SignTool not found: $ExplicitPath"
        }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $cmd = Get-Command signtool -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $kits = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    )
    foreach ($kitRoot in $kits) {
        if (-not (Test-Path $kitRoot)) { continue }
        $found = Get-ChildItem -Path $kitRoot -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Select-Object -First 1
        if ($found) {
            return $found.FullName
        }
    }

    throw "signtool.exe not found. Install Windows SDK or pass -SignToolPath."
}

function Invoke-AuthenticodeSign {
    param(
        [string] $SignTool,
        [string] $FilePath,
        [string] $Thumbprint,
        [string] $Timestamp
    )

    Write-Host "Signing $FilePath"
    & $SignTool sign `
        /fd SHA256 `
        /sha1 $Thumbprint `
        /tr $Timestamp `
        /td SHA256 `
        $FilePath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for $FilePath (exit $LASTEXITCODE)"
    }
}

$project = Join-Path $root "src\DesktopOps.Agent\DesktopOps.Agent.csproj"
$signEnabled = -not [string]::IsNullOrWhiteSpace($CertThumbprint)
$signTool = $null
if ($signEnabled) {
    $signTool = Resolve-SignTool -ExplicitPath $SignToolPath
    $CertThumbprint = ($CertThumbprint -replace '\s', '').ToUpperInvariant()
}

Write-Host "Publishing Agent $Version -> $PublishDir"
dotnet publish $project -c $Configuration -r $Runtime --self-contained false -o $PublishDir /p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed"
}

if ($signEnabled) {
    Get-ChildItem -LiteralPath $PublishDir -Filter "*.exe" -File | ForEach-Object {
        Invoke-AuthenticodeSign -SignTool $signTool -FilePath $_.FullName -Thumbprint $CertThumbprint -Timestamp $TimestampUrl
    }
}

$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
    Write-Host "Installing vpk global tool..."
    dotnet tool install -g vpk
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install vpk. Run: dotnet tool install -g vpk"
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$packArgs = @(
    "pack",
    "--packId", $PackId,
    "--packVersion", $Version,
    "--packDir", $PublishDir,
    "--mainExe", "DesktopOps.Agent.exe",
    "--packTitle", $PackTitle,
    "--outputDir", $OutputDir,
    "--framework", "net8.0-x64-desktop"
)

if ($signEnabled) {
    # Velopack substitutes {{file}} for each produced binary during pack.
    $template = "& `"$signTool`" sign /fd SHA256 /sha1 $CertThumbprint /tr $TimestampUrl /td SHA256 `"{{file}}`""
    $packArgs += @("--signTemplate", $template)
}

Write-Host "Packing Velopack release..."
& vpk @packArgs
if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed"
}

if ($signEnabled) {
    Get-ChildItem -LiteralPath $OutputDir -Include "Setup.exe", "*.exe" -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match 'Setup|DesktopOps\.Agent' } |
        ForEach-Object {
            Invoke-AuthenticodeSign -SignTool $signTool -FilePath $_.FullName -Thumbprint $CertThumbprint -Timestamp $TimestampUrl
        }
}

Write-Host "Done. Releases in: $OutputDir"
if ($signEnabled) {
    Write-Host "Authenticode signing applied (thumbprint $CertThumbprint)."
}
Write-Host "Install with Setup.exe from that folder (or distribute the portable package)."
