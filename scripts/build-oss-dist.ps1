#requires -Version 5.1
<#
.SYNOPSIS
  Build a downloadable MALDA distribution (no source tree required to run).

.DESCRIPTION
  Publishes a self-contained CLI under bin/malda, and on Windows also the
  Desktop IDE under bin/desktop-ide (layout expected by FindRepoRoot). Packs
  Examples, ReferenceManual, program.html (Desktop IDE Preview web host),
  Templates, docs/llm (language pack), docs/spec, agent entrypoints
  (AGENTS.md, llms.txt), and licence files into a zip under artifacts/dist/.

.PARAMETER Version
  Version label used in folder/zip names (default: from MaldaLang.csproj, else 0.1.0).

.PARAMETER Runtime
  Target RID: win-x64, linux-x64, or all (default: win-x64).

.PARAMETER Configuration
  Build configuration (default: Release).

.PARAMETER SkipZip
  Leave the unpacked folder only; do not create .zip archives.

.PARAMETER SkipDesktop
  Do not publish the Windows Desktop IDE (CLI-only bundle).
#>
param(
    [string]$Version = "",
    [ValidateSet("win-x64", "linux-x64", "all")]
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SkipZip,
    [switch]$SkipDesktop
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $csproj = Get-Content (Join-Path $repoRoot "MaldaLang\MaldaLang.csproj") -Raw
    if ($csproj -match "<Version>([^<]+)</Version>") {
        $Version = $Matches[1].Trim()
    }
    else {
        $Version = "0.1.0"
    }
}

$rids = if ($Runtime -eq "all") { @("win-x64", "linux-x64") } else { @($Runtime) }
$distRoot = Join-Path $repoRoot "artifacts\dist"
New-Item -ItemType Directory -Force -Path $distRoot | Out-Null

function Copy-TreeFiltered {
    param(
        [string]$Source,
        [string]$Destination,
        [string[]]$ExcludeDirNames = @()
    )

    if (-not (Test-Path $Source)) {
        throw "Missing source directory: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        if ($_.PSIsContainer -and ($ExcludeDirNames -contains $_.Name)) {
            return
        }
        $target = Join-Path $Destination $_.Name
        if ($_.PSIsContainer) {
            Copy-TreeFiltered -Source $_.FullName -Destination $target -ExcludeDirNames $ExcludeDirNames
        }
        else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
}

function Write-DistReadme {
    param(
        [string]$Path,
        [string]$Rid,
        [string]$Ver,
        [bool]$IncludeDesktop
    )

    $exeRel = if ($Rid -like "win-*") { "bin\malda\malda.exe" } else { "bin/malda/malda" }
    $launcherHint = if ($Rid -like "win-*") { ".\malda.bat" } else { "./malda" }
    $included = @(
        "bin/malda - CLI interpreter / compiler driver"
    )
    if ($IncludeDesktop) {
        $included += "bin/desktop-ide - Desktop IDE (WPF)"
    }
    $included += @(
        "Examples\ - sample programs",
        "ReferenceManual\ - HTML language reference (open index.html)",
        "program.html - Desktop IDE Preview web host page",
        "docs\llm\ - language pack for coding agents writing .malda",
        "docs\spec\ - language spec notes",
        "Templates\ - scaffolds for malda new webapi|fullstack",
        "AGENTS.md / llms.txt - agent entrypoints",
        "Licence files (MIT OR Apache-2.0)"
    )

    $desktopBlock = ""
    if ($IncludeDesktop) {
        $desktopBlock = @"

Desktop IDE (Windows)
---------------------
Run:

   bin\desktop-ide\MaldaLang.DesktopIDE.exe

Or double-click MaldaDesktop.bat. Examples and the Reference Manual sit next to
bin\ so the IDE can find them.
"@
    }

    $includedText = ($included | ForEach-Object { "- $_" }) -join "`r`n"

    $text = @"
MALDA $Ver ($Rid)
=================

Ready-to-run build. You do not need the git sources.

Quick start (CLI)
-----------------
1. Unzip this folder anywhere.
2. From a terminal in this folder, run:

   $exeRel Examples\Basics\hello_world.malda

   Or: $launcherHint Examples\Basics\hello_world.malda

3. Optional: add bin\malda (or bin/malda) to your PATH.
$desktopBlock
For coding agents
-----------------
Point any agentic LLM at this folder. Start with AGENTS.md, then load
docs\llm\ (see docs\llm\README.md). Smoke-run generated programs with
$launcherHint path\to\program.malda. Use malda new webapi|fullstack for
scaffolds (Templates\ must stay next to bin\).

Requirements
------------
- This build is self-contained for $Rid (no separate .NET install needed for the shipped apps).
- AI features still need OPENROUTER_API_KEY or will download the small local GGUF
  fallback on first use (~500 MB). See the project README.

Included
--------
$includedText

Source and updates
------------------
https://github.com/amaldini/maldalang
"@
    Set-Content -LiteralPath $Path -Value $text -Encoding UTF8
}

function Write-DistAgentsMd {
    param(
        [string]$Path,
        [string]$Rid
    )

    $launcher = if ($Rid -like "win-*") { ".\malda.bat" } else { "./malda" }
    $exeRel = if ($Rid -like "win-*") { "bin\malda\malda.exe" } else { "bin/malda/malda" }

    $text = @"
# AGENTS.md - MALDA distribution

This folder is a **runtime distribution** (CLI, docs, examples, templates). It is
not the C# engine source tree. Do not look for ``MaldaLang/`` or run ``dotnet test``.

## Writing / reviewing ``.malda`` programs

Load the language pack under ``docs/llm/`` in the order described in
``docs/llm/README.md``:

1. ``docs/llm/malda-syntax.md`` (always)
2. ``docs/llm/malda-gotchas.md`` (always - the failures that run without error)
3. Matching files under ``docs/llm/few-shot/``
4. ``docs/llm/malda-grammar.md`` for unfamiliar constructs
5. ``docs/llm/malda-builtins-min.md`` when calling APIs/libs; grep
   ``docs/llm/malda-builtins.tsv`` for one specific name
6. Deeper: ``Examples/``, ``ReferenceManual/``, ``docs/spec/``

Compact index: ``llms.txt``.

## Run generated code

From this folder:

``````bash
$launcher path/to/program.malda
# or: $exeRel path/to/program.malda
``````

Scaffold a project (requires ``Templates/`` next to ``bin/``):

``````bash
$launcher new webapi my-api
$launcher new fullstack my-app
``````

Programs that read input or use randomness are still testable. Seed with
``math.seed(n)`` so branches are reachable on purpose, then pipe a scripted
transcript into ``input()``:

``````bash
printf '50\n25\n39\n' | $launcher guess_number.malda
``````

``````powershell
"50","25","39" | $launcher guess_number.malda
``````

Piped output strips colour and Unicode panel borders; that is expected. Check
those visuals in an interactive terminal.

## Prefer ``function``

In examples and generated code, use the keyword ``function`` (not ``fn`` / ``def``).
Prompt declarations use name-only parameters (no typed prompt params).
"@
    Set-Content -LiteralPath $Path -Value $text -Encoding UTF8
}

function Write-DistLlmsTxt {
    param(
        [string]$Path
    )

    $text = @"
# MALDA distribution - LLM doc index

Canonical reading order for tools and agents working from this unzipped folder
(not the full git repository).

## Start

- AGENTS.md - distribution agent map (run CLI, load language pack)
- README.txt - human quick start
- docs/llm/README.md - how to load the language pack

## Writing MALDA programs (language pack)

- docs/llm/malda-syntax.md - compact idioms / do-don't
- docs/llm/malda-gotchas.md - mistakes that run without error
- docs/llm/malda-grammar.md - plain-text BNF
- docs/llm/malda-builtins-min.md - high-frequency builtins and top-level objects
- docs/llm/malda-builtins.tsv - every builtin, grep-able
- docs/llm/few-shot/ - tiny runnable snippets

## Language reference (deeper)

- ReferenceManual/index.html - HTML reference home
- docs/spec/malda-language-1.0.md - language spec notes
- docs/spec/README.md - spec folder index
- malda-cheat-sheet.html - quick syntax cheat sheet (prefer docs/llm for agents)

## Examples and scaffolds

- Examples/Basics/
- Examples/Prompts/
- Examples/Web/
- Examples/Actors/
- Templates/webapi/
- Templates/fullstack/
"@
    Set-Content -LiteralPath $Path -Value $text -Encoding UTF8
}

function Write-LauncherScripts {
    param(
        [string]$Stage,
        [string]$Rid,
        [bool]$IncludeDesktop
    )

    if ($Rid -like "win-*") {
        @(
            '@echo off'
            'setlocal'
            '"%~dp0bin\malda\malda.exe" %*'
            'exit /b %ERRORLEVEL%'
        ) | Set-Content -LiteralPath (Join-Path $Stage "malda.bat") -Encoding ASCII

        if ($IncludeDesktop) {
            @(
                '@echo off'
                'setlocal'
                'start "" "%~dp0bin\desktop-ide\MaldaLang.DesktopIDE.exe" %*'
            ) | Set-Content -LiteralPath (Join-Path $Stage "MaldaDesktop.bat") -Encoding ASCII
        }
    }
    else {
        $launcher = Join-Path $Stage "malda"
        $bash = @(
            '#!/usr/bin/env bash'
            'set -euo pipefail'
            'ROOT="$(cd "$(dirname "$0")" && pwd)"'
            'exec "$ROOT/bin/malda/malda" "$@"'
        ) -join "`n"
        [System.IO.File]::WriteAllText($launcher, $bash + "`n")
        try { & chmod +x $launcher 2>$null } catch { }
    }
}

foreach ($rid in $rids) {
    $includeDesktop = ($rid -like "win-*") -and (-not $SkipDesktop)
    $stage = Join-Path $distRoot "malda-$Version-$rid"
    $cliDir = Join-Path $stage "bin\malda"
    $desktopDir = Join-Path $stage "bin\desktop-ide"
    $compilerPublish = Join-Path $distRoot "_compiler-$rid"

    Write-Host "=== Publishing CLI ($rid, self-contained) ===" -ForegroundColor Cyan
    if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    if (Test-Path $compilerPublish) { Remove-Item -LiteralPath $compilerPublish -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $cliDir | Out-Null

    dotnet publish (Join-Path $repoRoot "MaldaLang\MaldaLang.csproj") `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $cliDir `
        --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish MaldaLang failed for $rid" }

    Write-Host "=== Publishing compiler beside CLI ===" -ForegroundColor Cyan
    dotnet publish (Join-Path $repoRoot "MaldaLang.Compiler\MaldaLang.Compiler.csproj") `
        -c $Configuration `
        -r $rid `
        --self-contained false `
        -o $compilerPublish `
        --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish MaldaLang.Compiler failed for $rid" }

    Get-ChildItem -LiteralPath $compilerPublish -File | ForEach-Object {
        $dest = Join-Path $cliDir $_.Name
        if (-not (Test-Path $dest) -or $_.Name -like "MaldaLang.Compiler.*") {
            Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
        }
    }
    Remove-Item -LiteralPath $compilerPublish -Recurse -Force

    if ($includeDesktop) {
        Write-Host "=== Publishing Desktop IDE ($rid, self-contained) ===" -ForegroundColor Cyan
        New-Item -ItemType Directory -Force -Path $desktopDir | Out-Null
        dotnet publish (Join-Path $repoRoot "MaldaLang.DesktopIDE\MaldaLang.DesktopIDE.csproj") `
            -c $Configuration `
            -r $rid `
            --self-contained true `
            -p:PublishSingleFile=false `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:ErrorOnDuplicatePublishOutputFiles=false `
            -o $desktopDir `
            --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish MaldaLang.DesktopIDE failed for $rid" }

        $desktopExe = Join-Path $desktopDir "MaldaLang.DesktopIDE.exe"
        if (-not (Test-Path $desktopExe)) {
            throw "Expected Desktop IDE binary missing: $desktopExe"
        }
    }

    Write-Host "=== Copying Examples, ReferenceManual, language pack, Templates, licences ===" -ForegroundColor Cyan
    Copy-TreeFiltered `
        -Source (Join-Path $repoRoot "Examples") `
        -Destination (Join-Path $stage "Examples") `
        -ExcludeDirNames @("bin", "obj", "runtimes", "node_modules")

    Copy-TreeFiltered `
        -Source (Join-Path $repoRoot "ReferenceManual") `
        -Destination (Join-Path $stage "ReferenceManual") `
        -ExcludeDirNames @("bin", "obj")

    Copy-TreeFiltered `
        -Source (Join-Path $repoRoot "docs\llm") `
        -Destination (Join-Path $stage "docs\llm") `
        -ExcludeDirNames @("bin", "obj")

    Copy-TreeFiltered `
        -Source (Join-Path $repoRoot "docs\spec") `
        -Destination (Join-Path $stage "docs\spec") `
        -ExcludeDirNames @("bin", "obj")

    Copy-TreeFiltered `
        -Source (Join-Path $repoRoot "Templates") `
        -Destination (Join-Path $stage "Templates") `
        -ExcludeDirNames @("bin", "obj", "node_modules")

    $cheatSheet = Join-Path $repoRoot "malda-cheat-sheet.html"
    if (Test-Path $cheatSheet) {
        Copy-Item -LiteralPath $cheatSheet -Destination (Join-Path $stage "malda-cheat-sheet.html") -Force
    }

    $webPreviewHost = Join-Path $repoRoot "program.html"
    if (-not (Test-Path $webPreviewHost)) {
        throw "Expected Desktop IDE web preview host missing: $webPreviewHost"
    }
    Copy-Item -LiteralPath $webPreviewHost -Destination (Join-Path $stage "program.html") -Force

    $webPreviewFallbackHost = Join-Path $repoRoot "host.html"
    if (Test-Path $webPreviewFallbackHost) {
        Copy-Item -LiteralPath $webPreviewFallbackHost -Destination (Join-Path $stage "host.html") -Force
    }

    foreach ($lic in @(
            "LICENSE-MIT",
            "LICENSE-APACHE",
            "LICENSE-RUNTIME-EXCEPTION",
            "TRADEMARK.md",
            "THIRD-PARTY-NOTICES.md"
        )) {
        $src = Join-Path $repoRoot $lic
        if (Test-Path $src) {
            Copy-Item -LiteralPath $src -Destination (Join-Path $stage $lic) -Force
        }
    }

    Write-DistReadme -Path (Join-Path $stage "README.txt") -Rid $rid -Ver $Version -IncludeDesktop:$includeDesktop
    Write-DistAgentsMd -Path (Join-Path $stage "AGENTS.md") -Rid $rid
    Write-DistLlmsTxt -Path (Join-Path $stage "llms.txt")
    Write-LauncherScripts -Stage $stage -Rid $rid -IncludeDesktop:$includeDesktop

    if ($rid -like "win-*") {
        $updateScript = Join-Path $repoRoot "scripts\update-local-win-x64-release.ps1"
        if (Test-Path -LiteralPath $updateScript) {
            $stageScripts = Join-Path $stage "scripts"
            New-Item -ItemType Directory -Force -Path $stageScripts | Out-Null
            Copy-Item -LiteralPath $updateScript -Destination (Join-Path $stageScripts "update-local-win-x64-release.ps1") -Force
        }
    }

    $syntaxPack = Join-Path $stage "docs\llm\malda-syntax.md"
    $webapiTemplate = Join-Path $stage "Templates\webapi"
    $stagedPreviewHost = Join-Path $stage "program.html"
    if (-not (Test-Path $syntaxPack)) {
        throw "Expected language pack missing: $syntaxPack"
    }
    if (-not (Test-Path $webapiTemplate)) {
        throw "Expected Templates/webapi missing: $webapiTemplate"
    }
    if (-not (Test-Path $stagedPreviewHost)) {
        throw "Expected web preview host missing from stage: $stagedPreviewHost"
    }

    $exe = if ($rid -like "win-*") { Join-Path $cliDir "malda.exe" } else { Join-Path $cliDir "malda" }
    if (-not (Test-Path $exe)) {
        throw "Expected CLI binary missing: $exe"
    }

    Write-Host "=== Smoke: hello_world ===" -ForegroundColor Cyan
    & $exe (Join-Path $stage "Examples\Basics\hello_world.malda")
    if ($LASTEXITCODE -ne 0) { throw "Smoke run failed for $rid" }

    if (-not $SkipZip) {
        $zip = Join-Path $distRoot "malda-$Version-$rid.zip"
        if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
        Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
        Write-Host "Zip: $zip" -ForegroundColor Green
    }

    Write-Host "Folder: $stage" -ForegroundColor Green
}

Write-Host "Done. Artifacts under $distRoot" -ForegroundColor Green
