# Agent examples

Agents, tools, multi-agent workflows, and larger demos such as `secondbrain.malda`.

## Run

```bash
malda Examples/Agents/single_agent_code_generator.malda
```

## Governance golden (offline)

Prefer this pattern for tool / LLM-shaped payloads before side effects:

```bash
malda Examples/Agents/agent_governance_golden.malda
```

Uses `schema` + `validate()` + `@pure` helpers + `@effects("print")` on the impure handler.
Unit twin: `phase6_pure_validate.malda`. Structured prompt modes: `Examples/Prompts/` (Mode A / Mode C).

## Dependencies

Most examples need an LLM provider:

- `OPENROUTER_API_KEY`, or
- `providers.openrouter` in `~/.malda/config.json`, or
- the local GGUF fallback (downloaded on first use)

See root README “LLM access”. Offline-safe samples are rare here — check each entry’s `requires` in [`metadata.json`](metadata.json).

For the PRD-driven autonomous loop, see [`../RalphWiggum/README.md`](../RalphWiggum/README.md).
