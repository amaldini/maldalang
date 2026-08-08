<#
.SYNOPSIS
  Micro-benchmarks for interpret startup, C# transpile, and a short HTTP health loop.

.DESCRIPTION
  Writes human-readable timings to the console and optionally JSON to -OutJson.
  Numbers are machine-local; compare relative changes, not absolute vanity scores.
  See docs/benchmarks.md.
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$CliOut = "",
    [string]$OutJson = "",
    [int]$HealthRequests = 50
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($CliOut)) {
    $CliOut = Join-Path $RepoRoot "artifacts/malda-bench-cli"
}

function Measure-Seconds([scriptblock]$Block) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $Block
    $sw.Stop()
    return [math]::Round($sw.Elapsed.TotalSeconds, 3)
}

Write-Host "Building CLI + compiler to $CliOut ..."
dotnet build (Join-Path $RepoRoot "MaldaLang") -c Release -o $CliOut --verbosity quiet | Out-Null
dotnet build (Join-Path $RepoRoot "MaldaLang.Compiler") -c Release -o $CliOut --verbosity quiet | Out-Null
$malda = Join-Path $CliOut "malda.exe"
if (-not (Test-Path $malda)) {
    $malda = Join-Path $CliOut "malda"
}
if (-not (Test-Path $malda)) {
    throw "CLI binary not found under $CliOut"
}

$hello = Join-Path $RepoRoot "Examples/Basics/hello_world.malda"
$starter = Join-Path $RepoRoot "Examples/Basics/complete_starter_program.malda"
$compileOutDir = Join-Path $RepoRoot "artifacts/malda-bench-compile"
New-Item -ItemType Directory -Force -Path $compileOutDir | Out-Null
$compileExe = Join-Path $compileOutDir "starter.exe"

Write-Host "1) Interpret hello_world ..."
$interpretSec = Measure-Seconds {
    & $malda $hello | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "interpret failed with exit $LASTEXITCODE" }
}

Write-Host "2) Transpile complete_starter_program ..."
$transpileSec = Measure-Seconds {
    & $malda compile $starter --mode transpile -o $compileExe | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "transpile failed with exit $LASTEXITCODE" }
}

Write-Host "3) HTTP health loop ($HealthRequests GETs, in-process) ..."
# Mirror Examples/Web/http_client_json.malda (loopback RestServer + httpGet).
$healthScript = @"
@GET("/api/health")
function health() {
    return parseJSON("{\"ok\":true}");
}

var port = 18080;
var baseUrl = "http://localhost:" + port;
var server = new RestServer(port, "localhost");
server.start();
sleep(300);

var n = $HealthRequests;
var i = 0;
var failures = 0;
while (i < n) {
    var res = httpGet(baseUrl + "/api/health");
    if (!res.ok) {
        failures = failures + 1;
        print("FAIL status=" + res.status + " error=" + res.error);
    }
    i = i + 1;
}
server.stop();
if (failures > 0) {
    print("HEALTH_FAIL " + failures);
} else {
    print("HEALTH_OK " + n);
}
"@
$healthPath = Join-Path $compileOutDir "bench_health.malda"
[System.IO.File]::WriteAllText($healthPath, $healthScript, (New-Object System.Text.UTF8Encoding($false)))

$healthSec = Measure-Seconds {
    $out = & $malda $healthPath 2>&1
    $text = ($out | Out-String)
    if ($text -notmatch "HEALTH_OK") { throw "health bench failed: $text" }
}
$rps = [math]::Round($HealthRequests / [math]::Max($healthSec, 0.001), 1)

$results = [ordered]@{
    machine = $env:COMPUTERNAME
    os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    interpret_hello_seconds = $interpretSec
    transpile_starter_seconds = $transpileSec
    health_requests = $HealthRequests
    health_loop_seconds = $healthSec
    health_requests_per_second = $rps
}

Write-Host ""
Write-Host "Results"
Write-Host "-------"
Write-Host ("interpret hello_world:     {0} s" -f $interpretSec)
Write-Host ("transpile starter:         {0} s" -f $transpileSec)
Write-Host ("health {0} GETs:            {1} s ({2} req/s)" -f $HealthRequests, $healthSec, $rps)

if (-not [string]::IsNullOrWhiteSpace($OutJson)) {
    ($results | ConvertTo-Json) | Set-Content -Path $OutJson -Encoding utf8
    Write-Host "Wrote $OutJson"
}
