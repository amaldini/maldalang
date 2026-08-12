# Prompt examples

Prompt blocks, agents, and typed structured output.

## Run

```bash
# From repo root — offline (builds PromptInstance only)
malda Examples/Prompts/basic_prompt.malda
malda Examples/Prompts/prompt_return_type.malda
malda Examples/Prompts/prompt_tools_mode.malda
malda Examples/Prompts/prompt_tools_then_structured.malda

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
| `prompt_tools_then_structured.malda` | **C Sequence** | Offline recipe: tools gather, then typed prompt without tools |
| RAG / agent demos | — | Need LLM provider |

See [`docs/spec/backend-capability-matrix.md`](../../docs/spec/backend-capability-matrix.md)
and gotchas on tools vs `response_format` in [`docs/llm/malda-gotchas.md`](../../docs/llm/malda-gotchas.md).
