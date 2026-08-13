<#
.SYNOPSIS
    Assemble the Reference Manual chapters into one print-ready book.

.DESCRIPTION
    Reads ReferenceManual/chapters.json, lifts the <main> content out of each
    chapter, rewrites cross-chapter links into internal anchors and emits a
    single self-contained folder that can be printed to PDF from a browser.

    The cover plate is ReferenceManual/assets/cover.svg, inlined as a data URI
    so the book stays a single file. Replace that SVG and rebuild to change the
    graphic; version, date and trim stay HTML overlaid at the bottom of the page.

    Output folder (default artifacts/reference-manual):
        malda-reference-manual.html   the book

    Producing the PDF:
        1. Open malda-reference-manual.html in Chrome or Edge.
        2. Wait for pagination to finish (a banner reports the page count).
        3. Ctrl+P, destination "Save as PDF", margins "None",
           "Background graphics" enabled.

    Page numbers, running heads and the table-of-contents folios are produced
    by Paged.js, loaded from a CDN. Offline the book still prints correctly,
    without those refinements.

.PARAMETER Trim
    Paper size. A4 (default), Letter, 7x10 or 6x9.

.PARAMETER OutputDirectory
    Destination folder. Defaults to artifacts/reference-manual.

.PARAMETER NoPagedJs
    Omit the Paged.js reference entirely.
#>

[CmdletBinding()]
param(
    [ValidateSet('A4', 'Letter', '7x10', '6x9')]
    [string]$Trim = 'A4',

    [string]$OutputDirectory,

    [switch]$NoPagedJs
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$manualDir = Join-Path $repoRoot "ReferenceManual"
$configPath = Join-Path $manualDir "chapters.json"

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\reference-manual"
}

# Trim presets. Inner margins are wider than outer ones to leave room for the
# binding; the resulting text measure is what the code font size is tuned to.
$trimPresets = @{
    'A4'     = @{ Size = '210mm 297mm'; Margin = '22mm 20mm 20mm'; Inner = '26mm'; Outer = '18mm'; Measure = '166mm'; CodeSize = '9pt';   BodySize = '10.5pt' }
    'Letter' = @{ Size = '8.5in 11in';  Margin = '20mm 20mm 18mm'; Inner = '25mm'; Outer = '18mm'; Measure = '173mm'; CodeSize = '9pt';   BodySize = '10.5pt' }
    '7x10'   = @{ Size = '7in 10in';    Margin = '18mm 16mm 16mm'; Inner = '21mm'; Outer = '15mm'; Measure = '142mm'; CodeSize = '8pt';   BodySize = '10pt' }
    '6x9'    = @{ Size = '6in 9in';     Margin = '16mm 14mm 15mm'; Inner = '19mm'; Outer = '13mm'; Measure = '120mm'; CodeSize = '7.2pt'; BodySize = '9.5pt' }
}
$preset = $trimPresets[$Trim]

Write-Host "Building MALDA Reference Manual book ($Trim, text measure $($preset.Measure))"

# ---------------------------------------------------------------- Chapters

$config = Get-Content $configPath -Raw | ConvertFrom-Json

$chapters = @()
$num = 0
foreach ($chapter in $config.chapters) {
    if ($chapter.isHome) { continue }
    $num++
    $chapters += [pscustomobject]@{
        Num     = $num
        File    = $chapter.file
        Title   = $chapter.title
        Label   = "$num. $($chapter.title)"
        Anchor  = "ch-$num"
    }
}

# file name -> anchor, so cross-chapter links become internal jumps
$anchorByFile = @{}
foreach ($chapter in $chapters) {
    $anchorByFile[$chapter.File] = $chapter.Anchor
}

function Get-MainContent {
    param([string]$Html, [string]$FileName)

    $match = [regex]::Match($Html, '(?s)<main>(.*?)</main>')
    if (-not $match.Success) {
        throw "No <main> element found in $FileName"
    }
    return $match.Groups[1].Value
}

function Remove-ScreenChrome {
    param([string]$Content)

    # Non-greedy: these blocks are flat, they contain no nested <div>.
    $Content = [regex]::Replace($Content, '(?s)<div class="breadcrumbs">.*?</div>', '')
    $Content = [regex]::Replace($Content, '(?s)<div class="nav-footer">.*?</div>', '')
    $Content = [regex]::Replace($Content, '(?s)<footer>.*?</footer>', '')
    return $Content.Trim()
}

function Convert-Links {
    param([string]$Content)

    return [regex]::Replace($Content, 'href="([^"#]+\.html)(#[^"]*)?"', {
        param($match)
        $target = $match.Groups[1].Value
        if ($anchorByFile.ContainsKey($target)) {
            return 'href="#' + $anchorByFile[$target] + '"'
        }
        if ($target -eq 'index.html') {
            return 'href="#book-toc"'
        }
        return $match.Value
    })
}

$sections = New-Object System.Text.StringBuilder
$tocItems = New-Object System.Text.StringBuilder

foreach ($chapter in $chapters) {
    $path = Join-Path $manualDir $chapter.File
    if (-not (Test-Path $path)) {
        Write-Warning "Missing chapter file: $($chapter.File)"
        continue
    }

    $html = Get-Content $path -Raw -Encoding UTF8
    $content = Convert-Links (Remove-ScreenChrome (Get-MainContent $html $chapter.File))

    [void]$sections.AppendLine("<section class=""chapter"" id=""$($chapter.Anchor)"">")
    [void]$sections.AppendLine($content)
    [void]$sections.AppendLine("</section>")

    $escapedLabel = [System.Net.WebUtility]::HtmlEncode($chapter.Label)
    [void]$tocItems.AppendLine("        <li><a href=""#$($chapter.Anchor)""><span class=""toc-title"">$escapedLabel</span><span class=""toc-dots""></span></a></li>")

    Write-Host "  + $($chapter.Label)"
}

# ------------------------------------------------------------------ Output

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

# Styles and the highlighter are inlined rather than linked. Two reasons: the book
# becomes a single self-contained file that can be mailed or archived on its own, and
# Paged.js re-fetches linked stylesheets over XHR to parse their @page rules, which
# Chrome blocks under the file:// origin. Linking them makes pagination hang forever
# at "Paginating..." unless the file is served over HTTP.
$bookCss = [System.IO.File]::ReadAllText((Join-Path $manualDir "book.css"))
$syntaxCss = [System.IO.File]::ReadAllText((Join-Path $manualDir "syntax.css"))
$highlightJs = [System.IO.File]::ReadAllText((Join-Path $manualDir "malda-highlight.js"))

$coverPath = Join-Path $manualDir "assets\cover.svg"
if (-not (Test-Path $coverPath)) {
    throw "Cover plate missing: $coverPath. Add ReferenceManual/assets/cover.svg and rebuild."
}
$coverSrc = "data:image/svg+xml;base64," + [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($coverPath))

$pagedJsTag = if ($NoPagedJs) {
    "    <!-- Paged.js omitted (-NoPagedJs) -->"
} else {
    '    <script src="https://unpkg.com/pagedjs@0.4.3/dist/paged.polyfill.js"></script>'
}

$buildDate = Get-Date -Format "MMMM yyyy"
$year = Get-Date -Format "yyyy"

$sizeParts = $preset.Size -split '\s+', 2
$pageWidth = $sizeParts[0]
$pageHeight = $sizeParts[1]

$pageGeometry = @"
        @page {
            size: $($preset.Size);
            margin: $($preset.Margin);
        }

        @page :left {
            margin-left: $($preset.Outer);
            margin-right: $($preset.Inner);
        }

        @page :right {
            margin-left: $($preset.Inner);
            margin-right: $($preset.Outer);
        }

        @page cover {
            margin: 0;
        }

        :root {
            --code-size: $($preset.CodeSize);
            --page-width: $pageWidth;
            --page-height: $pageHeight;
        }

        body {
            font-size: $($preset.BodySize);
        }
"@

$bookHtml = @"
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta name="description" content="MALDA Reference Manual - bound edition ($Trim)">
    <title>MALDA Reference Manual - $Trim print edition</title>
    <style>
/* ===== book.css ===== */
@@BOOK_CSS@@
/* ===== syntax.css ===== */
@@SYNTAX_CSS@@
    </style>
    <style>
$pageGeometry
    </style>
</head>
<body>
    <section class="book-cover">
        <img class="cover-plate" alt="MALDA Reference Manual" src="@@COVER_SRC@@" />
        <p class="cover-meta">Version 0.1 &middot; $buildDate &middot; $Trim edition</p>
    </section>

    <section class="book-copyright">
        <h2>MALDA Reference Manual</h2>
        <p>Version 0.1, $buildDate. $Trim print edition.</p>
        <p>Copyright (c) $year Andrea Maldini.</p>
        <p>MALDA is free and open source software. The language implementation,
           this manual, and the code examples it contains are dual licensed: you
           may use them under the MIT License or under the Apache License 2.0,
           whichever suits you. Either way you may use, copy, modify and
           redistribute them, including commercially, provided the copyright
           notice and the licence text are preserved. Both licence texts are in
           <code>LICENSE-MIT</code> and <code>LICENSE-APACHE</code> at the root of
           the source distribution.</p>
        <p>Programs you write in MALDA are yours. The compiler injects runtime
           code into the programs it produces, and
           <code>LICENSE-RUNTIME-EXCEPTION</code> confirms that this places no
           attribution obligation on your own work.</p>
        <p>The software and this manual are provided &ldquo;as is&rdquo;, without
           warranty of any kind.</p>
        <p>This edition is generated from the HTML Reference Manual by
           <code>scripts/build-reference-manual-book.ps1</code>. Do not edit it by
           hand: change the chapter sources under <code>ReferenceManual/</code> and
           rebuild.</p>
        <p>Code listings wrap at the page measure. A line that continues onto the
           next printed line is indented, so an indented continuation is never a
           new statement.</p>
    </section>

    <nav class="book-toc" id="book-toc">
        <h1>Contents</h1>
        <ol>
$($tocItems.ToString().TrimEnd())
        </ol>
    </nav>

$($sections.ToString().TrimEnd())

    <script>
        // Highlight before paginating: Paged.js measures the rendered DOM, so
        // any content added afterwards would not be laid out into pages.
        window.PagedConfig = { auto: false };
    </script>
    <script>
@@HIGHLIGHT_JS@@
    </script>
$pagedJsTag
    <script>
        (function () {
            // Paged.js empties <body> and refills it with page boxes, so the
            // status banner is only created once pagination is done. Anything
            // present beforehand would be typeset into the book itself.
            function banner(text) {
                var el = document.createElement('div');
                el.className = 'build-status';
                el.textContent = text;
                document.body.appendChild(el);
            }

            function start() {
                if (window.MaldaHighlight) {
                    window.MaldaHighlight.highlightAll();
                }

                if (!window.Paged || !window.Paged.Previewer) {
                    document.body.classList.add('no-paged');
                    banner('Paged.js unavailable: printing without running heads, folios or contents page numbers.');
                    return;
                }

                document.title = 'Paginating... - MALDA Reference Manual';
                new window.Paged.Previewer().preview().then(function (flow) {
                    document.title = 'MALDA Reference Manual (' + flow.total + ' pages)';
                    document.body.classList.add('paged-ready');
                    banner(flow.total + ' pages. Print with margins "None" and "Background graphics" enabled.');
                });
            }

            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', start);
            } else {
                start();
            }
        })();
    </script>
</body>
</html>
"@

# Literal substitution, not string interpolation: the CSS and JS contain "$1" and
# similar sequences that an expandable here-string would eat as variable references.
$bookHtml = $bookHtml.
    Replace('@@BOOK_CSS@@', $bookCss).
    Replace('@@SYNTAX_CSS@@', $syntaxCss).
    Replace('@@HIGHLIGHT_JS@@', $highlightJs).
    Replace('@@COVER_SRC@@', $coverSrc)

$outputPath = Join-Path $OutputDirectory "malda-reference-manual.html"
[System.IO.File]::WriteAllText($outputPath, $bookHtml, [System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "Wrote $outputPath"
Write-Host "$($chapters.Count) chapters, $([math]::Round((Get-Item $outputPath).Length / 1KB)) KB"
Write-Host "Open it in Chrome or Edge, then Ctrl+P -> Save as PDF (margins: None, background graphics: on)."
