# Rebuilds conformance/tier0/manifest.json from cases/*.malda and skip rules.
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
$casesDir = Join-Path $RepoRoot "conformance\tier0\cases"
$manifestPath = Join-Path $RepoRoot "conformance\tier0\manifest.json"

# Files listed here run interpreter-only until C# parity is restored.
$csharpSkip = @{}

$specByFile = @{
    "actor-send-order.malda" = "13"
    "all-array.malda" = "11"
    "all-variadic.malda" = "11"
    "async-await.malda" = "11"
    "await-literal-task.malda" = "11"
    "match-literal.malda" = "9"
    "dict-missing-null.malda" = "5.3"
    "typeof-int.malda" = "4.3"
    "typeof-dict.malda" = "4.3"
    "typeof-bool.malda" = "4.3"
    "typeof-variant.malda" = "8"
    "typeof-task.malda" = "11"
    "typeof-string.malda" = "4.3"
    "typeof-float.malda" = "4.3"
    "typeof-null.malda" = "4.3"
    "typeof-array.malda" = "4.3"
    "is-tag-legacy.malda" = "4.3"
    "is-tag-bool-legacy.malda" = "4.3"
    "is-tag-dict-legacy.malda" = "4.3"
    "sum-type-match.malda" = "8-9"
    "sum-ok-payload-field.malda" = "8-9"
    "variant-err-message.malda" = "8-9"
    "is-number.malda" = "10"
    "run-property-stable.malda" = "15"
    "match-object-simple.malda" = "9"
    "match-object-shorthand.malda" = "9"
    "match-object-nested.malda" = "9"
    "match-object-missing-prop.malda" = "9"
    "match-no-default-error.malda" = "9"
    "match-statement-body.malda" = "9"
    "match-identifier-first.malda" = "9"
    "match-nested-array-object.malda" = "9"
    "match-block-expression.malda" = "9"
    "match-block-null.malda" = "9"
    "match-default-block.malda" = "9"
    "match-guard.malda" = "9"
    "sum-type-divide-ok.malda" = "8-9"
    "sum-type-none-constructor.malda" = "8"
    "catch-io-filter.malda" = "12"
    "catch-fallback-generic.malda" = "12"
    "catch-plain-string.malda" = "12"
    "catch-rethrow-nested.malda" = "12"
    "try-catch-string.malda" = "12"
    "pipe-sort.malda" = "18"
    "list-comprehension-filter.malda" = "18"
    "const-read.malda" = "18"
    "defer-lifo.malda" = "18"
    "using-dispose.malda" = "18"
    "dict-comprehension-map.malda" = "18"
    "option-some-map.malda" = "4.4"
    "option-unwrap-none.malda" = "4.4"
    "result-err-unwrapor.malda" = "4.4"
    "result-is-err-true.malda" = "4.4"
    "result-map-unwrap.malda" = "4.4"
}

$jsReason = "JavaScript Tier 0 backend is not part of CI; see docs/spec/tier0-backend-matrix.md"
# JS Tier 0 pilot: cases verified via Tier0JavaScriptPilotProbeTests (MALDA_JS_PROBE=1).
$jsPilot = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
@(
    "all-array.malda"
    "all-variadic.malda"
    "and-or-bool.malda"
    "arithmetic-print.malda"
    "array-append-length.malda"
    "array-index-access.malda"
    "array-length.malda"
    "async-await.malda"
    "await-literal-task.malda"
    "break-while.malda"
    "catch-fallback-generic.malda"
    "catch-io-filter.malda"
    "catch-plain-string.malda"
    "catch-rethrow-nested.malda"
    "compare-null.malda"
    "comparison-operators.malda"
    "const-read.malda"
    "continue-while.malda"
    "dict-bracket-access.malda"
    "dict-dot-assign.malda"
    "dict-keys-dot.malda"
    "dict-missing-null.malda"
    "empty-array-length.malda"
    "equality-primitives.malda"
    "fibonacci-small.malda"
    "float-literal-print.malda"
    "for-continue-skip.malda"
    "for-loop-count.malda"
    "foreach-sum.malda"
    "function-return.malda"
    "greater-equals-int.malda"
    "if-else-chain.malda"
    "is-number.malda"
    "is-tag-bool-legacy.malda"
    "is-tag-dict-legacy.malda"
    "is-tag-legacy.malda"
    "is-tag-rejects-wrong.malda"
    "logical-not.malda"
    "match-array-bind-sum.malda"
    "match-array-exact.malda"
    "match-array-rest.malda"
    "match-array-two.malda"
    "match-block-expression.malda"
    "match-block-null.malda"
    "match-bool-literal.malda"
    "match-default-block.malda"
    "match-default-fallback.malda"
    "match-first-branch.malda"
    "match-guard.malda"
    "match-identifier-bind.malda"
    "match-identifier-first.malda"
    "match-literal.malda"
    "match-no-default-error.malda"
    "match-nested-array-object.malda"
    "match-null-literal.malda"
    "match-object-missing-prop.malda"
    "match-object-nested.malda"
    "match-object-shorthand.malda"
    "match-object-simple.malda"
    "match-statement-body.malda"
    "match-string-literal.malda"
    "match-wildcard.malda"
    "modulo-op.malda"
    "nested-match.malda"
    "null-conditional-index.malda"
    "null-conditional-member.malda"
    "null-conditional-present.malda"
    "operator-precedence.malda"
    "string-concat.malda"
    "string-length-builtin.malda"
    "string-not-equal.malda"
    "sum-ok-payload-field.malda"
    "sum-type-divide-ok.malda"
    "sum-type-err-branch.malda"
    "sum-type-match.malda"
    "sum-type-none-constructor.malda"
    "ternary-true-branch.malda"
    "try-catch-string.malda"
    "typeof-array.malda"
    "typeof-bool.malda"
    "typeof-dict.malda"
    "typeof-float.malda"
    "typeof-int.malda"
    "typeof-null.malda"
    "typeof-string.malda"
    "typeof-task.malda"
    "typeof-variant.malda"
    "unary-minus.malda"
    "variant-err-message.malda"
    "while-loop-count.malda"
    "actor-send-order.malda"
    "defer-lifo.malda"
    "dict-comprehension-map.malda"
    "list-comprehension-filter.malda"
    "option-some-map.malda"
    "option-unwrap-none.malda"
    "pipe-sort.malda"
    "result-err-unwrapor.malda"
    "result-is-err-true.malda"
    "result-map-unwrap.malda"
    "run-property-stable.malda"
    "using-dispose.malda"
) | ForEach-Object { [void]$jsPilot.Add($_) }
$files = Get-ChildItem $casesDir -Filter "*.malda" | Sort-Object Name
$id = 1
$cases = foreach ($f in $files) {
    $file = $f.Name
    $base = [System.IO.Path]::GetFileNameWithoutExtension($file)
    $spec = if ($specByFile.ContainsKey($file)) { $specByFile[$file] } else { "7" }
    $csharp = -not $csharpSkip.ContainsKey($file)
    $javascript = $jsPilot.Contains($file)
    $entry = [ordered]@{
        id = "T0-{0:D3}" -f $id
        file = $file
        title = ($base -replace '-', ' ')
        spec = $spec
        backends = [ordered]@{
            interpreter = $true
            csharp = $csharp
            javascript = $javascript
        }
    }
    if (-not $javascript) {
        $entry.jsSkipReason = $jsReason
    }
    if (-not $csharp) {
        $entry.csharpSkipReason = $csharpSkip[$file]
    }
    $id++
    $entry
}

$manifest = [ordered]@{
    version = 1
    caseCount = $cases.Count
    cases = $cases
}

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), $utf8)
Write-Host "manifest.json: $($cases.Count) cases"
