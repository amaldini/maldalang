# Few-shot snippets

*Applies to: MALDA 1.0.1*

Tiny programs for LLM context. Larger curated samples live under `Examples/`.

| File | Topic |
|------|--------|
| `01_hello.malda` | `io.print` |
| `02_vars_control.malda` | vars, if, loops |
| `03_functions.malda` | functions + lambda |
| `04_prompt.malda` | prompt block |
| `05_rest_get.malda` | `@GET` handler shape (no `RestServer.start`) |
| `06_actor_counter.malda` | actor + send + sleep (flat `print` inside handlers — `io` is out of scope there) |
| `07_input_loop.malda` | `io.input` + validation + seeded randomness, testable from a piped transcript |
| `08_ansi_console.malda` | `AnsiConsole` markup, panel, table, tree, status, progress (+ prompt shape) |
| `09_collections.malda` | array mutation, `.length`, object fields, foreach |
| `10_strings.malda` | `$"..."` interpolation vs plain strings and concatenation |
| `11_errors_match.malda` | `try` / `catch` / `finally`, tagged catch, `match` |
| `12_include.malda` | `include` splice (+ `helpers/greet_lib.malda`) |
| `13_http_client.malda` | `httpGet` / `httpPost` against a local echo `RestServer` |
| `14_assert_test.malda` | `assert` unit checks (`malda test` needs `*.test.malda`) |
| `15_bearer_jwt_shape.malda` | `createJwt` / `verifyJwt` + Bearer middleware shape (no server loop) |
| `16_workflow_retry.malda` | `workflow` + `retry`, `startWorkflow`, inspect / dead letters |
| `17_schema_validate.malda` | `schema` + `validate` (`{ ok, data|error }`) |
| `18_schema_prompt.malda` | `schema` return type + `await` prompt (structured `response_format`) |
| `19_ui_tree.malda` | `ui.*` tree composition (`props` / children; no host loop) |
| `20_sum_type_prompt.malda` | sum-type return + optional payload types + `await` → variant + `match` (`{tag,…}` JSON) |
| `21_api_program.malda` | `api` + program JSON + `runProgram` (deterministic) |
| `22_gather_extract_prompt.malda` | `gather:` + `-> Type` Mode C (offline PromptInstance) |
| `23_grounded_wrap.malda` | `grounded.wrap` citations wrapper (GraphMemory `ask` is the ASK path) |
| `24_capability_tokens.malda` | `cap.fileRead` unforgeable token (`cap.read` rejects forged dicts) |

Also useful from the main tree:

- `Examples/Basics/errors_and_match.malda`, `Examples/Basics/schema_validate.malda`, `Examples/Basics/modules_include.malda`, `Examples/Basics/modules_import.malda`
- Recipe: [`docs/tutorials/errors-and-validation.md`](../tutorials/errors-and-validation.md)
- `Examples/Testing/unit_test_basics.test.malda`
- `Examples/Web/http_client_json.malda`, `Examples/Web/rest_bearer_jwt.malda`, `Examples/Web/auth_cookie_login.malda`
- `Examples/Web/rest_api_server.malda`, `Examples/Web/ui_form_workflow.malda`, `Examples/Web/ui_counter_dashboard.malda`
- Language API: `ReferenceManual/23-web-ui.html` (hub: `22-web-ui-hub.html`)
- `Examples/Workflows/retry_and_inspect.malda`
- `Examples/Prompts/basic_prompt.malda`, `Examples/Prompts/schema_prompt_structured.malda`, `Examples/Prompts/sum_type_intent_prompt.malda`, `Examples/Prompts/api_program_calc.malda`, `Examples/Prompts/prompt_tools_then_structured.malda`
- `Examples/Actors/basic_counter.malda`
