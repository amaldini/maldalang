# Applies documented C# / JS skip flags (delegates to sync-tier0-manifest.ps1).
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)
& "$PSScriptRoot\sync-tier0-manifest.ps1" -RepoRoot $RepoRoot
