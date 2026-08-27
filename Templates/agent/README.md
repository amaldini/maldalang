# __PROJECT_NAME__ (MALDA agent tools)

Offline-first starter for a file tool that **cannot invent a path**. `@effects("io")` is only a name allow-list — a tool that takes `path: string` can still read `/etc/passwd`. This scaffold mints an unforgeable `cap.fileRead` token for `notes/` on the host. The model (if you later add an `Agent`) only passes a **relative path**. `cap.confine` rejects `..` and absolute paths. JSON / `{ kind, path }` dicts are not tokens.

## Run This First

```bash
malda app.malda
```

Expected output (no API key):

```text
Welcome to your MALDA agent workspace.
true
true
```

That is: a valid read, an escaped `../secret.txt` (throws → `true`), and a forged dict (throws → `true`).

```bash
malda test
```

`--local-first` does not apply (no SQLite / `malda db` / environment profiles).

## What this starter includes

- `tools.malda`: `schema NoteArgs` + `readNoteWith(rootCap, relativePath)` — `validate` then `cap.confine` then `cap.read`
- `app.malda`: host-mints `notesRoot` with `getProgramDirectory()`, `@Tool("read_note", …)` wrapping the helper, plus the offline demo
- `notes/welcome.txt`: the only file the demo is allowed to read
- `tests/cap_tools.test.malda`: same three checks via `assert` (`malda test`)

Do not pass a capability token through LLM JSON. Mint the workspace root in host code; give the model a relative path only.

Live agents: add an `LLMClient` / `Agent` and `agent.addTool("read_note")` when you have a provider. The tool handler is already registered. See `Examples/Agents/agent_governance_golden.malda` for `validate` + `@pure` / `@effects`, and `Examples/Tools/capability_tokens.malda` for the token contract.
