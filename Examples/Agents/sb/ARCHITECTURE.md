# Second Brain module layout

Entry points: [`../secondbrain.malda`](../secondbrain.malda) (lexical ASK) and
[`../secondbrain_semantic.malda`](../secondbrain_semantic.malda) (GraphMemory ASK).

## Include order (required)

1. `00-i18n.malda` — `t()`, product name, language prompt
2. `import "secondbrain_cli_lib.malda"` then `include "secondbrain_cli_apply_lib.malda"`
3. `01-llm.malda` — OpenRouter client, agents, `tryThink`
4. `02-text.malda` — path/slug/HTML/PDF/DOCX helpers
5. `03-distill.malda` — taxonomy, note files, catalog/index writers
6. Semantic only: `06-memory.malda` (GraphMemory + embeddings)
7. Host hooks (`addHostAskTools`, `selectNotesForAsk`, `indexBrainAfterFinalize`, …)
8. `04-build.malda` — BUILD / UPDATE
9. `05-ask-common.malda` — retrieval + `runAskTurn` / `generateDocumentCli`
10. `include "secondbrain_ask_ui_lib.malda"` then host `askBrain` / PACK / menu

Hosts keep config (`EMBED_ALIAS`, ports, pack `-o`) and ASK/PACK/menu dispatch.

## ASK citations (P1)

Answers may contain `[nota: slug]`. The ASK UI rewrites those to in-page links
(`#src-{slug}`), marks cited source chips, and opens a note preview (`GET /note`).

## Retrieval eval (P1)

Offline lexical gold set: [`eval/questions.json`](eval/questions.json) +
[`eval/catalog.json`](eval/catalog.json). Runner:
`malda Examples/Agents/sb/eval/run_retrieval_eval.malda` (no LLM).

## Semantic rerank (P1)

GraphMemory ASK reranks hybrid hits with `rerankMode: cross` by default
(local, no extra model). If `model.onnx` + `vocab.txt` are present
(`malda memory download-rerank` or `MALDA_MEMORY_RERANK_MODEL_PATH`), ASK
uses `onnx`. CLI: `--rerank off|cross|onnx` (env `MALDA_BRAIN_RERANK`).
No-op on the lexical host. LLM rerank is not used.

## GraphMemory UPDATE (semantic host)

`indexBrainAfterFinalize` receives `{ mode, forceFull, catalog, removedNodeIds }`.
Unchanged notes keep `memoryNodeId` and are skipped; new/changed notes are upserted;
deleted notes are `forget`n. Full rebuild when `--reindex-memory`, artifacts are
missing, the embed fingerprint (`embedMode` / `embedDim`) changed, or the catalog
has no `memoryNodeId`s (upgrade from older brains).
