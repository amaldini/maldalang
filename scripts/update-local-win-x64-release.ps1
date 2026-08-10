#requires -Version 5.1
<#
.SYNOPSIS
  Update a local Windows x64 MALDA install from the latest GitHub Release.

.DESCRIPTION
  Downloads malda-<version>-win-x64.zip from amaldini/maldalang (or -Repo) and
  merges it into -Destination. Extra local files that are not in the zip
  (for example a custom run-*.bat) are left in place. The bin/ tree from the
  previous install is replaced so stale publish files do not linger.

  Writes Destination\.malda-release with the installed tag for skip-if-current.

.PARAMETER Destination
  Folder to update. Default order:
    1) this parameter when set
    2) $env:MALDA_HOME when set
    3) parent of this script's folder (…\scripts\..), e.g. C:\malda when the
       script lives at C:\malda\scripts\update-local-win-x64-release.ps1

.PARAMETER Repo
  GitHub owner/name (default: amaldini/maldalang).

.PARAMETER Tag
  Install a specific release tag (e.g. v0.1.41). Default: latest non-draft release.

.PARAMETER Force
  Reinstall even when Destination\.malda-release already matches the target tag.

.PARAMETER KeepZip
  Keep the downloaded zip under Destination\.cache (default: delete after extract).

.PARAMETER WhatIf
  Show what would be installed without changing Destination.
#>
param(
    [string]$Destination = "",
    [string]$Repo = "amaldini/maldalang",
    [string]$Tag = "",
    [switch]$Force,
    [switch]$KeepZip,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Destination)) {
    if (-not [string]::IsNullOrWhiteSpace($env:MALDA_HOME)) {
        $Destination = $env:MALDA_HOME.Trim()
    }
    else {
        # Prefer the install root when this script sits in <install>\scripts\.
        $Destination = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    }
}

$Destination = [System.IO.Path]::GetFullPath($Destination)
$markerName = ".malda-release"
$userAgent = "malda-update-local-win-x64"

function Write-Step([string]$Message) {
    Write-Host $Message -ForegroundColor Cyan
}

function Get-GitHubJson {
    param(
        [Parameter(Mandatory = $true)][string]$Uri
    )
    $headers = @{
        "User-Agent" = $userAgent
        "Accept"     = "application/vnd.github+json"
    }
    if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
        $headers["Authorization"] = "Bearer $($env:GH_TOKEN)"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        $headers["Authorization"] = "Bearer $($env:GITHUB_TOKEN)"
    }
    return Invoke-RestMethod -Uri $Uri -Headers $headers
}

function Get-ReleasePayload {
    param(
        [string]$Repository,
        [string]$ReleaseTag
    )
    if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
        return Get-GitHubJson -Uri "https://api.github.com/repos/$Repository/releases/latest"
    }
    $normalized = $ReleaseTag.Trim()
    if ($normalized -notmatch '^v') {
        $normalized = "v$normalized"
    }
    return Get-GitHubJson -Uri "https://api.github.com/repos/$Repository/releases/tags/$normalized"
}

function Find-WinX64Asset {
    param($Release)
    $asset = @($Release.assets) |
        Where-Object { $_.name -match '(?i)malda-.*-win-x64\.zip$' } |
        Select-Object -First 1
    if ($null -eq $asset) {
        $names = (@($Release.assets) | ForEach-Object { $_.name }) -join ", "
        throw "Release $($Release.tag_name) has no malda-*-win-x64.zip asset. Assets: $names"
    }
    return $asset
}

function Resolve-ExtractedRoot {
    param([string]$ExtractDir)
    $children = @(Get-ChildItem -LiteralPath $ExtractDir -Force)
    if ($children.Count -eq 1 -and $children[0].PSIsContainer) {
        $candidate = $children[0].FullName
        $exe = Join-Path $candidate "bin\malda\malda.exe"
        if (Test-Path -LiteralPath $exe) {
            return $candidate
        }
    }
    $exeAtRoot = Join-Path $ExtractDir "bin\malda\malda.exe"
    if (Test-Path -LiteralPath $exeAtRoot) {
        return $ExtractDir
    }
    throw "Extracted zip does not look like a MALDA win-x64 distribution (missing bin\malda\malda.exe)."
}

function Copy-TreeMerge {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Dest
    )
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        $target = Join-Path $Dest $_.Name
        if ($_.PSIsContainer) {
            Copy-TreeMerge -Source $_.FullName -Dest $target
        }
        else {
            $parent = Split-Path -Parent $target
            if (-not (Test-Path -LiteralPath $parent)) {
                New-Item -ItemType Directory -Force -Path $parent | Out-Null
            }
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
}

Write-Step "Querying GitHub releases for $Repo ..."
$release = Get-ReleasePayload -Repository $Repo -ReleaseTag $Tag
$tagName = [string]$release.tag_name
$asset = Find-WinX64Asset -Release $release
Write-Host "Target: $tagName  asset: $($asset.name)"

$markerPath = Join-Path $Destination $markerName
if (-not $Force -and (Test-Path -LiteralPath $markerPath)) {
    $current = (Get-Content -LiteralPath $markerPath -Raw).Trim()
    if ($current -eq $tagName) {
        Write-Host "Already at $tagName in $Destination (use -Force to reinstall)." -ForegroundColor Green
        return
    }
    Write-Host "Local marker: $current -> updating to $tagName"
}

if ($WhatIf) {
    Write-Host "WhatIf: would download $($asset.browser_download_url)"
    Write-Host "WhatIf: would update $Destination"
    return
}

$cacheDir = Join-Path $Destination ".cache"
New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
$zipPath = Join-Path $cacheDir $asset.name
$workRoot = Join-Path $cacheDir ("extract-" + [guid]::NewGuid().ToString("N"))
$stagingBinBackup = $null

try {
    Write-Step "Downloading $($asset.name) ..."
    $headers = @{ "User-Agent" = $userAgent }
    if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
        $headers["Authorization"] = "Bearer $($env:GH_TOKEN)"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        $headers["Authorization"] = "Bearer $($env:GITHUB_TOKEN)"
    }
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -Headers $headers -UseBasicParsing

    Write-Step "Extracting ..."
    New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
    Expand-Archive -LiteralPath $zipPath -DestinationPath $workRoot -Force
    $payloadRoot = Resolve-ExtractedRoot -ExtractDir $workRoot

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    # Replace bin/ wholesale so old self-contained publish leftovers disappear.
    $srcBin = Join-Path $payloadRoot "bin"
    $dstBin = Join-Path $Destination "bin"
    if (Test-Path -LiteralPath $srcBin) {
        if (Test-Path -LiteralPath $dstBin) {
            $stagingBinBackup = Join-Path $cacheDir ("bin-backup-" + [guid]::NewGuid().ToString("N"))
            Move-Item -LiteralPath $dstBin -Destination $stagingBinBackup -Force
        }
        Copy-Item -LiteralPath $srcBin -Destination $dstBin -Recurse -Force
    }

    Write-Step "Merging release files into $Destination ..."
    Get-ChildItem -LiteralPath $payloadRoot -Force | ForEach-Object {
        if ($_.Name -eq "bin") {
            return
        }
        $target = Join-Path $Destination $_.Name
        if ($_.PSIsContainer) {
            Copy-TreeMerge -Source $_.FullName -Dest $target
        }
        else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }

    Set-Content -LiteralPath $markerPath -Value ($tagName + [Environment]::NewLine) -Encoding ASCII
    Write-Host "Installed $tagName -> $Destination" -ForegroundColor Green
    Write-Host "CLI: $(Join-Path $Destination 'bin\malda\malda.exe')"
}
catch {
    if ($null -ne $stagingBinBackup -and (Test-Path -LiteralPath $stagingBinBackup)) {
        $dstBin = Join-Path $Destination "bin"
        if (Test-Path -LiteralPath $dstBin) {
            Remove-Item -LiteralPath $dstBin -Recurse -Force -ErrorAction SilentlyContinue
        }
        Move-Item -LiteralPath $stagingBinBackup -Destination $dstBin -Force
        Write-Warning "Restored previous bin/ after failure."
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $stagingBinBackup -and (Test-Path -LiteralPath $stagingBinBackup)) {
        Remove-Item -LiteralPath $stagingBinBackup -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not $KeepZip -and (Test-Path -LiteralPath $zipPath)) {
        Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
    }
}
