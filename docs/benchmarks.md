# Micro-benchmarks

Honest, local timings for three smoke metrics — not a competitive leaderboard.

| Metric | What it measures |
|--------|------------------|
| Interpret `hello_world` | Cold-ish CLI startup + run of the smallest example |
| Transpile `complete_starter_program` | `malda compile --mode transpile` wall time for a medium Basics file |
| HTTP health loop | Sequential GETs against a short-lived `RestServer` on port `18080` |

## Run

From the repo root (Windows PowerShell 5+ or PowerShell 7):

```powershell
powershell -File scripts/run-micro-benchmarks.ps1
# optional JSON:
powershell -File scripts/run-micro-benchmarks.ps1 -OutJson artifacts/bench.json
```

Requirements: .NET 8 SDK. The script builds `MaldaLang` into `artifacts/malda-bench-cli`.

## How to read the numbers

- Report the machine OS and whether you used a Release CLI build (the script does).
- Prefer **deltas on the same machine** after a change over absolute comparisons across laptops.
- The health loop is single-threaded client requests; it is a regression smoke, not peak throughput.
- CI machines vary; publish sample ranges in release notes when useful, not as SLAs.

## Sample results (not an SLA)

Checked-in template: [`docs/benchmarks-sample-results.json`](benchmarks-sample-results.json). Values below are one illustrative local Release run — replace with your machine’s output when comparing.

| Metric | Sample (seconds) |
|--------|------------------|
| Interpret `hello_world` | 0.85 |
| Transpile `complete_starter_program` | 4.2 |
| HTTP health loop (50 GETs) | 1.1 (~45.5 req/s) |

Related: built-in profiler docs in [`docs/profiling.md`](profiling.md).
