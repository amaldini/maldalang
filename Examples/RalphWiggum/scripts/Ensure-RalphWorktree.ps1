# Ensures a git worktree exists for Ralph Wiggum and prints the project workdir (one line, stdout).
# Usage (from repo root):
#   powershell -NoProfile -ExecutionPolicy Bypass -File Examples\RalphWiggum\scripts\Ensure-RalphWorktree.ps1 `
#     -RepoRoot . -Name snake-demo -ProjectRelPath Examples/RalphWiggum/snake-demo
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [Parameter(Mandatory = $true)]
    [string]$Name,

    [string]$ProjectRelPath = ".",

    [string]$Branch = "",

    [string]$WorktreesParent = ""
)

$ErrorActionPreference = "Stop"

$repoFull = (Resolve-Path -LiteralPath $RepoRoot).Path
Push-Location -LiteralPath $repoFull
try {
    $null = git rev-parse --is-inside-work-tree 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Not a git repository: $repoFull"
    }

    if ([string]::IsNullOrWhiteSpace($Branch)) {
        $Branch = "ralph/$Name"
    }

    if ([string]::IsNullOrWhiteSpace($WorktreesParent)) {
        $WorktreesParent = Join-Path (Split-Path -Parent $repoFull) "ralph-worktrees"
    }
    $worktreePath = Join-Path $WorktreesParent $Name
    $worktreeFull = [System.IO.Path]::GetFullPath($worktreePath)

    $projectRel = $ProjectRelPath -replace '\\', '/'
    $projectRel = $projectRel.TrimStart('/')
    $ralphWorkDir = Join-Path $worktreeFull ($projectRel -replace '/', [System.IO.Path]::DirectorySeparatorChar)

    $existing = $false
    $list = git worktree list --porcelain 2>$null
    if ($LASTEXITCODE -eq 0 -and $list) {
        $blocks = ($list -split '(?m)^worktree ')
        foreach ($block in $blocks) {
            if ([string]::IsNullOrWhiteSpace($block)) { continue }
            $firstLine = ($block -split "`n")[0].Trim()
            if ($firstLine -and ([System.IO.Path]::GetFullPath($firstLine) -eq $worktreeFull)) {
                $existing = $true
                break
            }
        }
    }

    if (-not $existing) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $worktreeFull) | Out-Null
        $branchExists = $false
        git show-ref --verify --quiet "refs/heads/$Branch" 2>$null
        if ($LASTEXITCODE -eq 0) { $branchExists = $true }

        if ($branchExists) {
            git worktree add $worktreeFull $Branch | Out-Host
        } else {
            git worktree add -b $Branch $worktreeFull | Out-Host
        }
        if ($LASTEXITCODE -ne 0) {
            throw "git worktree add failed for $worktreeFull (branch $Branch)"
        }
    }

    if (-not (Test-Path -LiteralPath $ralphWorkDir)) {
        throw "Ralph project path not found in worktree: $ralphWorkDir (ProjectRelPath=$ProjectRelPath)"
    }

    $prdPath = Join-Path $ralphWorkDir "PRD.md"
    if (-not (Test-Path -LiteralPath $prdPath)) {
        throw "PRD.md not found in worktree project dir: $ralphWorkDir"
    }

    [Console]::Out.WriteLine($ralphWorkDir)
}
finally {
    Pop-Location
}
