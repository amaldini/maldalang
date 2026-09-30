#requires -Version 5.1
<#
.SYNOPSIS
  Record the Desktop IDE showcase reel and optionally mux a music track.

.DESCRIPTION
  Builds the Desktop IDE, launches it with --demo, and records the window with
  ffmpeg Desktop Duplication (ddagrab) so the WebView2 preview is not black.
  The IDE waits until this script creates a start flag, then plays
  scripts/showcase/playlist.json. Edit holdMs in that file to sit cuts on a beat.

  No audio is bundled. Pass -Audio with a track you have rights to use.
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
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
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

    # SW_RESTORE, then HWND_TOPMOST without moving or resizing.
    [ShowcaseWindow]::ShowWindow($hwnd, 9) | Out-Null
    [ShowcaseWindow]::SetWindowPos($hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0043) | Out-Null
    [ShowcaseWindow]::SetForegroundWindow($hwnd) | Out-Null
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

Remove-Item -LiteralPath $startFlag -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $statusPath -ErrorAction SilentlyContinue
Remove-Item -LiteralPath ($statusPath + ".tmp") -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $rawVideo -ErrorAction SilentlyContinue

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

New-Item -ItemType File -Path $startFlag -Force | Out-Null

$playDeadline = (Get-Date).AddSeconds(45)
$playing = $false
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

$grab = "ddagrab=output_idx=0:draw_mouse=0:framerate=60:video_size=${width}x${height}:offset_x=${x}:offset_y=${y}"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $ffmpeg.Source
$psi.Arguments = "-y -hide_banner -loglevel warning -f lavfi -i $grab -vf hwdownload,format=bgra -c:v libx264 -pix_fmt yuv420p -preset veryfast -movflags +faststart `"$rawVideo`""
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

$holdMs = 0
if ($playlistObj.browse) {
    foreach ($pick in $playlistObj.browse) {
        $holdMs += [int]$pick.holdMs
    }
}
if ($playlistObj.manual) {
    foreach ($page in $playlistObj.manual) {
        $holdMs += [int]$page.holdMs
    }
}
foreach ($scene in $playlistObj.scenes) {
    $holdMs += [int]$scene.holdMs
}
$timeoutMs = $holdMs + (($playlistObj.scenes.Count * 90) * 1000) + 60000
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

if (-not [string]::IsNullOrWhiteSpace($Audio)) {
    & ffmpeg -y -hide_banner -loglevel warning -i $rawVideo -i $Audio -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 192k -shortest $output
    if ($LASTEXITCODE -ne 0) {
        throw "Could not mux audio onto the showcase reel."
    }
    Remove-Item -LiteralPath $rawVideo -ErrorAction SilentlyContinue
}
else {
    Move-Item -LiteralPath $rawVideo -Destination $output -Force
}

Write-Output $output
