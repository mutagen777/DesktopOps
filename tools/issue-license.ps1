<#
.SYNOPSIS
  Issues a signed DesktopOps seat license, or generates a new RSA key pair.

.EXAMPLE
  .\tools\issue-license.ps1 -Tier team -MaxSeats 25 -Customer "Contoso" -OutFile .\license.json

.EXAMPLE
  .\tools\issue-license.ps1 -InitKeys
#>
[CmdletBinding()]
param(
    [ValidateSet("community", "team", "enterprise")]
    [string] $Tier = "team",

    [int] $MaxSeats = -1,

    [string] $Customer = "Customer",

    [string] $ValidUntil = "",

    [string] $OutFile = ".\license.lic.json",

    [string] $PrivateKeyPath = "",

    [switch] $InitKeys
)

$ErrorActionPreference = "Stop"

if (-not $PSBoundParameters.ContainsKey('MaxSeats')) {
    if ($Tier -eq 'community') { $MaxSeats = 3 }
    elseif ($Tier -eq 'team') { $MaxSeats = 25 }
    else { $MaxSeats = -1 }
}
$toolsDir = Join-Path $PSScriptRoot "licensing"
New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null

$privPath = if ($PrivateKeyPath) { $PrivateKeyPath } else { Join-Path $toolsDir "dev-private.pem" }
$pubPath = Join-Path $toolsDir "dev-public.pem"
$repoRoot = Split-Path $PSScriptRoot -Parent

function New-TempProject {
    param([string] $ProgramCs, [bool] $ReferenceLicensing)
    $tmp = Join-Path $env:TEMP ("do-lic-" + [guid]::NewGuid().ToString("n"))
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    Set-Content -Path (Join-Path $tmp "Program.cs") -Value $ProgramCs -Encoding UTF8
    if ($ReferenceLicensing) {
        $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$($repoRoot.Replace('\','\\'))\\src\\DesktopOps.Licensing\\DesktopOps.Licensing.csproj" />
  </ItemGroup>
</Project>
"@
    }
    else {
        $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
"@
    }
    Set-Content -Path (Join-Path $tmp "tool.csproj") -Value $csproj -Encoding UTF8
    return $tmp
}

if ($InitKeys) {
    $privEsc = $privPath.Replace("\", "\\")
    $pubEsc = $pubPath.Replace("\", "\\")
    $gen = @"
using System.Security.Cryptography;
using var rsa = RSA.Create(2048);
await File.WriteAllTextAsync("$privEsc", rsa.ExportRSAPrivateKeyPem());
await File.WriteAllTextAsync("$pubEsc", rsa.ExportRSAPublicKeyPem());
Console.WriteLine("Wrote private: $privEsc");
Console.WriteLine("Wrote public:  $pubEsc");
Console.WriteLine();
Console.WriteLine(rsa.ExportRSAPublicKeyPem());
"@
    $tmp = New-TempProject -ProgramCs $gen -ReferenceLicensing:$false
    & dotnet run --project (Join-Path $tmp "tool.csproj") -p:RunAnalyzers=false -v q
    if ($LASTEXITCODE -ne 0) { throw "Key generation failed." }
    Write-Host "Paste the public PEM into src/DesktopOps.Licensing/LicensingPublicKeys.cs before shipping."
    exit 0
}

if (-not (Test-Path -LiteralPath $privPath)) {
    throw "Private key not found: $privPath. Run with -InitKeys or pass -PrivateKeyPath."
}

$outFull = if ([System.IO.Path]::IsPathRooted($OutFile)) {
    $OutFile
} else {
    Join-Path (Get-Location).Path $OutFile
}

$privEsc = $privPath.Replace("\", "\\").Replace('"', '\"')
$outEsc = $outFull.Replace("\", "\\").Replace('"', '\"')
$customerEsc = $Customer.Replace("\", "\\").Replace('"', '\"')
$tierEsc = $Tier.Replace('"', '\"')
$untilExpr = if ([string]::IsNullOrWhiteSpace($ValidUntil)) {
    "null"
} else {
    "DateTimeOffset.Parse(`"$($ValidUntil.Replace('"','\"'))`", null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal)"
}

$issue = @"
using DesktopOps.Licensing;
var priv = await File.ReadAllTextAsync("$privEsc");
DateTimeOffset? until = $untilExpr;
var claims = new LicenseClaims
{
    LicenseId = Guid.NewGuid(),
    Tier = "$tierEsc",
    MaxSeats = $MaxSeats,
    Customer = "$customerEsc",
    ValidUntilUtc = until
};
var doc = LicenseCrypto.Sign(claims, priv);
var json = LicenseCrypto.SerializeDocument(doc);
var outPath = Path.GetFullPath("$outEsc");
var dir = Path.GetDirectoryName(outPath);
if (!string.IsNullOrEmpty(dir))
{
    Directory.CreateDirectory(dir);
}
await File.WriteAllTextAsync(outPath, json);
Console.WriteLine(outPath);
"@

$tmp = New-TempProject -ProgramCs $issue -ReferenceLicensing:$true
& dotnet run --project (Join-Path $tmp "tool.csproj") -p:RunAnalyzers=false -v q
if ($LASTEXITCODE -ne 0) { throw "License issue failed." }
Write-Host "Wrote $outFull"
