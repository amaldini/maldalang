# Prompt examples

Prompt blocks, agents, and typed structured output.

## Run

```bash
# From repo root — offline (builds PromptInstance only)
malda Examples/Prompts/basic_prompt.malda
malda Examples/Prompts/prompt_return_type.malda
malda Examples/Prompts/prompt_tools_mode.malda
malda Examples/Prompts/prompt_tools_then_structured.malda
malda Examples/Prompts/prompt_budget.malda
malda Examples/Prompts/api_program_calc.malda
malda Examples/Prompts/eval_prompt.malda
malda Examples/Prompts/multimodal_attachments.malda

# Needs a configured LLM (default local client or API key)
malda Examples/Prompts/schema_prompt_structured.malda
malda Examples/Prompts/prompt_with_agent.malda
```

## Notes

| Example | Mode | Notes |
|---------|------|--------|
| `basic_prompt.malda` | — | Offline; prints prompt fields |
| `prompt_return_type.malda` | — | Offline; `-> Type` without `await` |
| `schema_prompt_structured.malda` | **A Structured** | `requires: api-key`; `schema` + `await`, no tools → `response_format` |
| `prompt_tools_mode.malda` | **B Tools** | Offline; `tools:` listed → no format/appendix |
| `prompt_tools_then_structured.malda` | **C Gather-then-extract** | Offline: one prompt with `gather:` + `-> Type` |
| `prompt_budget.malda` | — | Offline: `@budget` + `@within` on a typed prompt |
| `api_program_calc.malda` | — | Offline: closed `api` + program JSON + `runProgram` |
| `eval_prompt.malda` | — | Offline: `evalPrompt` / `instance.eval` fixture in/out (schema + sum type) |
| `multimodal_attachments.malda` | — | Offline: `attachments:` image/pdf metadata (`user` stays a string) |
| RAG / agent demos | — | Need LLM provider |

Durable `runProgram` in a `step`: [`Examples/Workflows/runprogram_in_step.malda`](../Workflows/runprogram_in_step.malda).

See [`docs/spec/backend-capability-matrix.md`](../../docs/spec/backend-capability-matrix.md)
and gotchas on tools vs `response_format` in [`docs/llm/malda-gotchas.md`](../../docs/llm/malda-gotchas.md).
