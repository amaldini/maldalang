# Few-shot snippets

*Applies to: MALDA 0.1.0*

Tiny programs for LLM context. Larger curated samples live under `Examples/`.

| File | Topic |
|------|--------|
| `01_hello.malda` | `io.print` |
| `02_vars_control.malda` | vars, if, loops |
| `03_functions.malda` | functions + lambda |
| `04_prompt.malda` | prompt block |
| `05_rest_get.malda` | `@GET` handler shape |
| `06_actor_counter.malda` | actor + send + sleep (flat `print` inside handlers — `io` is out of scope there) |
| `07_input_loop.malda` | `io.input` + validation + seeded randomness, testable from a piped transcript |
| `08_ansi_console.malda` | `AnsiConsole` markup, panel, table, tree, status, progress (+ prompt shape) |
| `09_collections.malda` | array mutation, `.length`, object fields, foreach |
| `10_strings.malda` | `$"..."` interpolation vs plain strings and concatenation |

Also useful from the main tree:

- `Examples/Basics/*`
- `Examples/Prompts/basic_prompt.malda`
- `Examples/Web/rest_api_server.malda`
- `Examples/Actors/basic_counter.malda`
