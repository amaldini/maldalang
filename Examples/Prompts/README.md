# Prompt examples

Prompt blocks, agents, and typed structured output.

## Run

```bash
# From repo root — offline (builds PromptInstance only)
malda Examples/Prompts/basic_prompt.malda
malda Examples/Prompts/prompt_return_type.malda

# Needs a configured LLM (default local client or API key)
malda Examples/Prompts/schema_prompt_structured.malda
malda Examples/Prompts/prompt_with_agent.malda
```

## Notes

| Example | Notes |
|---------|--------|
| `basic_prompt.malda` | Offline; prints prompt fields |
| `prompt_return_type.malda` | Offline; `-> Type` without `await` |
| `schema_prompt_structured.malda` | `requires: api-key`; `schema` + `await` → `response_format` |
| RAG / agent demos | Need LLM provider |

See [`docs/spec/backend-capability-matrix.md`](../../docs/spec/backend-capability-matrix.md)
and gotchas on tools vs `response_format` in [`docs/llm/malda-gotchas.md`](../../docs/llm/malda-gotchas.md).
