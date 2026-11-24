<#
.SYNOPSIS
  Fetches the whisper.cpp source the Android app's transcription engine is built from (android\third_party\whisper.cpp,
  git-ignored: it is downloaded, not committed).

.DESCRIPTION
  A shallow clone of one pinned release tag, so builds are repeatable. Needs git. The Android Gradle build compiles it with
  the NDK and CMake (see app\src\main\cpp\CMakeLists.txt).
#>
[CmdletBinding()]
param([string]$Tag = 'v1.9.6')

$ErrorActionPreference = 'Stop'
$dest = Join-Path $PSScriptRoot '..\third_party\whisper.cpp'
$dest = [IO.Path]::GetFullPath($dest)

if (Test-Path (Join-Path $dest 'CMakeLists.txt')) {
    $have = git -C $dest describe --tags 2>$null
    Write-Host "whisper.cpp is already there ($have)."
    return
}

New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
Write-Host "Cloning whisper.cpp $Tag ..."
git clone --depth 1 --branch $Tag https://github.com/ggml-org/whisper.cpp.git $dest
if ($LASTEXITCODE -ne 0) { throw "git clone failed ($LASTEXITCODE)." }
Write-Host "Done: $dest"
