<#
.SYNOPSIS
  Downloads a 64-bit libmpv build and puts libmpv-2.dll into libs\libmpv (the app copies it next to the exe on build).

.DESCRIPTION
  Source: GitHub releases of shinchiro/mpv-winbuild-cmake (the "mpv-dev-x86_64-*.7z" asset, which contains libmpv-2.dll).
  Extracting .7z needs 7-Zip (7z.exe on PATH or in Program Files). Nothing is installed system-wide.
  Alternative: download any 64-bit libmpv yourself and drop libmpv-2.dll into libs\libmpv.
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\libs\libmpv')
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Destination | Out-Null
$Destination = (Resolve-Path $Destination).Path

$ProgressPreference = 'SilentlyContinue'  # Windows PowerShell 5.1 downloads are very slow with the progress bar on
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$sevenZip = $null
$cmd = Get-Command 7z -ErrorAction SilentlyContinue
if ($cmd) { $sevenZip = $cmd.Source }
if (-not $sevenZip) {
    $sevenZip = @("$env:ProgramFiles\7-Zip\7z.exe", "${env:ProgramFiles(x86)}\7-Zip\7z.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $sevenZip) { throw "7-Zip not found. Install it (https://www.7-zip.org) or place libmpv-2.dll in '$Destination' manually." }

Write-Host 'Looking up the latest libmpv build...'
$release = Invoke-RestMethod 'https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest' -Headers @{ 'User-Agent' = 'spectraplayer-setup' }
$asset = $release.assets | Where-Object { $_.name -match '^mpv-dev-x86_64-\d{8}.*\.7z$' } | Select-Object -First 1
if (-not $asset) { throw 'Could not find an mpv-dev-x86_64 asset in the latest release.' }

$work = Join-Path ([IO.Path]::GetTempPath()) ("spectra-libmpv-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
try {
    $archive = Join-Path $work $asset.name
    Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB)..."
    Invoke-WebRequest $asset.browser_download_url -OutFile $archive

    $extracted = Join-Path $work 'x'
    & $sevenZip x $archive "-o$extracted" -y | Out-Null

    $dll = Get-ChildItem $extracted -Recurse -Filter '*mpv*.dll' | Select-Object -First 1
    if (-not $dll) { throw 'No libmpv DLL found inside the archive.' }

    Copy-Item $dll.FullName (Join-Path $Destination 'libmpv-2.dll') -Force
    Write-Host "Installed $($dll.Name) -> $Destination\libmpv-2.dll"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
