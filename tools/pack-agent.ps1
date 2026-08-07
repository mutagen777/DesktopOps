#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes DesktopOps.Agent and packs a Velopack installer/portable release.

.EXAMPLE
  .\tools\pack-agent.ps1 -Version 0.3.0
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $Configuration = "Release",

    [string] $Runtime = "win-x64",

    [string] $PublishDir = "",

    [string] $OutputDir = "",

    [string] $PackId = "DesktopOps.Agent",

    [string] $PackTitle = "DesktopOps Agent"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $PublishDir) {
    $PublishDir = Join-Path $root "artifacts\agent-publish"
}
if (-not $OutputDir) {
    $OutputDir = Join-Path $root "artifacts\agent-releases"
}

$project = Join-Path $root "src\DesktopOps.Agent\DesktopOps.Agent.csproj"

Write-Host "Publishing Agent $Version -> $PublishDir"
dotnet publish $project -c $Configuration -r $Runtime --self-contained false -o $PublishDir /p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed"
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

Write-Host "Packing Velopack release..."
vpk pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir $PublishDir `
    --mainExe "DesktopOps.Agent.exe" `
    --packTitle $PackTitle `
    --outputDir $OutputDir `
    --framework net8.0-x64-desktop

if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed"
}

Write-Host "Done. Releases in: $OutputDir"
Write-Host "Install with Setup.exe from that folder (or distribute the portable package)."
