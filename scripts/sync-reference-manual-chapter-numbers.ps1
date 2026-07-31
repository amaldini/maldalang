# Sync Reference Manual chapter numbers from ReferenceManual/chapters.json
# Updates <title>, breadcrumbs, <main><h1>, and nav-footer prev/next labels.

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$manualDir = Join-Path $repoRoot "ReferenceManual"
$configPath = Join-Path $manualDir "chapters.json"

$config = Get-Content $configPath -Raw | ConvertFrom-Json
$ordered = @()
$num = 0
foreach ($chapter in $config.chapters) {
    if ($chapter.isHome) { continue }
    $num++
    $ordered += [pscustomobject]@{
        Num   = $num
        File  = $chapter.file
        Title = $chapter.title
        Label = "$num. $($chapter.title)"
    }
}

for ($i = 0; $i -lt $ordered.Count; $i++) {
    $ch = $ordered[$i]
    $path = Join-Path $manualDir $ch.File
    if (-not (Test-Path $path)) {
        Write-Warning "Missing file: $($ch.File)"
        continue
    }

    $html = Get-Content $path -Raw -Encoding UTF8

    $html = [regex]::Replace($html, '<title>[^<]*</title>', "<title>$($ch.Label) - MALDA Reference Manual</title>", 1)
    $html = [regex]::Replace(
        $html,
        '<header>\s*<h1>[^<]*</h1>',
        # The trademark symbol goes in as an HTML entity, not a literal character, so
        # this script stays pure ASCII and cannot mojibake the masthead on Windows
        # PowerShell 5 the way the footer arrows once did.
        "<header>`r`n        <h1>MALDA&trade; Reference Manual</h1>",
        1)
    $html = [regex]::Replace(
        $html,
        '<div class="breadcrumbs">[\s\S]*?</div>',
        "<div class=""breadcrumbs""><a href=""index.html"">Home</a> <span>/</span> <span>$($ch.Label)</span></div>",
        1)
    $html = [regex]::Replace($html, '(<main>[\s\S]*?)<h1>[^<]*</h1>', "`${1}<h1>$($ch.Label)</h1>", 1)

    if ($html -match '<div class="nav-footer">') {
        # Use codepoints (not literal ←/→) so Windows PowerShell 5 does not
        # mis-decode UTF-8 arrows in this .ps1 as ANSI and rewrite mojibake.
        $leftArrow = [string][char]0x2190
        $rightArrow = [string][char]0x2192
        $prevLink = ""
        $nextLink = ""
        if ($i -gt 0) {
            $prev = $ordered[$i - 1]
            $prevLink = "<a href=""$($prev.File)"">$leftArrow Previous: $($prev.Label)</a>"
        } else {
            $prevLink = "<a href=""index.html"">$leftArrow Home</a>"
        }
        if ($i -lt ($ordered.Count - 1)) {
            $next = $ordered[$i + 1]
            $nextLink = "<a href=""$($next.File)"">Next: $($next.Label)$rightArrow</a>"
        } else {
            $nextLink = "<span></span>"
        }
        $footer = @"
        <div class="nav-footer">
            $prevLink
            $nextLink
        </div>
"@
        # Consume the existing indentation as well: replacing from '<div' onwards
        # would prepend the replacement's own indentation on every run, growing
        # the leading whitespace a little more each time.
        $html = [regex]::Replace(
            $html,
            '(?m)^[ \t]*<div class="nav-footer">[\s\S]*?</div>',
            $footer.TrimEnd(),
            1)
    }

    [System.IO.File]::WriteAllText($path, $html, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Updated $($ch.File) -> $($ch.Label)"
}

Write-Host "Done. $($ordered.Count) chapters synced."
