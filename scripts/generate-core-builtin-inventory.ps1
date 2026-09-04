# Generates docs/planning/core-builtin-inventory.txt from BuiltInRegistry.cs
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$registryPath = Join-Path $RepoRoot "MaldaLang\BuiltIns\BuiltInRegistry.cs"
$outPath = Join-Path $RepoRoot "docs\planning\core-builtin-inventory.txt"

if (-not (Test-Path $registryPath)) {
    Write-Error "BuiltInRegistry not found: $registryPath"
    exit 1
}

function Get-Tier([string]$name) {
    if ($name -match '^(ui|component)') { return 'platform-ui' }
    if ($name -match '^embed') { return 'platform-embed' }
    if ($name -match 'Workflow|workflow') { return 'platform-workflow' }
    if ($name -match '^(http|webSearch|redirect|RedirectTo)') { return 'platform-web' }
    if ($name -match '^(getSkill|loadSkill|setDefaultAgent|enableAgent|setAgent|reportRalph|create.*Tool|executePlan|decomposeTask|runMALDA|compileMALDA|getSymbols|getParseErrors|checkMalda|createMcp)') { return 'platform-ai' }
    if ($name -match '^(loadNativeModule|createNativeCallback|loadAssembly|getDotNetType|dotnetNew)') { return 'core-interop' }
    if ($name -match '^(readFile|writeFile|hasFile|deleteFile|listDirectory|glob|grep|git|getEnv|path)') { return 'stdlib-io' }
    if ($name -match '^(int|float|string|abs|sum|average|max|min|pow|sqrt|sin|cos|tan|floor|ceil|round|clamp|random|typeOf|isNumber|parseJSON)') { return 'tier1-core' }
    return 'stdlib-general'
}

# A tier assigned by hand beats the heuristic above, which knows nothing about intent. Same
# contract as the notes column in sync-llm-builtins-tsv.ps1: regeneration adds and removes
# symbols, it does not silently reclassify the ones already here.
function Get-ExistingTiers {
    $tiers = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    if (-not (Test-Path $outPath)) { return $tiers }
    foreach ($line in Get-Content $outPath) {
        if ($line.StartsWith("#") -or $line.Trim().Length -eq 0) { continue }
        if ($line -match '^(?<name>\S+)\s*->\s*(?<tier>[^|]+?)\s*\|') {
            $tiers[$matches['name']] = $matches['tier']
        }
    }
    return $tiers
}

# MALDA registers built-ins whose names differ only by case (parseJSON and parseJson), so the
# symbol set needs an ordinal comparer; PowerShell's default hashtable would merge them.
$names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

$registryText = Get-Content -Raw $registryPath
$start = $registryText.IndexOf("GetDescriptor(string name)")
$end = if ($start -lt 0) { -1 } else { $registryText.IndexOf("_ => null", $start) }
if ($start -lt 0 -or $end -lt 0) {
    Write-Error "Could not locate the BuiltInRegistry.GetDescriptor switch."
    exit 1
}

# The last name in each switch arm carries the "=>" rather than a trailing "or"; matching only
# the "or" form drops one real built-in per arm.
$block = $registryText.Substring($start, $end - $start)
foreach ($match in [regex]::Matches($block, '(?m)^\s+"(?<name>[a-zA-Z][a-zA-Z0-9_]*)"\s*(?:or\s*$|=>)')) {
    [void]$names.Add($match.Groups['name'].Value)
}

$existingTiers = Get-ExistingTiers

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# Malda BuiltInRegistry inventory (Phase 0)")
[void]$sb.AppendLine("# Source: MaldaLang/BuiltIns/BuiltInRegistry.cs")
[void]$sb.AppendLine("# Regenerate: scripts/generate-core-builtin-inventory.ps1")
[void]$sb.AppendLine("# Format: symbol -> tier | note")
[void]$sb.AppendLine("# Optional-pack / vertical symbols must NOT appear here (enforced by OptionalPackRegistryGuardTests)")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("# count: $($names.Count)")
[void]$sb.AppendLine("")

foreach ($name in ($names | Sort-Object -CaseSensitive)) {
    $tier = if ($existingTiers.ContainsKey($name)) { $existingTiers[$name] } else { Get-Tier $name }
    [void]$sb.AppendLine("$name -> $tier | BuiltInRegistry")
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($outPath, $sb.ToString(), $utf8NoBom)
Write-Host "Wrote $($names.Count) symbols to $outPath"
