#requires -Version 5.1
<#
.SYNOPSIS
  Record the Desktop IDE showcase reel and optionally mux a music track.

.DESCRIPTION
  Builds the Desktop IDE, launches it with --demo, and records the window with
  ffmpeg Desktop Duplication (ddagrab) so the WebView2 preview is not black.
  The IDE shows the splash, waits until this script creates a start flag, then
  starts the reel clock. Each cue in scripts/showcase/playlist.json has startMs,
  milliseconds from that splash. The cue stays up until the next startMs.
  endMs is when the reel ends. Edit those values to put each section on a beat.
  A paste-ready generator prompt is in scripts/showcase/song-prompt.md.
  If the generator only plays in the browser, that file also has the
  Stereo Mix ffmpeg commands that save an mp3 for -Audio.

  Recording starts while the splash is already up, before the clock. The script
  trims that lead so the mp4 starts at reel time 0. No audio is bundled. Pass
  -Audio with a track you have rights to use. The video is trimmed to whichever
  of the picture and the track is shorter.
  The mp4 is written to artifacts/showcase/malda-showcase.mp4.

.PARAMETER Audio
  Optional music file. The video is trimmed to the shorter of the two.

.PARAMETER Playlist
  Playlist JSON. Defaults to scripts/showcase/playlist.json.

.PARAMETER SkipBuild
  Launch the existing Debug build instead of compiling first.
#>
param(
    [string]$Audio = "",
    [string]$Playlist = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class ShowcaseWindow {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
"@

function Assert-ShowcaseInFront([System.Diagnostics.Process]$proc) {
    $proc.Refresh()
    $hwnd = $proc.MainWindowHandle
    if ($hwnd -eq [IntPtr]::Zero) {
        return
    }

    # SW_SHOWNA, then HWND_TOPMOST without activating, moving, or resizing.
    # Activating the IDE here steals focus from the examples dialog on every pass.
    [ShowcaseWindow]::ShowWindow($hwnd, 8) | Out-Null
    [ShowcaseWindow]::SetWindowPos($hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0013) | Out-Null
}

function Get-ShowcaseCapturedSeconds([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        return 0.0
    }

    $text = ""
    try {
        $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $reader = New-Object System.IO.StreamReader($stream)
        try {
            $text = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    catch {
        return 0.0
    }

    $frame = 0
    foreach ($line in ($text -split "`n")) {
        if ($line -match 'frame=\s*(\d+)') {
            $frame = [int]$Matches[1]
        }
    }

    return $frame / 60.0
}

$machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if (-not [string]::IsNullOrWhiteSpace($machinePath) -or -not [string]::IsNullOrWhiteSpace($userPath)) {
    $env:Path = "$machinePath;$userPath"
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($Playlist)) {
    $Playlist = Join-Path $repoRoot "scripts\showcase\playlist.json"
}
$Playlist = (Resolve-Path $Playlist).Path

if (-not [string]::IsNullOrWhiteSpace($Audio)) {
    $Audio = (Resolve-Path $Audio).Path
}

$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
if (-not $ffmpeg) {
    throw "ffmpeg is not on PATH. Install a build that includes the ddagrab filter."
}

& ffmpeg -hide_banner -h filter=ddagrab 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "This ffmpeg does not include ddagrab. Install a current ffmpeg build (Desktop Duplication) so the web preview is captured."
}

$playlistObj = Get-Content -Raw -Path $Playlist | ConvertFrom-Json
if (-not $playlistObj.scenes -or $playlistObj.scenes.Count -lt 1) {
    throw "Playlist has no scenes: $Playlist"
}
if ($null -eq $playlistObj.endMs -or [int]$playlistObj.endMs -le 0) {
    throw "Playlist needs endMs, the millisecond when the reel ends: $Playlist"
}

foreach ($scene in $playlistObj.scenes) {
    $relative = [string]$scene.file
    if ([string]::IsNullOrWhiteSpace($relative)) {
        throw "A playlist scene is missing its file."
    }
    $scenePath = Join-Path $repoRoot ($relative -replace '/', '\')
    if (-not (Test-Path -LiteralPath $scenePath)) {
        throw "Showcase scene was not found: $relative"
    }
}

if ($playlistObj.browse) {
    foreach ($pick in $playlistObj.browse) {
        $relative = [string]$pick.file
        if ([string]::IsNullOrWhiteSpace($relative)) {
            throw "A browse pick is missing its file."
        }
        $pickPath = Join-Path $repoRoot ($relative -replace '/', '\')
        if (-not (Test-Path -LiteralPath $pickPath)) {
            throw "Showcase browse file was not found: $relative"
        }
    }
}

if ($playlistObj.manual) {
    foreach ($page in $playlistObj.manual) {
        $relative = [string]$page.file
        if ([string]::IsNullOrWhiteSpace($relative)) {
            throw "A manual page is missing its file."
        }
        $pageName = [System.IO.Path]::GetFileName($relative)
        $pagePath = Join-Path $repoRoot (Join-Path "ReferenceManual" $pageName)
        if (-not (Test-Path -LiteralPath $pagePath)) {
            throw "Showcase manual page was not found: $relative"
        }
    }
}

$ideOut = Join-Path $repoRoot "artifacts\showcase-ide"
if (-not $SkipBuild) {
    & dotnet build (Join-Path $repoRoot "MaldaLang.DesktopIDE\MaldaLang.DesktopIDE.csproj") -c Debug -o $ideOut --nologo
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$ide = Join-Path $ideOut "MaldaLang.DesktopIDE.exe"
if (-not (Test-Path -LiteralPath $ide)) {
    throw "Desktop IDE executable was not found: $ide. Run without -SkipBuild."
}

$handshake = Join-Path $repoRoot "artifacts\showcase"
New-Item -ItemType Directory -Force -Path $handshake | Out-Null
$startFlag = Join-Path $handshake "start"
$statusPath = Join-Path $handshake "status.json"
$rawVideo = Join-Path $handshake "malda-showcase.raw.mp4"
$output = Join-Path $handshake "malda-showcase.mp4"
$ffmpegLog = Join-Path $handshake "ffmpeg.log"
$progressPath = Join-Path $handshake "ffmpeg-progress.txt"

Remove-Item -LiteralPath $startFlag -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $statusPath -ErrorAction SilentlyContinue
Remove-Item -LiteralPath ($statusPath + ".tmp") -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $rawVideo -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $progressPath -ErrorAction SilentlyContinue

$ideProc = $null
$ideInfo = New-Object System.Diagnostics.ProcessStartInfo
$ideInfo.FileName = $ide
$ideInfo.Arguments = "--demo `"$Playlist`" --handshake `"$handshake`""
$ideInfo.WorkingDirectory = $repoRoot
$ideInfo.UseShellExecute = $true
$ideProc = [System.Diagnostics.Process]::Start($ideInfo)
if (-not $ideProc) {
    throw "Could not start the Desktop IDE."
}

$deadline = (Get-Date).AddMinutes(3)
$status = $null
while ((Get-Date) -lt $deadline) {
    if ($ideProc.HasExited) {
        if (Test-Path -LiteralPath $statusPath) {
            $status = Get-Content -Raw -Path $statusPath | ConvertFrom-Json
            throw "Desktop IDE exited before the showcase was armed. $($status.error)"
        }
        throw "Desktop IDE exited before the showcase was armed (exit $($ideProc.ExitCode))."
    }

    if (Test-Path -LiteralPath $statusPath) {
        try {
            $status = Get-Content -Raw -Path $statusPath | ConvertFrom-Json
        }
        catch {
            Start-Sleep -Milliseconds 100
            continue
        }

        if ($status.phase -eq "error") {
            throw "Showcase failed to arm: $($status.error)"
        }
        if ($status.phase -eq "armed") {
            break
        }
    }

    Start-Sleep -Milliseconds 200
}

if (-not $status -or $status.phase -ne "armed") {
    if (-not $ideProc.HasExited) {
        $ideProc.Kill()
    }
    throw "Timed out waiting for the showcase window to arm."
}

$width = [int]$status.width
$height = [int]$status.height
$x = [int]$status.x
$y = [int]$status.y
if ($width -lt 2 -or $height -lt 2) {
    if (-not $ideProc.HasExited) {
        $ideProc.Kill()
    }
    throw "Showcase window rectangle is empty."
}
$width = $width - ($width % 2)
$height = $height - ($height % 2)

$grab = "ddagrab=output_idx=0:draw_mouse=0:framerate=60:video_size=${width}x${height}:offset_x=${x}:offset_y=${y}"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $ffmpeg.Source
$psi.Arguments = "-y -hide_banner -loglevel warning -stats_period 0.1 -f lavfi -i $grab -vf hwdownload,format=bgra -c:v libx264 -pix_fmt yuv420p -preset veryfast -movflags +faststart -progress `"$progressPath`" `"$rawVideo`""
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
$ff = [System.Diagnostics.Process]::Start($psi)
if (-not $ff) {
    if (-not $ideProc.HasExited) {
        $ideProc.Kill()
    }
    throw "Could not start ffmpeg."
}
$ffmpegErrors = $ff.StandardError.ReadToEndAsync()
Assert-ShowcaseInFront $ideProc
Start-Sleep -Milliseconds 200
Assert-ShowcaseInFront $ideProc
if ($ff.HasExited) {
    $early = ""
    try { $early = $ffmpegErrors.Result } catch { }
    if (-not $ideProc.HasExited) {
        try { $ideProc.Kill() } catch { }
    }
    throw "ffmpeg exited before the reel started. $early"
}

New-Item -ItemType File -Path $startFlag -Force | Out-Null

$playDeadline = (Get-Date).AddSeconds(45)
$playing = $false
$videoLeadSeconds = 0.0
while ((Get-Date) -lt $playDeadline) {
    if ($ideProc.HasExited) {
        $detail = ""
        if (Test-Path -LiteralPath $statusPath) {
            try {
                $failed = Get-Content -Raw -Path $statusPath | ConvertFrom-Json
                $detail = [string]$failed.error
            }
            catch { }
        }
        throw "Desktop IDE exited before the first scene. $detail"
    }

    if (Test-Path -LiteralPath $statusPath) {
        try {
            $live = Get-Content -Raw -Path $statusPath | ConvertFrom-Json
        }
        catch {
            Start-Sleep -Milliseconds 50
            continue
        }
        if ($live.phase -eq "error") {
            throw "Showcase failed before the first scene: $($live.error)"
        }
        if ($live.phase -eq "playing") {
            $playing = $true
            $videoLeadSeconds = Get-ShowcaseCapturedSeconds $progressPath
            break
        }
    }

    Start-Sleep -Milliseconds 50
}

if (-not $playing) {
    if (-not $ideProc.HasExited) {
        $ideProc.Kill()
    }
    throw "Timed out waiting for the first showcase scene."
}

Assert-ShowcaseInFront $ideProc

$timeoutMs = [int]$playlistObj.endMs + (($playlistObj.scenes.Count * 90) * 1000) + 60000
$exitDeadline = (Get-Date).AddMilliseconds($timeoutMs)
while (-not $ideProc.HasExited -and (Get-Date) -lt $exitDeadline) {
    Assert-ShowcaseInFront $ideProc
    Start-Sleep -Milliseconds 400
    $ideProc.Refresh()
}
if (-not $ideProc.HasExited) {
    try { $ideProc.Kill() } catch { }
    try {
        $ff.StandardInput.WriteLine("q")
        $ff.StandardInput.Close()
    }
    catch { }
    if (-not $ff.WaitForExit(15000)) {
        try { $ff.Kill() } catch { }
    }
    throw "Showcase reel timed out after $([int]($timeoutMs / 1000)) seconds."
}

try {
    $ff.StandardInput.WriteLine("q")
    $ff.StandardInput.Close()
}
catch { }
if (-not $ff.WaitForExit(20000)) {
    try { $ff.Kill() } catch { }
    $ff.WaitForExit(5000) | Out-Null
}

$ffmpegText = ""
try { $ffmpegText = $ffmpegErrors.Result } catch { }
if (-not [string]::IsNullOrWhiteSpace($ffmpegText)) {
    Set-Content -LiteralPath $ffmpegLog -Value $ffmpegText
}

if ($ideProc.ExitCode -ne 0) {
    $detail = ""
    if (Test-Path -LiteralPath $statusPath) {
        try {
            $failed = Get-Content -Raw -Path $statusPath | ConvertFrom-Json
            $detail = [string]$failed.error
        }
        catch { }
    }
    throw "Showcase IDE exited with code $($ideProc.ExitCode). $detail"
}

if (-not (Test-Path -LiteralPath $rawVideo)) {
    throw "ffmpeg did not write $rawVideo. $ffmpegText"
}

$leadSeconds = [double]$videoLeadSeconds
if ([string]::IsNullOrWhiteSpace($Audio) -and $leadSeconds -le 0.03) {
    Move-Item -LiteralPath $rawVideo -Destination $output -Force
}
else {
    $publishArgs = @("-y", "-hide_banner", "-loglevel", "warning", "-i", $rawVideo)
    if (-not [string]::IsNullOrWhiteSpace($Audio)) {
        $publishArgs += @("-i", $Audio)
    }
    if ($leadSeconds -gt 0.03) {
        $leadText = $leadSeconds.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture)
        $publishArgs += @("-ss", $leadText, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-preset", "veryfast", "-movflags", "+faststart")
    }
    else {
        $publishArgs += @("-c:v", "copy")
    }
    if (-not [string]::IsNullOrWhiteSpace($Audio)) {
        $publishArgs += @("-map", "0:v:0", "-map", "1:a:0", "-c:a", "aac", "-b:a", "192k", "-shortest")
    }
    $publishArgs += $output
    & ffmpeg @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Could not write the showcase reel."
    }
    Remove-Item -LiteralPath $rawVideo -ErrorAction SilentlyContinue
}

Write-Output $output
