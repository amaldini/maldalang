# Runs Tier 0 file-driven conformance (interpreter + C# matrix).
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [switch]$RegenerateCases
)

$ErrorActionPreference = "Stop"
Push-Location $RepoRoot
try {
    if ($RegenerateCases) {
        & "$RepoRoot\scripts\generate-tier0-cases.ps1" -RepoRoot $RepoRoot
        & "$RepoRoot\scripts\update-tier0-manifest-skips.ps1" -RepoRoot $RepoRoot
    }

    dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Tier0MaldaConformanceTests|FullyQualifiedName~Tier0BackendMatrixTests|FullyQualifiedName~Tier0ConformanceTests"
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
