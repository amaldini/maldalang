# Fails if BuiltInRegistry.cs symbols differ from docs/planning/core-builtin-inventory.txt
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$registryPath = Join-Path $RepoRoot "MaldaLang\BuiltIns\BuiltInRegistry.cs"
$inventoryPath = Join-Path $RepoRoot "docs\planning\core-builtin-inventory.txt"
$generateScript = Join-Path $RepoRoot "scripts\generate-core-builtin-inventory.ps1"

if (-not (Test-Path $registryPath)) { Write-Error "Missing $registryPath"; exit 1 }
if (-not (Test-Path $inventoryPath)) { Write-Error "Missing $inventoryPath"; exit 1 }

function Get-RegistrySymbols {
    $names = [ordered]@{}
    Get-Content $registryPath | ForEach-Object {
        if ($_ -match '^\s+"([a-zA-Z][a-zA-Z0-9_]*)" or\s*$') {
            $names[$matches[1]] = $true
        }
    }
    return $names.Keys
}

function Get-InventorySymbols {
    $names = [ordered]@{}
    Get-Content $inventoryPath | ForEach-Object {
        if ($_ -match '^\s*#' -or [string]::IsNullOrWhiteSpace($_)) { return }
        if ($_ -match '^([a-zA-Z][a-zA-Z0-9_]*)\s*->') {
            $names[$matches[1]] = $true
        }
    }
    return $names.Keys
}

$registry = @(Get-RegistrySymbols | Sort-Object)
$inventory = @(Get-InventorySymbols | Sort-Object)

$onlyRegistry = Compare-Object $inventory $registry -PassThru | Where-Object { $_.SideIndicator -eq '=>' }
$onlyInventory = Compare-Object $inventory $registry -PassThru | Where-Object { $_.SideIndicator -eq '<=' }

if ($onlyRegistry.Count -gt 0 -or $onlyInventory.Count -gt 0) {
    Write-Error @"
core-builtin-inventory.txt is out of date.
  Only in BuiltInRegistry.cs: $($onlyRegistry -join ', ')
  Only in inventory file: $($onlyInventory -join ', ')
Run: powershell -File $generateScript
"@
    exit 1
}

Write-Host "OK: $($registry.Count) builtins match between BuiltInRegistry.cs and core-builtin-inventory.txt."
exit 0
