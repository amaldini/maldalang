#requires -Version 5.1
<#
.SYNOPSIS
  Record the Desktop IDE showcase and mux scripts/showcase/showcaseTrack1.mp3.

.DESCRIPTION
  Runs scripts/record-showcase.ps1 with the local soundtrack
  scripts/showcase/showcaseTrack1.mp3. That file is gitignored. The reel
  clock is scripts/showcase/playlist.json. The mp4 is written to
  artifacts/showcase/malda-showcase.mp4. The picture is trimmed to whichever
  of the reel and the track is shorter.

.PARAMETER SkipBuild
  Launch the existing Debug build instead of compiling the Desktop IDE first.
#>
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$audio = Join-Path $repoRoot "scripts\showcase\showcaseTrack1.mp3"
if (-not (Test-Path -LiteralPath $audio)) {
    throw "Soundtrack was not found: $audio"
}

$recorder = Join-Path $PSScriptRoot "record-showcase.ps1"
$recorderArgs = @{
    Audio = $audio
}
if ($SkipBuild) {
    $recorderArgs.SkipBuild = $true
}

& $recorder @recorderArgs
if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
