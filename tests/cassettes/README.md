# LLM cassettes

Record and replay `Chat` traffic for hermetic evals and ship-contract pairs.

```bash
MALDA_RECORD=tests/cassettes/name.jsonl   malda Examples/path.malda
MALDA_REPLAY=tests/cassettes/name.jsonl   malda Examples/path.malda
MALDA_REPLAY_STRICT=1                    malda Examples/path.malda
```

Secrets (`Authorization`, API keys, obvious secret-shaped strings) are redacted on record.
A miss under `MALDA_REPLAY_STRICT=1` fails; otherwise the host warns, journals `cassette_miss`, and falls through to a live call.

`Examples/Prompts/prompt_tools_then_structured.malda` and `Examples/Agents/agent_governance_golden.malda` are offline pair programs (no live `await`). Record here when you convert a live-await example.
