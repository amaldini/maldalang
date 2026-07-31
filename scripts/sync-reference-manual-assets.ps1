# Ensure every ReferenceManual page loads the shared stylesheets and scripts.
#
# The manual is a set of hand-written static pages, so a new chapter can easily
# be added with an incomplete <head>. This script is idempotent: run it after
# adding a chapter to bring its asset references in line with the others.

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$manualDir = Join-Path $repoRoot "ReferenceManual"

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$updated = 0

foreach ($file in Get-ChildItem -Path $manualDir -Filter "*.html") {
    $html = [System.IO.File]::ReadAllText($file.FullName)
    $original = $html

    if ($html -notmatch 'href="styles\.css"') {
        Write-Warning "$($file.Name): missing styles.css link, skipping"
        continue
    }

    if ($html -notmatch 'href="syntax\.css"') {
        $html = [regex]::Replace(
            $html,
            '(?m)^(\s*)<link rel="stylesheet" href="styles\.css">',
            '${1}<link rel="stylesheet" href="styles.css">' + "`r`n" + '${1}<link rel="stylesheet" href="syntax.css">',
            1)
    }

    # print.css must load after styles.css so its rules win in the print medium.
    if ($html -notmatch 'href="print\.css"') {
        $html = [regex]::Replace(
            $html,
            '(?m)^(\s*)<link rel="stylesheet" href="syntax\.css">',
            '${1}<link rel="stylesheet" href="syntax.css">' + "`r`n" + '${1}<link rel="stylesheet" href="print.css" media="print">',
            1)
    }

    if ($html -notmatch 'src="malda-highlight\.js"') {
        $html = [regex]::Replace(
            $html,
            '(?m)^(\s*)<script src="navigation\.js"></script>',
            '${1}<script src="malda-highlight.js"></script>' + "`r`n" + '${1}<script src="navigation.js"></script>',
            1)
    }

    if ($html -ne $original) {
        [System.IO.File]::WriteAllText($file.FullName, $html, $utf8NoBom)
        Write-Host "Updated $($file.Name)"
        $updated++
    }
}

Write-Host "Done. $updated file(s) updated."
