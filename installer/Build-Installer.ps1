<#
.SYNOPSIS
  Builds the Spectraplayer setup program: artifacts\installer\Spectraplayer-Setup-<version>.exe

.DESCRIPTION
  1. publishes the app self-contained for 64-bit Windows (so the target computer needs no .NET),
  2. checks that libmpv, the speech engine and SQLite are in the publish folder,
  3. optionally stages the speech model (checksum-verified) so transcripts work offline right after installing,
  4. compiles installer\Spectraplayer.iss with Inno Setup.

  Needs: the .NET 8 SDK, Inno Setup 6, and libs\libmpv\libmpv-2.dll (run tools\Get-LibMpv.ps1 once).
  The setup shows the NeuSpectra and Spectraplayer pictures from installer\images (made by tools\IconGen).

.PARAMETER ModelPath
  A copy of ggml-base.en-q5_1.bin to put in the setup. Without it (or -DownloadModel) the setup is built without the model
  and the app asks before downloading one the first time you transcribe.

.PARAMETER DownloadModel
  Download the model from Hugging Face (59.7 MB) into artifacts\model when no -ModelPath is given.

.PARAMETER SkipPublish
  Reuse the existing artifacts\publish folder.
#>
[CmdletBinding()]
param(
    [string]$ModelPath,
    [switch]$DownloadModel,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$modelName = 'ggml-base.en-q5_1.bin'
$modelSha256 = '4baf70dd0d7c4247ba2b81fafd9c01005ac77c2f9ef064e00dcf195d0e2fdd2f'
$modelSize = 59721011
$modelUrl = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/' + $modelName

$version = ([xml](Get-Content "$repo\Directory.Build.props" -Raw)).Project.PropertyGroup.Version
if (-not $version) { throw 'Could not read <Version> from Directory.Build.props.' }
Write-Host "Spectraplayer $version"

# --- 1. publish -----------------------------------------------------------------------------------------------------
$publish = Join-Path $repo 'artifacts\publish'
if (-not (Test-Path "$repo\libs\libmpv\libmpv-2.dll")) { throw 'libs\libmpv\libmpv-2.dll is missing. Run tools\Get-LibMpv.ps1 first.' }

if (-not $SkipPublish) {
    Write-Host 'Publishing (self-contained, win-x64)...'
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    & dotnet publish "$repo\src\SpectraPlayer.App" -c Release -r win-x64 --self-contained true `
        -p:DebugType=none -p:DebugSymbols=false -o $publish | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
}

# --- 2. is everything in there? ---------------------------------------------------------------------------------------
$required = 'Spectraplayer.exe', 'libmpv-2.dll', 'runtimes\win-x64\whisper.dll', 'e_sqlite3.dll', 'coreclr.dll', 'PresentationFramework.dll'
foreach ($f in $required) {
    $found = Get-ChildItem $publish -Recurse -File | Where-Object { $_.FullName.EndsWith($f, [StringComparison]::OrdinalIgnoreCase) }
    if (-not $found) { throw "The publish folder is missing $f." }
}
$bytes = (Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Publish folder OK: {0:N0} MB" -f ($bytes / 1MB))

# --- 3. the speech model (optional) -----------------------------------------------------------------------------------
$stage = Join-Path $repo 'artifacts\model'
$staged = Join-Path $stage $modelName
$hasModel = $false

# Work on a private temporary copy: a file you pass in is only ever read, never moved or deleted, and the staging
# folder is only cleared once a verified model is in hand.
$temp = Join-Path ([IO.Path]::GetTempPath()) ('spectra-model-' + [guid]::NewGuid().ToString('N'))
try {
    if ($ModelPath) {
        if (-not (Test-Path $ModelPath)) { throw "Model file not found: $ModelPath" }
        Copy-Item $ModelPath $temp
    }
    elseif ($DownloadModel) {
        Write-Host "Downloading $modelName ($([math]::Round($modelSize / 1MB, 1)) MB)..."
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest $modelUrl -OutFile $temp
    }

    if (Test-Path $temp) {
        $hash = (Get-FileHash $temp -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $modelSha256 -or (Get-Item $temp).Length -ne $modelSize) {
            throw 'The model file does not match the expected checksum and size, so it was not included.'
        }
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        New-Item -ItemType Directory -Force $stage | Out-Null
        Move-Item $temp $staged
        $hasModel = $true
        Write-Host 'Speech model verified and included.'
    }
    else {
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        Write-Host 'No speech model included (use -ModelPath or -DownloadModel to add it).'
    }
}
finally {
    if (Test-Path $temp) { Remove-Item $temp -Force -ErrorAction SilentlyContinue }
}

# --- 4. compile the setup ---------------------------------------------------------------------------------------------
$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue | ForEach-Object { $_.Source }),
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php' }

$defines = @("/DAppVersion=$version")
if ($hasModel) { $defines += '/DHasModel=1' }

Write-Host 'Compiling the setup (this takes a minute or two)...'
& $iscc @defines "$repo\installer\Spectraplayer.iss" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }

$setup = Join-Path $repo "artifacts\installer\Spectraplayer-Setup-$version.exe"
$info = Get-Item $setup
Write-Host ''
Write-Host ("Done: {0}" -f $info.FullName)
Write-Host ("Size: {0:N1} MB   SHA-256: {1}" -f ($info.Length / 1MB), (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant())
