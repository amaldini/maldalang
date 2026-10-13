<#
.SYNOPSIS
  Runtime benchmarks for the code MALDA generates: interpreter vs C# transpile vs JavaScript.

.DESCRIPTION
  Compiles each benchmarks/runtime/*.malda once per backend, then times the run (best of
  -Runs). Compile time is not included. The "hello" row is the startup baseline of each
  backend; subtract it mentally when a benchmark is short.
  Numbers are machine-local; compare deltas on the same machine. See docs/benchmarks.md.
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$CliOut = "",
    [string]$OutJson = "",
    [string[]]$Only = @(),
    [string[]]$Backends = @("interpret", "csharp", "js"),
    [int]$Runs = 3,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

# `powershell -File ... -Backends csharp,js` passes one string; accept comma lists either way.
$Backends = @($Backends | ForEach-Object { $_ -split "," } | Where-Object { $_ })
$Only = @($Only | ForEach-Object { $_ -split "," } | Where-Object { $_ })

if ([string]::IsNullOrWhiteSpace($CliOut)) {
    $CliOut = Join-Path $RepoRoot "artifacts/malda-perf-cli"
}

if (-not $NoBuild) {
    Write-Host "Building CLI + compiler (Release) to $CliOut ..."
    dotnet build (Join-Path $RepoRoot "MaldaLang") -c Release -o $CliOut --verbosity quiet | Out-Null
    dotnet build (Join-Path $RepoRoot "MaldaLang.Compiler") -c Release -o $CliOut --verbosity quiet | Out-Null
}

$malda = Join-Path $CliOut "malda.exe"
if (-not (Test-Path $malda)) { $malda = Join-Path $CliOut "malda" }
if (-not (Test-Path $malda)) { throw "CLI binary not found under $CliOut" }

$node = Get-Command node -ErrorAction SilentlyContinue
if ($Backends -contains "js" -and -not $node) {
    Write-Warning "node not found; skipping the js backend."
    $Backends = $Backends | Where-Object { $_ -ne "js" }
}

$benchDir = Join-Path $RepoRoot "benchmarks/runtime"
$workDir = Join-Path $RepoRoot "artifacts/perf"
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

$helloPath = Join-Path $workDir "hello.malda"
Set-Content -Path $helloPath -Value 'io.print("hello");' -Encoding utf8

$sources = @(Get-Item $helloPath) + @(Get-ChildItem $benchDir -Filter *.malda | Sort-Object Name)
if ($Only.Count -gt 0) {
    $sources = $sources | Where-Object { $Only -contains $_.BaseName -or $_.BaseName -eq "hello" }
}

function Invoke-Best([scriptblock]$Block) {
    $best = [double]::MaxValue
    $output = ""
    for ($r = 0; $r -lt $Runs; $r++) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $output = (& $Block 2>&1 | Out-String).Trim()
        $sw.Stop()
        if ($LASTEXITCODE -ne 0) { return @{ Seconds = $null; Output = $output } }
        $best = [math]::Min($best, $sw.Elapsed.TotalSeconds)
    }
    return @{ Seconds = [math]::Round($best, 3); Output = $output }
}

$results = @()
foreach ($src in $sources) {
    $name = $src.BaseName
    $outDir = Join-Path $workDir $name
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $row = [ordered]@{ benchmark = $name }
    $outputs = @{}

    foreach ($backend in $Backends) {
        $run = $null
        switch ($backend) {
            "interpret" {
                $run = Invoke-Best { & $malda $src.FullName }
            }
            "csharp" {
                $exe = Join-Path $outDir "$name.exe"
                & $malda compile $src.FullName --mode transpile -o $exe | Out-Null
                if ($LASTEXITCODE -ne 0) { $run = @{ Seconds = $null; Output = "compile failed" } }
                else { $run = Invoke-Best { & $exe } }
            }
            "js" {
                $js = Join-Path $outDir "$name.js"
                & $malda compile $src.FullName --mode js -o $js | Out-Null
                if ($LASTEXITCODE -ne 0) { $run = @{ Seconds = $null; Output = "compile failed" } }
                else {
                    $runtime = Join-Path $outDir "malda-js-runtime.js"
                    $run = Invoke-Best { & $node.Source -r $runtime $js }
                }
            }
        }
        $row[$backend] = $run.Seconds
        $outputs[$backend] = $run.Output
        if ($null -eq $run.Seconds) { Write-Warning "${name}/${backend} failed: $($run.Output)" }
    }

    $distinct = @($outputs.Values | Where-Object { $_ } | Select-Object -Unique)
    $row["sameOutput"] = ($distinct.Count -le 1)
    if ($distinct.Count -gt 1) {
        Write-Warning "${name}: backends disagree: $(($outputs.GetEnumerator() | ForEach-Object { "$($_.Key)=[$($_.Value)]" }) -join ' ')"
    }
    $results += [pscustomobject]$row
}

$results | Format-Table -AutoSize

if (-not [string]::IsNullOrWhiteSpace($OutJson)) {
    $payload = [ordered]@{
        note = "Best of $Runs runs, seconds, compile time excluded. Machine-local; compare same-machine deltas."
        os = [System.Environment]::OSVersion.VersionString
        recorded = (Get-Date -Format "yyyy-MM-dd")
        results = $results
    }
    $payload | ConvertTo-Json -Depth 4 | Set-Content -Path $OutJson -Encoding utf8
    Write-Host "Wrote $OutJson"
}
