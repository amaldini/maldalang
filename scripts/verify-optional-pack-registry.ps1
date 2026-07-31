# Fails if optional-pack symbols from optional-pack-builtin-inventory.txt appear in BuiltInRegistry.cs
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$inventoryPath = Join-Path $RepoRoot "docs\planning\optional-pack-builtin-inventory.txt"
$registryPath = Join-Path $RepoRoot "MaldaLang\BuiltIns\BuiltInRegistry.cs"

if (-not (Test-Path $inventoryPath)) { Write-Error "Missing $inventoryPath"; exit 1 }
if (-not (Test-Path $registryPath)) { Write-Error "Missing $registryPath"; exit 1 }

$forbidden = @()
Get-Content $inventoryPath | ForEach-Object {
    if ($_ -match '^\s*#' -or [string]::IsNullOrWhiteSpace($_)) { return }
    if ($_ -match '^([a-zA-Z][a-zA-Z0-9_]*)\s*->') { $forbidden += $matches[1] }
}

$registry = Get-Content $registryPath -Raw
$violations = @()
foreach ($symbol in $forbidden | Select-Object -Unique) {
    if ($registry -match ('"' + [regex]::Escape($symbol) + '"\s+or')) {
        $violations += $symbol
    }
}

if ($violations.Count -gt 0) {
    Write-Error "Optional-pack symbols must not be in BuiltInRegistry: $($violations -join ', ')"
    exit 1
}

Write-Host "OK: no optional-pack symbols in BuiltInRegistry ($($forbidden.Count) checked)."

Push-Location $RepoRoot
try {
    dotnet test MaldaLang.Tests --filter "FullyQualifiedName~CompilerPackDecouplingGuardTests" --no-restore
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
