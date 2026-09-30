<#
.SYNOPSIS
  Builds the Castorice installer for Windows and a portable zip of the same build.

.DESCRIPTION
  Publishes a self-contained build and wraps it with Inno Setup. The results land in
  artifacts\Castorice-<version>-windows-<arch>-setup.exe and ...-portable.zip.

  Needs Inno Setup 6 (https://jrsoftware.org/isdl.php, or: winget install JRSoftware.InnoSetup).
  Without it only the portable zip is made. CASTORICE_VERSION sets the version, as CI
  does from a v* tag; without it the build is <VersionPrefix from Directory.Build.props>-dev.

.EXAMPLE
  build\package-windows.ps1
  build\package-windows.ps1 -Runtime win-arm64
#>
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$arch = $Runtime -replace '^win-', ''

$version = $env:CASTORICE_VERSION
if (-not $version) {
    $props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
    # A build from source, marked "-dev" like a plain dotnet build.
    $prefix = if ($props -match '<VersionPrefix>([^<]+)</VersionPrefix>') { $Matches[1] } else { '0.0.0' }
    $version = "$prefix-dev"
}

$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts "windows\$Runtime\Castorice"

Write-Host "Building Castorice $version for $Runtime"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publish | Out-Null

# Self-contained, so the PC it runs on does not need .NET installed.
dotnet publish (Join-Path $root 'src\Castorice.Desktop\Castorice.Desktop.csproj') `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:Version=$version `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$zip = Join-Path $artifacts "Castorice-$version-windows-$arch-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip
Write-Host "Portable zip: $zip"

$iscc = (Get-Command 'iscc.exe' -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

if (-not $iscc) {
    Write-Warning 'Inno Setup 6 not found, so no installer was made. Install it with: winget install JRSoftware.InnoSetup'
    exit 0
}

& $iscc `
    "/DAppVersion=$version" `
    "/DSourceDir=$publish" `
    "/DOutputDir=$artifacts" `
    "/DArch=$arch" `
    (Join-Path $PSScriptRoot 'windows\Castorice.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }

Write-Host ''
Write-Host "Done: $(Join-Path $artifacts "Castorice-$version-windows-$arch-setup.exe")"
