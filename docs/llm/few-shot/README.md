# Few-shot snippets

*Applies to: MALDA 1.0.14*

Tiny programs for LLM context. Larger curated samples live under `Examples/`.
After generating a snippet, diagnose with `malda check path.malda --json` before running.

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
| `25_as_variant.malda` | `asVariant("Intent", dict)` after `validate` → variant + `match` |
| `26_tool_cap_read.malda` | `validate` tool args → `cap.confine` (host-minted root; relative path only) |
| `27_eval_prompt.malda` | `evalPrompt` / `instance.eval` offline fixture in/out (no LLM) |
| `28_api_program_prompt.malda` | `prompt … -> program(Api)` + `evalPrompt` fixture + `runProgram` |
| `29_runprogram_in_step.malda` | `step result = runProgram(prog)` (durable; plan JSON is input) |
| `31_mcptool_schema.malda` | `@MCPTool` third arg is a schema name; `getTools()` prints `inputSchema` |
| `32_mcptool_validate.malda` | `MCPServer.callTool` host-validates args against the attached schema |
| `33_check_malda.malda` | `createCheckMaldaTool` + `execute` on a snippet (same report as `malda check --json`) |
| `34_agents_team.malda` | `agents.team` + graph `rel`/`contract` + `handoff` (offline, no LLM) |
| `35_agents_kind.malda` | `agents.define` `kind: "CodingAgent"` (offline, no LLM) |
| `36_agents_plan.malda` | `executePlan(plan, team)` rejects an undeclared role hop (offline) |
| `37_agents_handoff_think.malda` | `team.handoff(..., { think: false })` stays validate-only (offline) |
| `38_agents_review_reject.malda` | `team.review` / `team.reject` require matching `rel` (offline) |
| `39_agents_plan_verdict.malda` | `executePlan` `think: false` + `approved: false` runs the reject hop |
| `40_agents_consult.malda` | `team.consult` requires `rel: consult` (offline) |

Also useful from the main tree:

- `Examples/Basics/errors_and_match.malda`, `Examples/Basics/schema_validate.malda`, `Examples/Basics/as_variant.malda`, `Examples/Basics/modules_include.malda`, `Examples/Basics/modules_import.malda`
- Recipe: [`docs/tutorials/errors-and-validation.md`](../tutorials/errors-and-validation.md)
- `Examples/Testing/unit_test_basics.test.malda`
- `Examples/Web/http_client_json.malda`, `Examples/Web/rest_bearer_jwt.malda`, `Examples/Web/auth_cookie_login.malda`
- `Examples/Web/rest_api_server.malda`, `Examples/Web/ui_form_workflow.malda`, `Examples/Web/ui_counter_dashboard.malda`
- Language API: `ReferenceManual/24-web-ui.html` (hub: `23-web-ui-hub.html`)
- `Examples/Workflows/retry_and_inspect.malda`
- `Examples/Prompts/basic_prompt.malda`, `Examples/Prompts/schema_prompt_structured.malda`, `Examples/Prompts/sum_type_intent_prompt.malda`, `Examples/Prompts/api_program_calc.malda`, `Examples/Prompts/prompt_tools_then_structured.malda`, `Examples/Prompts/eval_prompt.malda`, `Examples/Prompts/multimodal_attachments.malda`
- `Examples/Workflows/runprogram_in_step.malda` (`evalPrompt` then `step result = runProgram`)
- `Examples/Actors/basic_counter.malda`
