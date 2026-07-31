# Phase 2.4: fail when Parser/Lexer change without spec or grammar documentation update.
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$BaseRef = "",
    [switch]$AllowBypass
)

$ErrorActionPreference = "Stop"
Set-Location $RepoRoot

if ($env:MALDA_SKIP_SPEC_PARSER_DRIFT -eq "1") {
    if (-not $AllowBypass) {
        Write-Warning "MALDA_SKIP_SPEC_PARSER_DRIFT=1 set; skipping spec/parser drift check."
    }
    exit 0
}

$parserTriggers = @(
    "MaldaLang/Parser/Parser.cs",
    "MaldaLang/Lexer.cs"
)

$documentationTriggers = @(
    "docs/spec/CHANGELOG.md",
    "ReferenceManual/22-grammar.html",
    "docs/planning/parser-manual-drift-audit.md"
)

function Normalize-GitPath {
    param([string]$Path)
    return ($Path -replace '\\', '/').Trim()
}

function Get-ChangedFileNames {
    $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    $diffArgs = @()
    if ([string]::IsNullOrWhiteSpace($BaseRef)) {
        $mergeBase = git merge-base HEAD origin/master 2>$null
        if ($LASTEXITCODE -eq 0 -and $mergeBase) {
            $diffArgs += "$mergeBase...HEAD"
        }
        else {
            $diffArgs += "HEAD~1...HEAD"
        }
    }
    else {
        $diffArgs += "$BaseRef...HEAD"
    }

    foreach ($range in $diffArgs) {
        $out = git diff --name-only $range 2>$null
        if ($LASTEXITCODE -eq 0 -and $out) {
            foreach ($line in $out) {
                if (-not [string]::IsNullOrWhiteSpace($line)) {
                    [void]$names.Add((Normalize-GitPath $line))
                }
            }
        }
    }

    foreach ($flag in @("", "--cached")) {
        $out = git diff --name-only $flag 2>$null
        if ($LASTEXITCODE -eq 0 -and $out) {
            foreach ($line in $out) {
                if (-not [string]::IsNullOrWhiteSpace($line)) {
                    [void]$names.Add((Normalize-GitPath $line))
                }
            }
        }
    }

    return @($names)
}

function Test-SpecDocumentationPath {
    param([string]$NormalizedPath)
    if ($documentationTriggers -contains $NormalizedPath) { return $true }
    if ($NormalizedPath -like "docs/spec/malda-language-*.md") { return $true }
    return $false
}

$changed = Get-ChangedFileNames
if ($changed.Count -eq 0) {
    Write-Host "OK: no git diff detected (spec/parser drift check skipped)."
    exit 0
}

$parserTouched = $false
foreach ($file in $changed) {
    if ($parserTriggers -contains $file) {
        $parserTouched = $true
        break
    }
}

if (-not $parserTouched) {
    Write-Host "OK: Parser.cs and Lexer.cs unchanged in diff ($($changed.Count) file(s) checked)."
    exit 0
}

$docTouched = $false
foreach ($file in $changed) {
    if (Test-SpecDocumentationPath $file) {
        $docTouched = $true
        break
    }
}

if (-not $docTouched) {
    Write-Error @"
Spec/parser drift: MaldaLang/Parser/Parser.cs or MaldaLang/Lexer.cs changed without a documentation update.

Update at least one of:
  - docs/spec/malda-language-*.md (Tier 0 semantics)
  - docs/spec/CHANGELOG.md (version / deprecation note)
  - ReferenceManual/22-grammar.html (syntax BNF)
  - docs/planning/parser-manual-drift-audit.md (manual drift notes)

Changed files in diff:
$($changed -join "`n")

Run: .\scripts\verify-spec-parser-drift.ps1
"@
    exit 1
}

Write-Host "OK: parser/lexer change accompanied by spec or grammar documentation."
exit 0
