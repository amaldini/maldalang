# Core Examples/ must stay free of vertical pack sample trees; also runs optional-pack registry guard.
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

Push-Location $RepoRoot
try {
    & "$RepoRoot\scripts\verify-optional-pack-registry.ps1" -RepoRoot $RepoRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet test MaldaLang.Tests --filter "FullyQualifiedName~CoreExamplesGuardTests" --no-restore
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
