# Tier 0 / spec CI bundle (Phase 2.4): registry, examples, parser-spec drift, core tests.
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
Push-Location $RepoRoot
try {
    & "$RepoRoot\scripts\verify-optional-pack-registry.ps1" -RepoRoot $RepoRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & "$RepoRoot\scripts\verify-spec-parser-drift.ps1" -RepoRoot $RepoRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $parityOut = Join-Path $RepoRoot "artifacts\tier0"
    $env:TIER0_PARITY_OUT = $parityOut
    dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Spec|FullyQualifiedName~Tier0ConformanceTests|FullyQualifiedName~Tier0MaldaConformanceTests|FullyQualifiedName~Tier0BackendMatrixTests|FullyQualifiedName~Phase6EffectsTests|FullyQualifiedName~Phase7ExpressivenessTests|FullyQualifiedName~Phase72ResourceTests|FullyQualifiedName~Phase73ConstTests|FullyQualifiedName~Phase75DictComprehensionTests|FullyQualifiedName~TypedPromptValidatorTests|FullyQualifiedName~TranspiledTypedPromptTests|FullyQualifiedName~SchemaToLlmTests|FullyQualifiedName~ReferenceManualChapterSyncTests|FullyQualifiedName~ReferenceManualGrammarCoverageTests" --no-restore
    $exit = $LASTEXITCODE
    Remove-Item Env:\TIER0_PARITY_OUT -ErrorAction SilentlyContinue
    exit $exit
}
finally {
    Pop-Location
}
