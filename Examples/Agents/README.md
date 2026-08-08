# Agent examples

Agents, tools, multi-agent workflows, and larger demos such as `secondbrain.malda`.

## Run

```bash
malda Examples/Agents/single_agent_code_generator.malda
```

## Dependencies

Most examples need an LLM provider:

- `OPENROUTER_API_KEY`, or
- `providers.openrouter` in `~/.malda/config.json`, or
- the local GGUF fallback (downloaded on first use)

See root README “LLM access”. Offline-safe samples are rare here — check each entry’s `requires` in [`metadata.json`](metadata.json).

For the PRD-driven autonomous loop, see [`../RalphWiggum/README.md`](../RalphWiggum/README.md).
