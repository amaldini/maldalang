# Runs Tier 0 backend matrix and writes parity artifacts (JSON + Markdown).
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
Push-Location $RepoRoot
try {
    if ([string]::IsNullOrWhiteSpace($OutputDir)) {
        $OutputDir = Join-Path $RepoRoot "artifacts\tier0"
    }

    $manifest = Get-Content "conformance\tier0\manifest.json" -Raw | ConvertFrom-Json
    $csharpOn = @($manifest.cases | Where-Object { $_.backends.csharp }).Count
    $csharpOff = @($manifest.cases | Where-Object { -not $_.backends.csharp }).Count
    Write-Host "Tier 0 manifest: $($manifest.caseCount) cases (C# enabled: $csharpOn, skipped: $csharpOff)"
    Write-Host "Parity artifacts -> $OutputDir"
    Write-Host ""

    $env:TIER0_PARITY_OUT = $OutputDir
    dotnet test MaldaLang.Tests --filter "FullyQualifiedName~Tier0BackendMatrixTests" --logger "console;verbosity=normal" --no-restore
    $exit = $LASTEXITCODE
    Remove-Item Env:\TIER0_PARITY_OUT -ErrorAction SilentlyContinue

    if ($exit -eq 0) {
        Write-Host ""
        Write-Host "Wrote:"
        Write-Host "  $(Join-Path $OutputDir 'parity-report.json')"
        Write-Host "  $(Join-Path $OutputDir 'parity-report.md')"
    }

    exit $exit
}
finally {
    Pop-Location
}
