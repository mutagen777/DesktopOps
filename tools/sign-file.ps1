#Requires -Version 5.1
<#
.SYNOPSIS
  Authenticode-signs one or more files with signtool.

.EXAMPLE
  .\tools\sign-file.ps1 -CertThumbprint ABC123 -Path .\artifacts\agent-releases\Setup.exe
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $CertThumbprint,

    [Parameter(Mandatory = $true)]
    [string[]] $Path,

    [string] $TimestampUrl = "http://timestamp.digicert.com",

    [string] $SignToolPath = ""
)

$ErrorActionPreference = "Stop"
$thumb = ($CertThumbprint -replace '\s', '').ToUpperInvariant()

function Resolve-SignTool {
    param([string] $ExplicitPath)
    if ($ExplicitPath) {
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }
    $cmd = Get-Command signtool -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $found = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Select-Object -First 1
    if (-not $found) { throw "signtool.exe not found. Install Windows SDK or pass -SignToolPath." }
    return $found.FullName
}

$signTool = Resolve-SignTool -ExplicitPath $SignToolPath
foreach ($item in $Path) {
    if (-not (Test-Path -LiteralPath $item)) {
        throw "File not found: $item"
    }
    Write-Host "Signing $item"
    & $signTool sign /fd SHA256 /sha1 $thumb /tr $TimestampUrl /td SHA256 $item
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for $item"
    }
}

Write-Host "Done."
