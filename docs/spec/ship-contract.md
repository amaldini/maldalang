# Ship contract registry

**Status:** Active (post-DT7)  
**Guard:** [`MaldaLang.Tests/ShipContractGuardTests.cs`](../../MaldaLang.Tests/ShipContractGuardTests.cs)

A host program that claims to ship (`malda compile --mode transpile` / `publish`)
must have an **oracle**, not only a successful compile. JavaScript / PWA stay on
the [backend capability matrix](backend-capability-matrix.md); they are not this
contract.

| Kind | Meaning |
|------|---------|
| `pair` | Same `.malda` → same stdout when interpret and C# transpile both exit 0. Mixed success/failure fails. Oracle: [`InterpretTranspilePairTests`](../../MaldaLang.Tests/InterpretTranspilePairTests.cs). |
| `trace` | Structured outcome (workflow journal today). Not stdout. |
| `n/a` | Compile-only smoke, or excluded. **Notes must name the reason** (`llm-await`, `too-large`, `relative-cwd`, `js-only`, `http-trace pending`, …). |

Inline pair fixtures (interpolation, `cap.*` abs-path, nested `result.map`, dict
`append`+`length`, `--typed-transpile-level 2`, `error()` failure identity) live
in the pair suite and do not need a path row.

Templates (`malda new`) and README showcases must appear below even when `n/a`.

| Path | Kind | Oracle | Notes |
|------|------|--------|-------|
| `Examples/Basics/hello_world.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Basics/first_look.malda` | pair | InterpretTranspilePairTests | README characteristic file |
| `Examples/Basics/schema_validate.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Basics/schema_sumtype_validate.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Basics/as_variant.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Basics/schema_nested_validate.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Basics/sumtype_typed_payloads.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Basics/async_all_example.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Prompts/eval_prompt.malda` | pair | InterpretTranspilePairTests | offline `evalPrompt` |
| `Examples/Prompts/api_program_calc.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Prompts/prompt_budget.malda` | pair | InterpretTranspilePairTests | no `await` |
| `Examples/Prompts/multimodal_attachments.malda` | pair | InterpretTranspilePairTests | builds instance only |
| `Examples/Prompts/prompt_tools_then_structured.malda` | n/a | TranspileSmokeTests | llm-await |
| `Examples/Agents/phase6_pure_validate.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Agents/agent_governance_golden.malda` | n/a | TranspileSmokeTests | llm-await |
| `Examples/Agents/secondbrain_semantic.malda` | n/a | — | too-large; README showcase |
| `Examples/RalphWiggum/RalphWiggum.malda` | n/a | — | too-large; llm-await; README showcase |
| `Examples/MCP/mcp_schema_tool.malda` | pair | InterpretTranspilePairTests | |
| `docs/llm/few-shot/28_api_program_prompt.malda` | pair | InterpretTranspilePairTests | |
| `docs/llm/few-shot/31_mcptool_schema.malda` | pair | InterpretTranspilePairTests | |
| `docs/llm/few-shot/32_mcptool_validate.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Modules/selective_import.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Modules/export_type_schema.malda` | pair | InterpretTranspilePairTests | |
| `Examples/Workflows/simple_step.malda` | trace | WorkflowTranspilerParityTests | journal snapshot is inline; this file is smoke |
| `Examples/Workflows/determinism_helpers.malda` | trace | WorkflowTranspilerParityTests | WF1001/WF1002; file is smoke |
| `Examples/Workflows/runprogram_in_step.malda` | n/a | TranspileSmokeTests | smoke + interpreter; pair would need a journal fixture |
| `Examples/Web/job_queue_basic.malda` | n/a | TranspileSmokeTests | jobs-cwd; generated ids |
| `Examples/Memory/grounded_ask.malda` | n/a | TranspileSmokeTests | graphmemory-score |
| `Examples/Tools/capability_tokens.malda` | n/a | TranspileSmokeTests | relative-cwd; abs-path cap fixtures are inline pairs |
| `Examples/VectorDB/basic_vectordb.malda` | n/a | TranspileSmokeTests | inline VectorDB pair covers the contract |
| `Templates/agent/app.malda` | n/a | — | relative-cwd; `malda new agent`; cap pair fixtures cover the contract |
| `Templates/webapi/app.malda` | n/a | — | http-trace pending |
| `Templates/fullstack/backend/app.malda` | n/a | — | http-trace pending |
| `Templates/game/app.malda` | n/a | — | js-only |
| `Templates/game-fullstack/app.malda` | n/a | — | fullstack; do not interpret |

Landing a new host construct that claims interpret + C# agree: add a `pair` or
`trace` row (or `n/a` with a one-line reason) in the same PR. Do not weaken
pairs to `Contains`.
