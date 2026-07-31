# GraphMemory — Piano 3 Sprint (post Tier 3)

Baseline: commit `234d06f` — reflect, graph evolution, synapse/MMR, reindexDocuments, dual-index. 62 GraphMemory tests.

---

## Sprint 1 — CLI, docs, esempio shared memory (~1 settimana)

### Obiettivo
Rendere GraphMemory operabile da CLI e documentare la configurazione assistant senza scrivere script ad hoc.

### Deliverable

#### 1.1 CLI `malda memory`
**File:** `MaldaLang/Program.cs` (+ eventuale helper `MaldaLang/BuiltIns/MemoryCliHelpers.cs`)

Subcomandi:

| Comando | Descrizione |
|---------|-------------|
| `malda memory stats [--path PATH]` | Carica memoria da path (default `~/.malda/memory/assistant`), stampa stats JSON o testo |
| `malda memory reindex [--path PATH] [--dir DIR] [--pattern PATTERN]` | `reindexDocuments` con `changedOnly: true` |
| `malda memory prune [--path PATH] [--type TYPE] [--older-than-days N] [--consolidated]` | Wrapper `prune()` |
| `malda memory reflect [--path PATH] [--scope SCOPE] [--dry-run]` | `reflect()` o `consolidate()` se reflect disabilitato |
| `malda memory export-bundle [--path PATH] [-o FILE]` | `exportBundle` |

- Path default: `%USERPROFILE%\.malda\memory\assistant` (stesso dell'assistant).
- Inizializzare GraphMemory con `embedHash` (384) per CLI; supportare `MALDA_MEMORY_EMBED` env.
- Exit code 1 su errori; output machine-readable con `--json` dove utile.

#### 1.2 Documentazione config assistant
**File:** `ReferenceManual/26-personal-assistant.html`, `ReferenceManual/21-graph-memory.html` (sezione CLI)

Aggiornare schema `config.json` con `agents.memory`:

```json
"agents": {
  "memory": {
    "embed": "hash",
    "modelPath": "",
    "pruneEpisodicAfterDays": 30,
    "consolidateMinEpisodic": 3,
    "maxNodes": 5000,
    "reflectEnabled": false,
    "reflectMinEpisodic": 3,
    "reflectEveryNSaves": 1,
    "reflectModel": "",
    "reflectMinConfidence": 0.7,
    "kbDir": "",
    "kbPattern": "**/*.md"
  }
}
```

Documentare `MALDA_MEMORY_REFLECT`, maintenance flow (reflect vs consolidate, reindex KB).

#### 1.3 Esempio shared memory
**File:** `Examples/Memory/shared_memory.malda`

- Due agent con `useMemory(memory)` sullo stesso GraphMemory.
- `load`/`save` su path temporaneo o `enableMemory("team_memory")` (path-based).
- Dimostra che agent2 vede ciò che agent1 ha ricordato.

#### 1.4 Test Sprint 1
- Test C# per `MemoryCommand` (stats su memoria vuota/caricata) oppure test integrazione CLI via Process.
- Test MALDA esempio shared memory in `GraphMemoryTests.cs` o nuovo file.

**Commit suggerito:** `feat(cli): add malda memory subcommands and shared memory example`

---

## Sprint 2 — Reflect production-ready, test Agent, migrazione dual-index (~1 settimana)

### Obiettivo
Affidabilità in produzione: reflect usa il client configurato, Agent testato end-to-end, nodi legacy con single-vector migrati.

### Deliverable

#### 2.1 Reflect client injection
**Files:** `MemoryReflectService.cs`, `GraphMemory.cs`, `assistant.malda`

- `reflect(options)` accetta:
  - `client` — istanza LLM (OpenRouterClient / LlamaCppClient)
  - `minConfidence` (default 0.7)
  - `model` (fallback se client assente)
- Se `client` fornito, chiamare `client.complete(prompt)` invece di `new OpenRouterClientInstance(model)`.
- Assistant: passare il client già creato in `maintainOpts.client = client` (o equivalente dict-safe).
- Env `MALDA_MEMORY_REFLECT_MIN_CONFIDENCE` opzionale.

#### 2.2 Test integrazione Agent
**File:** `MaldaLang.Tests/GraphMemoryTests.cs`

Nuovi test:

| Test | Verifica |
|------|----------|
| `Agent_ExcludeNodeIds_AvoidsRepetition` | Due think consecutivi non ripetono stessi node id in injection |
| `Agent_WorkingMemory_MergesRecentAndSemantic` | getRecent + query merge nel prompt |
| `Reflect_UsesInjectedClient` | Mock/injected facts path già presente; aggiungere test client option se fattibile |

#### 2.3 Migrazione dual-index su load
**File:** `GraphMemory.cs` — `CallLoad` / post-load hook

- `migrateDualIndex(options?)` o automatico in `load()`:
  - Per nodi con solo vector singolo, generare head (fact) + body (fact+context) se mancanti.
  - Flag metadata `dualIndexMigrated: true` per evitare re-processing.
- Opzione `load({ migrateDualIndex: true })` (default true).
- Test: load memoria legacy → query usa entrambi i vettori.

#### 2.4 Stats arricchite (base)
**File:** `GraphMemory.cs` — `CallStats`

Aggiungere: `supersededCount`, `lastReflectAt` (metadata store o file sidecar), `dualIndexPending`.

**Commit suggeriti:**
- `feat(graph-memory): inject LLM client into reflect and configurable minConfidence`
- `feat(graph-memory): dual-index migration on load and enriched stats`
- `test(agent): add working memory and excludeNodeIds integration tests`

---

## Sprint 3 — Rerank LLM, conflict resolution, KB watch (~1–2 settimane)

### Obiettivo
Retrieval di qualità superiore e manutenzione automatica KB senza dipendere solo da save().

### Deliverable

#### 3.1 LLM rerank (top-K)
**Files:** `GraphMemory.cs`, eventuale `MemoryRerankService.cs`

- `query(options)` nuove opzioni:
  - `rerank: true|false` (default false)
  - `rerankTopK: 20` — candidati da vector+lexical prima del rerank
  - `rerankModel` / `rerankClient` — come reflect
- Pipeline: retrieval → top 20 → LLM rank JSON `[{ "nodeId", "score" }]` → reorder results.
- Test injection: `rerankScores: [{ nodeId, score }]` bypass LLM.
- Fallback: ordine originale se rerank fallisce.

#### 3.2 Conflict resolution su reflect
**Files:** `GraphMemory.cs`, `MemoryReflectService.cs`

Quando reflect crea fatti che matchano semantic esistente (similarity > soglia, es. 0.85):

1. Confrontare `confidence` nuovo vs vecchio.
2. Se nuovo >= vecchio: edge `supersedes` (nuovo→vecchio), abbassare importance vecchio.
3. Se nuovo < vecchio: skip o creare con `confidence` ridotta + link `contradicts` (opzionale).
- Opzione `reflect({ resolveConflicts: true })` default true.
- Test: `Reflect_SupersedesConflictingSemantic_WhenHigherConfidence`

#### 3.3 KB file watch
**Files:** `MaldaLang/BuiltIns/KbWatchService.cs` (new), `assistant.malda`, opzionale CLI

- Se `config.agents.memory.kbDir` impostato:
  - `FileSystemWatcher` su directory (debounce 2s).
  - Su change: `reindexDocuments(kbPattern, kbDir, { changedOnly: true })`.
- Avvio watch in assistant all'avvio; stop su exit.
- CLI: `malda memory watch [--dir DIR] [--pattern PATTERN] [--path PATH]` — processo long-running (opzionale).
- Test: scrivere file in temp dir, trigger reindex, verificare chunk indicizzato (sync test con debounce corto in test hook).

#### 3.4 `forgetByScope` / `forgetByCategory`
**API:**
```malda
memory.forgetByScope(scope, options?)
memory.forgetByCategory(category, options?)
```
- Wrapper su prune/forget batch.
- Utile per reset chat Telegram.

#### 3.5 Docs + test finali
- `21-graph-memory.html`: rerank, conflict resolution, KB watch, forgetBy*.
- Target: **75+** GraphMemory tests, tutti verdi.

**Commit suggeriti:**
- `feat(graph-memory): add optional LLM rerank for query results`
- `feat(graph-memory): conflict resolution on reflect with supersedes`
- `feat(graph-memory): KB directory watch and forgetByScope/Category`
- `docs(graph-memory): document Sprint 3 retrieval and maintenance APIs`

---

## Ordine di esecuzione

```
Sprint 1 → Sprint 2 → Sprint 3
```

Dipendenze:
- Sprint 2 reflect client beneficia di docs Sprint 1.
- Sprint 3 rerank riusa pattern client injection Sprint 2.
- KB watch complementa `reindexDocuments` Sprint 1 CLI.

## Completato (P6)

- Scope gerarchici multi-livello da `agents.memory.scopeHierarchy` + `agent.setMemoryScopeHierarchy()`
- Env `MALDA_MEMORY_SCOPE_HIERARCHY` (comma-separated o JSON array)
- ONNX cross-encoder rerank (`rerankMode: onnx`, `rerankModelPath`, `MALDA_MEMORY_RERANK_MODEL_PATH`) con fallback a `cross`

## Completato (P7 parziale)

- Rerank in `Agent.think()` via `setMemoryRerank()` + `agents.memory.rerank*` config
- Ralph: `enableCodeMemory()` su GraphMemory esistente + `MALDA_RALPH_CODE_MEMORY`
- Gateway cron avanzato (`*/N`, liste, monthly, multi-weekday)

## Completato (P8)

- Download/bundle ONNX cross-encoder (`malda memory download-rerank`, `malda onboard --download-rerank`)
- Cache default in `~/.malda/models/cross-encoder` con auto-resolve in `GraphMemory` rerank
- Test integrazione ONNX opt-in (`MALDA_RUN_ONNX_INTEGRATION=1`)
- `malda onboard` arricchito (memory/telegram config, `--download-local-llama`, checklist)

## Completato (P9)

- `malda doctor` esteso: gateway, GraphMemory, ONNX rerank, skills, Telegram
- Skill template `greeting.malda` installato da `malda onboard` (tool + sub-agent)
- Notifiche gateway: Telegram alert su cron falliti/crash, log `gateway-alerts.log`, marker `gateway-crash.json`

## Fuori scope (backlog)

- Canali WhatsApp / Feishu

## Verifica

Dopo ogni sprint:
```powershell
dotnet build MaldaLang
dotnet test MaldaLang.Tests\MaldaLangTests.csproj --filter FullyQualifiedName~GraphMemoryTests
```

Non committare automaticamente; l'utente committa su richiesta.
