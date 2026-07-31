# Removes a Ralph git worktree created by Ensure-RalphWorktree.ps1 (does not delete the branch).
# Safety: refuses main repo, unregistered paths, and names outside ralph-worktrees layout.
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [Parameter(Mandatory = $true)]
    [string]$Name,

    [string]$WorktreesParent = "",

    [switch]$Force,

    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

function Get-FullPathOrThrow([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw "Path is empty."
    }
    return [System.IO.Path]::GetFullPath($path)
}

function Test-PathUnderParent([string]$childPath, [string]$parentPath) {
    $child = Get-FullPathOrThrow $childPath
    $parent = Get-FullPathOrThrow $parentPath
    if (-not $parent.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $parent += [System.IO.Path]::DirectorySeparatorChar
    }
    return $child.StartsWith($parent, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-SafeRalphWorktreeName([string]$name) {
    if ([string]::IsNullOrWhiteSpace($name)) {
        throw "Name must not be empty."
    }
    if ($name -eq "." -or $name -eq "..") {
        throw "Name '$name' is not allowed."
    }
    if ($name -match '[\\/]' -or $name -match '\.\.') {
        throw "Name must be a single path segment (letters, digits, dash, underscore only). Got: '$name'"
    }
    if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
        throw "Name must match ^[A-Za-z0-9][A-Za-z0-9._-]*$ (no spaces or special chars). Got: '$name'"
    }
}

function Get-GitWorktrees([string]$repoRoot) {
    Push-Location -LiteralPath $repoRoot
    try {
        $raw = git worktree list --porcelain 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "git worktree list failed: $raw"
        }
        $entries = @()
        $current = $null
        foreach ($line in ($raw -split "`n")) {
            if ($line -match '^worktree (.+)$') {
                if ($null -ne $current) { $entries += $current }
                $current = [PSCustomObject]@{
                    Path   = Get-FullPathOrThrow $Matches[1].Trim()
                    Branch = ""
                }
                continue
            }
            if ($null -eq $current) { continue }
            if ($line -match '^branch (.+)$') {
                $current.Branch = $Matches[1].Trim()
            }
        }
        if ($null -ne $current) { $entries += $current }
        return $entries
    }
    finally {
        Pop-Location
    }
}

Assert-SafeRalphWorktreeName -name $Name

$repoFull = (Resolve-Path -LiteralPath $RepoRoot).Path
$repoFull = Get-FullPathOrThrow $repoFull

$defaultWorktreesParent = Get-FullPathOrThrow (Join-Path (Split-Path -Parent $repoFull) "ralph-worktrees")
if ([string]::IsNullOrWhiteSpace($WorktreesParent)) {
    $WorktreesParent = $defaultWorktreesParent
} else {
    $WorktreesParent = Get-FullPathOrThrow $WorktreesParent
}

$worktreeFull = Get-FullPathOrThrow (Join-Path $WorktreesParent $Name)

# --- Path guards (before touching git remove) ---
if ($worktreeFull.Equals($repoFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove: target is the main repository root ($repoFull)."
}

if (Test-PathUnderParent -childPath $repoFull -parentPath $worktreeFull) {
    throw "Refusing to remove: target ($worktreeFull) is an ancestor of the main repository."
}

if (Test-PathUnderParent -childPath $worktreeFull -parentPath $repoFull) {
    throw "Refusing to remove: target is inside the main repository. Ralph worktrees must live outside the repo (default: ../ralph-worktrees/<name>)."
}

if (-not (Test-PathUnderParent -childPath $worktreeFull -parentPath $WorktreesParent)) {
    throw "Refusing to remove: target is not under WorktreesParent ($WorktreesParent)."
}

if (-not $WorktreesParent.EndsWith("ralph-worktrees", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove: WorktreesParent must end with 'ralph-worktrees'. Got: $WorktreesParent"
}

if (-not $WorktreesParent.Equals($defaultWorktreesParent, [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Warning "WorktreesParent differs from default ($defaultWorktreesParent). Proceeding only because path checks passed."
}

Push-Location -LiteralPath $repoFull
try {
    $mainTop = Get-FullPathOrThrow (git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "Not a git repository: $repoFull"
    }

    if ($worktreeFull.Equals($mainTop, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove: git reports this path as the main working tree ($mainTop)."
    }

    $registered = Get-GitWorktrees -repoRoot $repoFull
    $match = $registered | Where-Object { $_.Path.Equals($worktreeFull, [System.StringComparison]::OrdinalIgnoreCase) }

    if (-not $match) {
        $known = ($registered | ForEach-Object { $_.Path }) -join "`n  "
        throw @(
            "Refusing to remove: path is not a registered git worktree for this repository."
            "  Target:   $worktreeFull"
            "  Known:"
            "  $known"
        )
    }

    if ($match.Path.Equals($mainTop, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove: matched worktree is the main working tree."
    }

    $expectedBranchSuffix = "ralph/$Name"
    if ($match.Branch -and ($match.Branch -notmatch [regex]::Escape($expectedBranchSuffix))) {
        Write-Warning "Worktree branch is '$($match.Branch)' (expected something like refs/heads/$expectedBranchSuffix). Removal will still target only this linked worktree."
    }

    Write-Host "Repository:  $repoFull"
    Write-Host "Main tree:   $mainTop"
    Write-Host "Worktree:    $($match.Path)"
    if ($match.Branch) { Write-Host "Branch:      $($match.Branch)" }

    if ($WhatIf) {
        Write-Host "WhatIf: would run: git worktree remove $(if ($Force) { '-f ' })$worktreeFull"
        return
    }

    $gitArgs = @("worktree", "remove", $worktreeFull)
    if ($Force) { $gitArgs = @("worktree", "remove", "-f", $worktreeFull) }
    & git @gitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git worktree remove failed for $worktreeFull"
    }

    Write-Host "Removed worktree: $worktreeFull"
    Write-Host "Branch ralph/$Name was kept. Delete with: git branch -d ralph/$Name"
}
finally {
    Pop-Location
}
