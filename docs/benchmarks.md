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

## Runtime benchmarks (generated code)

The smoke metrics above time startup and compilation. To time the code each backend
*generates*, run the programs in [`benchmarks/runtime/`](../benchmarks/runtime/) through the
interpreter, the C# transpiler and the JavaScript transpiler:

```powershell
powershell -File scripts/run-runtime-benchmarks.ps1
# only some backends / programs, no rebuild, JSON out:
powershell -File scripts/run-runtime-benchmarks.ps1 -NoBuild -Backends csharp,js -Only fib,loops -OutJson artifacts/perf/run.json
```

Each program is compiled once per backend; the table reports the best of `-Runs` runs
(default 3) without compile time, and flags any backend whose output differs. The `hello`
row is each backend's startup cost. JavaScript needs `node` on `PATH`.

| Program | Stresses |
|---------|----------|
| `fib` | recursive calls, int arithmetic |
| `loops` | nested int loops, comparisons, `%` |
| `locals` | loops inside functions whose names other code also declares (per-body inference) |
| `globals` | top-level accumulators fed by user function calls (globals typed through `__Typed` calls) |
| `floats` | float arithmetic on top-level and local variables |
| `strings` | string concatenation, `str.length` |
| `collections` | array append/index, dict reads/writes, `for in` |
| `methods` | method calls with numeric arguments: recursion via `this`, helpers, static methods |
| `objects` | `new`, method calls, field access |
| `arrays` | float arrays: indexed reads and writes in nested loops (stencil passes) |
| `buffers` | the `arrays` kernel on `buf.float64` buffers |

### JavaScript against hand-written JavaScript

`benchmarks/runtime/js-baseline/<name>.js` is a hand-written JavaScript twin of `<name>.malda`
that prints the same output. The `js-hand` column runs it (it is in the default `-Backends`);
the ratio between `js` and `js-hand` is the cost of the code the JavaScript transpiler
generates. Programs without a twin leave the column empty.

```powershell
powershell -File scripts/run-runtime-benchmarks.ps1 -Backends js,js-hand -Runs 5
```

One local run (2026-10-24, Windows 11, Node 24, best of 5, seconds). Subtract startup to compare
work: bare `node -e 0` takes 0.055 s here, and `hello` on `js` (Node + runtime load) 0.072 s.

| Program | `js` | `js-hand` | Work ratio (startup removed) |
|---------|-----:|----------:|-----------------------------:|
| `arrays` | 0.170 | 0.070 | ~6.5× |
| `floats` | 0.285 | 0.080 | ~8.5× |
| `loops` | 0.133 | 0.077 | ~2.8× |
| `objects` | 0.104 | 0.071 | ~2× |
| `collections` | 0.127 | 0.093 | ~1.4× |
| `fib` | 0.092 | 0.072 | ~1.2× |

The gap was in numeric code: every operand of a comparison or an arithmetic operator went
through `mlRuntime.coerceToFloat`, looked up on the large runtime object.

**After numeric variables (J3, 2026-10-24).** The JavaScript emitter now leaves operators plain
for variables that only ever hold numbers, and binds `coerceToFloat` to a local for the rest.
At this size process timings are mostly Node startup, so this table times `main()` of the
generated program against the same hand-written code inside one Node process (warmed up, best
of 9):

| Program | Before | Generated | Hand-written | Ratio |
|---------|-------:|----------:|-------------:|------:|
| `floats` | 199.8 ms | 10.0 ms | 9.6 ms | 1.0× |
| `loops` | 47.0 ms | 4.0 ms | 4.0 ms | 1.0× |
| `objects` | — | 3.0 ms | 3.4 ms | 0.9× |
| `collections` | — | 25.9 ms | 19.9 ms | 1.3× |
| `fib` | — | 4.7 ms | 2.9 ms | 1.6× |
| `arrays` | 78.2 ms | 4.4 ms | 1.8 ms | 2.5× |
| `buffers` | — | 3.3 ms | 1.7 ms | 1.9× |

What is left: elements of a plain array are of unknown type, so `a[i] + b[j]` still goes through
`getIndex` and `mlRuntime.add` (`buf.float64` buffers avoid it: `buffers` row), and a call
result (`fib(n - 1) + fib(n - 2)`) through `mlRuntime.add`.

The C# fast paths these numbers track (conditional defer frames, primitive operator paths,
direct `this.field`, `__Sync` / `__Typed` function and method twins, direct method dispatch, inferred `int` and
`double` variables) are pinned by `TranspilePerformanceParityTests`.

Related: built-in profiler docs in [`docs/profiling.md`](profiling.md).
