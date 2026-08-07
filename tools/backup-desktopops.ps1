#Requires -Version 5.1
<#
.SYNOPSIS
  Backs up DesktopOps package storage and database (SQLite file and/or SQL Server).

.EXAMPLE
  .\backup-desktopops.ps1 -StorageRoot 'D:\DesktopOps\storage' -SqliteDbPath 'D:\DesktopOps\desktopops.db' -BackupRoot 'D:\DesktopOps\backups'

.EXAMPLE
  .\backup-desktopops.ps1 -StorageRoot 'D:\DesktopOps\storage' -BackupRoot 'D:\DesktopOps\backups' `
    -SqlServerConnectionString 'Server=sql;Database=DesktopOps;Trusted_Connection=True;TrustServerCertificate=True'
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $StorageRoot,

    [Parameter(Mandatory = $true)]
    [string] $BackupRoot,

    [string] $SqliteDbPath = "",

    [string] $SqlServerConnectionString = "",

    [string] $SqlPackagePath = ""
)

$ErrorActionPreference = "Stop"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$target = Join-Path $BackupRoot $stamp
New-Item -ItemType Directory -Force -Path $target | Out-Null

if (-not (Test-Path -LiteralPath $StorageRoot)) {
    throw "StorageRoot not found: $StorageRoot"
}

$storageBackup = Join-Path $target "storage"
Write-Host "Copying storage -> $storageBackup"
robocopy $StorageRoot $storageBackup /E /R:2 /W:2 /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "robocopy failed with exit code $LASTEXITCODE"
}

if ($SqliteDbPath) {
    if (-not (Test-Path -LiteralPath $SqliteDbPath)) {
        throw "SqliteDbPath not found: $SqliteDbPath"
    }

    $dbDest = Join-Path $target (Split-Path $SqliteDbPath -Leaf)
    Write-Host "Copying SQLite DB -> $dbDest"
    Copy-Item -LiteralPath $SqliteDbPath -Destination $dbDest -Force
}

if ($SqlServerConnectionString) {
    $bacpac = Join-Path $target "DesktopOps.bacpac"
    if (-not $SqlPackagePath) {
        $SqlPackagePath = Get-Command sqlpackage -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    }

    if (-not $SqlPackagePath) {
        Write-Warning "sqlpackage not found. Skipping SQL Server export. Install SqlPackage or pass -SqlPackagePath."
        Write-Warning "Fallback: schedule a native SQL backup of database DesktopOps."
    }
    else {
        Write-Host "Exporting SQL Server -> $bacpac"
        & $SqlPackagePath /Action:Export /SourceConnectionString:$SqlServerConnectionString /TargetFile:$bacpac
        if ($LASTEXITCODE -ne 0) {
            throw "sqlpackage export failed with exit code $LASTEXITCODE"
        }
    }
}

Write-Host "Backup complete: $target"
