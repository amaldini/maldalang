<#
.SYNOPSIS
  Regenerates docs/llm/malda-builtins.tsv from the engine sources.

.DESCRIPTION
  The language pack ships a grep-able lookup table so a coding agent can confirm that a
  built-in exists, how it is spelled, and how many arguments it takes, without reading the
  HTML reference manual or a second backend runtime.

  Three of the five columns are derived from the engine, so the table cannot drift:

    name    every symbol accepted by BuiltInRegistry.GetDescriptor, the StdLibNamespaces
            module sets, and AnsiConsoleInstance.Get
    call    the preferred spelling: math./str./io. namespace, AnsiConsole method,
            <array>.method for array members, or bare
    args    the argument description the engine itself puts in its "<name>() expects ..."
            error message, from either a BuiltInArity.Require call site or a literal
            message string, anywhere under MaldaLang/

  The fourth and fifth columns are hand-written:
    notes    behaviours an agent cannot infer (and the interpreter may never complain about)
    returns  what comes back (especially null vs "" vs typed values)

  Existing notes and returns are preserved across regeneration; new symbols arrive with
  empty cells for both.

  LlmBuiltinsTsvGuardTests fails when this file is out of date.
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

$registryPath = Join-Path $RepoRoot "MaldaLang/BuiltIns/BuiltInRegistry.cs"
$namespacesPath = Join-Path $RepoRoot "MaldaLang/BuiltIns/StdLibNamespaces.cs"
$ansiPath = Join-Path $RepoRoot "MaldaLang/BuiltIns/AnsiConsoleInstance.cs"
$engineDir = Join-Path $RepoRoot "MaldaLang"
$outputPath = Join-Path $RepoRoot "docs/llm/malda-builtins.tsv"

function Get-QuotedNames([string]$text) {
    return [regex]::Matches($text, '"([A-Za-z_][A-Za-z0-9_]*)"') |
        ForEach-Object { $_.Groups[1].Value }
}

function Get-RegistryNames {
    $text = Get-Content -Raw -LiteralPath $registryPath
    $start = $text.IndexOf("GetDescriptor(string name)")
    if ($start -lt 0) { throw "Could not locate BuiltInRegistry.GetDescriptor" }
    $end = $text.IndexOf("_ => null", $start)
    if ($end -lt 0) { throw "Could not locate the end of BuiltInRegistry.GetDescriptor" }
    return Get-QuotedNames $text.Substring($start, $end - $start)
}

function Get-ModuleNames([string]$field) {
    $text = Get-Content -Raw -LiteralPath $namespacesPath
    $pattern = [regex]::Escape($field) + '\s*=\s*new HashSet<string>\([^)]*\)\s*\{(?<body>[^}]*)\}'
    $match = [regex]::Match($text, $pattern)
    if (-not $match.Success) { throw "Could not locate StdLibNamespaces.$field" }
    return Get-QuotedNames $match.Groups["body"].Value
}

function Get-AnsiConsoleNames {
    $text = Get-Content -Raw -LiteralPath $ansiPath
    $match = [regex]::Match($text, 'public override RuntimeValue Get\(.*?throw new Exception', "Singleline")
    if (-not $match.Success) { throw "Could not locate AnsiConsoleInstance.Get" }
    return [regex]::Matches($match.Value, 'name == "([A-Za-z_][A-Za-z0-9_]*)"') |
        ForEach-Object { $_.Groups[1].Value }
}

# MALDA has built-ins whose names differ only by case (parseJSON and parseJson), so every
# lookup here needs an ordinal comparer; PowerShell's default hashtable would merge them.
function New-OrdinalMap {
    return [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
}

# Mirrors BuiltInArity.DescribeArguments. LlmBuiltinsTsvGuardTests calls the real C# method,
# so the two are compared on every run and cannot drift apart silently.
function Format-Arity([int]$minimum, [int]$maximum, [string]$signature) {
    $suffix = if ($signature.Length -eq 0) { "" } else { ": ($signature)" }
    $plural = if ($minimum -eq 1) { "argument" } else { "arguments" }

    if ($maximum -eq -1) { return "at least $minimum $plural$suffix" }
    if ($minimum -ne $maximum) { return "$minimum-$maximum arguments$suffix" }
    if ($minimum -eq 0 -and $signature.Length -eq 0) { return "0 arguments" }
    return "$minimum $plural$suffix"
}

# The engine describes its own arity in the exception it throws on a bad call. Reusing that
# text keeps the column honest: change the message and regeneration picks up the new wording.
#
# Two sources, in order of preference. A BuiltInArity.Require call site is declarative and
# always names the parameters, so it wins over a literal message string, which may only state
# a count.
function Get-ArgumentDescriptions {
    $fromArity = New-OrdinalMap
    $fromMessages = New-OrdinalMap

    $sources = Get-ChildItem -LiteralPath $engineDir -Filter *.cs -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

    foreach ($file in $sources) {
        $text = Get-Content -Raw -LiteralPath $file.FullName

        $arityPattern = 'BuiltInArity\.Require\(\s*"(?<name>[A-Za-z_][A-Za-z0-9_]*)"\s*,\s*[A-Za-z_][A-Za-z0-9_]*\s*,' +
            '\s*(?<min>\d+)\s*,\s*(?<max>BuiltInArity\.Unbounded|-?\d+)\s*(?:,\s*"(?<sig>[^"]*)"\s*)?\)'
        foreach ($match in [regex]::Matches($text, $arityPattern)) {
            $maxText = $match.Groups["max"].Value
            $maximum = if ($maxText -eq "BuiltInArity.Unbounded") { -1 } else { [int]$maxText }
            $fromArity[$match.Groups["name"].Value] = Format-Arity `
                ([int]$match.Groups["min"].Value) $maximum $match.Groups["sig"].Value
        }

        foreach ($match in [regex]::Matches($text, '"(?:AnsiConsole\.)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\(\) expects (?<rest>[^"]*)"')) {
            $name = $match.Groups["name"].Value
            $rest = $match.Groups["rest"].Value.TrimEnd(".", " ")
            if ($rest.Length -eq 0) { continue }

            # Several built-ins throw more than once; keep the description that names its
            # parameters over one that only states a count.
            if (-not $fromMessages.ContainsKey($name)) {
                $fromMessages[$name] = $rest
            }
            elseif ($rest.Contains("(") -and -not $fromMessages[$name].Contains("(")) {
                $fromMessages[$name] = $rest
            }
        }
    }

    foreach ($name in $fromArity.Keys) { $fromMessages[$name] = $fromArity[$name] }
    return $fromMessages
}

function Get-ExistingHandWrittenColumn([int]$index) {
    $values = New-OrdinalMap
    if (-not (Test-Path -LiteralPath $outputPath)) { return $values }
    # Windows PowerShell 5 defaults to the system code page; force UTF-8 so hand-written
    # cells survive regeneration without mojibake.
    foreach ($line in Get-Content -LiteralPath $outputPath -Encoding UTF8) {
        if ($line.StartsWith("#") -or $line.Trim().Length -eq 0) { continue }
        $fields = $line -split "`t"
        if ($fields.Count -gt $index -and $fields[$index].Trim().Length -gt 0) {
            $values[$fields[0]] = $fields[$index]
        }
    }
    return $values
}

$registryNames = Get-RegistryNames
$mathNames = Get-ModuleNames "MathMethodNames"
$strNames = Get-ModuleNames "StrMethodNames"
$ioNames = Get-ModuleNames "IoMethodNames"
$ansiNames = Get-AnsiConsoleNames
$argumentDescriptions = Get-ArgumentDescriptions
$existingNotes = Get-ExistingHandWrittenColumn 3
$existingReturns = Get-ExistingHandWrittenColumn 4

$rows = [System.Collections.Generic.Dictionary[string, psobject]]::new([System.StringComparer]::Ordinal)

function Add-Row([string]$name, [string]$call) {
    if ($rows.ContainsKey($name)) { return }
    $arguments = ""
    if ($argumentDescriptions.ContainsKey($name)) { $arguments = $argumentDescriptions[$name] }
    $note = ""
    if ($existingNotes.ContainsKey($name)) { $note = $existingNotes[$name] }
    $returns = ""
    if ($existingReturns.ContainsKey($name)) { $returns = $existingReturns[$name] }
    $rows[$name] = [pscustomobject]@{ Name = $name; Call = $call; Args = $arguments; Notes = $note; Returns = $returns }
}

# Array mutation helpers are member-style only (items.append(x)), even though the registry
# lists them as built-in names. Spell the preferred call with a receiver so agents do not
# invent a free-function form that takes the array as an argument.
$arrayMethodNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($name in @("append", "pop", "shift")) { [void]$arrayMethodNames.Add($name) }

# Namespaced spellings win: the language server reports the flat name as a deprecated alias.
foreach ($name in $mathNames) { Add-Row $name "math.$name" }
foreach ($name in $strNames) { Add-Row $name "str.$name" }
foreach ($name in $ioNames) { Add-Row $name "io.$name" }
foreach ($name in $ansiNames) { Add-Row $name "AnsiConsole.$name" }
foreach ($name in $registryNames) {
    if ($arrayMethodNames.Contains($name)) {
        Add-Row $name "<array>.$name"
    }
    else {
        Add-Row $name $name
    }
}

$ordered = @($rows.Values) | Sort-Object -Property Name -CaseSensitive

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# MALDA built-in lookup table - generated by scripts/sync-llm-builtins-tsv.ps1")
$lines.Add("# Applies to: MALDA 1.0.8")
$lines.Add("# Columns: name<TAB>preferred call<TAB>arguments (engine wording)<TAB>notes<TAB>returns")
$lines.Add("# name, call and arguments are derived from the engine. Notes and returns are hand-written.")
$lines.Add("# An empty arguments cell means the built-in is variadic and accepts any argument count:")
$lines.Add("# all(...tasks), and the ui* component builders, which take (props?, ...children).")
foreach ($row in $ordered) {
    $lines.Add(($row.Name, $row.Call, $row.Args, $row.Notes, $row.Returns) -join "`t")
}

$content = ($lines -join "`n") + "`n"
[System.IO.File]::WriteAllText($outputPath, $content, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Wrote $($ordered.Count) built-ins to docs/llm/malda-builtins.tsv"
