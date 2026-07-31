# MALDA Profiling

MALDA includes a built-in profiler for finding performance bottlenecks in both interpreted runs and transpiled executables.

The profiler is designed to help answer three practical questions:

- Which built-ins are consuming the most time?
- Which MALDA functions are hottest?
- Which statement locations are the main script bottlenecks?

## What It Profiles

The current profiler reports three categories:

- `BuiltIns`: time spent inside MALDA built-in functions
- `Functions`: time spent inside MALDA functions
- `Statements`: time spent in MALDA statement locations

Each entry includes:

- `Calls`: how many times the item ran
- `TotalMs`: inclusive time spent in that item
- `SelfMs`: time spent in that item excluding profiled child work
- `AvgMs`: average time per call

The report also includes the total runtime for the session.

## Interpreted Runs

Use `--profile` when running a MALDA file directly:

```bash
malda my-script.malda --profile
```

This prints a text summary to the console.

To write a JSON report instead:

```bash
malda my-script.malda --profile --profile-output profile.json --profile-format json
```

To write both a text file and a JSON file:

```bash
malda my-script.malda --profile --profile-output profile --profile-format both
```

With `both`, MALDA writes `profile.txt` and `profile.json`.

## Transpiled Executables

You can bake profiling into a transpiled executable at compile time:

```bash
malda compile my-script.malda --mode transpile -o my-script.exe --profile
```

This generates an executable with profiling instrumentation enabled.

To write a JSON report when that executable runs:

```bash
malda compile my-script.malda --mode transpile -o my-script.exe --profile --profile-output profile.json --profile-format json
my-script.exe
```

The same report structure is used in interpreted and transpiled execution.

## Output Formats

Supported formats:

- `text`: human-readable summary
- `json`: structured output for tooling
- `both`: write both text and JSON files

Examples:

```bash
malda my-script.malda --profile --profile-format text
malda my-script.malda --profile --profile-format json --profile-output profile.json
malda my-script.malda --profile --profile-format both --profile-output profile
```

## Periodic snapshots (long runs)

By default, file output is written **once** when the program exits normally. For long runs (for example backtests), you can ask MALDA to **rewrite the same output file on a wall-clock interval** so you still have a usable report if you stop the process early.

- **`--profile-periodic-seconds N`**: every **N** seconds (real time), MALDA writes the profile again to `--profile-output`. Use **`N = 0`** (the default) to disable periodic writes and keep end-of-run-only behavior.
- Applies to **`json`**, **`text`**, and **`both`**: with `both`, both files are refreshed on the same schedule.
- The output path is **overwritten** each time (you always see the latest snapshot, not a history of files).
- Periodic writes **do not** print the full text report to the console on every tick (only file output is updated).

In JSON reports, snapshots include **`"Partial": true`**. The final report written at exit has **`"Partial": false`**.

Example (snapshot every 60 seconds):

```bash
malda my-script.malda --profile --profile-output profile.json --profile-format json --profile-periodic-seconds 60
```

For transpiled executables, pass the same flags to `malda compile` so the generated `.exe` bakes in the interval.

## Reading The Report

Typical workflow:

1. Start with `BuiltIns` to see if a standard function dominates runtime.
2. Check `Functions` to find user-defined hotspots.
3. Use `Statements` to locate the expensive parts of a script or function body.

If `TotalMs` is high but `SelfMs` is low, the item is mostly expensive because of child work beneath it.
If both `TotalMs` and `SelfMs` are high, the item itself is likely the hotspot.

## Example JSON Shape

```json
{
  "SessionName": "my-script.malda",
  "Partial": false,
  "TotalMs": 41.8218,
  "BuiltIns": [
    {
      "Name": "string",
      "Calls": 2,
      "TotalMs": 4.1647,
      "SelfMs": 4.1647,
      "AvgMs": 2.08235
    }
  ],
  "Functions": [
    {
      "Name": "hot",
      "Line": 1,
      "Calls": 2,
      "TotalMs": 25.2938,
      "SelfMs": 0.8994,
      "AvgMs": 12.6469
    }
  ],
  "Statements": [
    {
      "Name": "Print",
      "Line": 1,
      "Calls": 2,
      "TotalMs": 24.3933,
      "SelfMs": 20.2286,
      "AvgMs": 12.19665
    }
  ]
}
```

Depending on the execution mode, file paths may be the original MALDA file path or the generated temporary path used during compilation.

## Current Scope

Version 1 of the profiler is intentionally focused on useful, low-overhead instrumentation:

- Statement-level timing is included.
- Function timing is included.
- Built-in timing is included.
- Expression-level profiling is not included yet.

That tradeoff keeps the profiler practical for everyday performance investigations while still making built-in hotspots and slow script regions visible.

## MT4 optimizer (PowerShell scenarios)

Optional domain-pack profiling scripts are maintained outside this core repository.
