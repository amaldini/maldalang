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

Packed ASK (`embed:`) is read-only: no UPDATE, no `/admin/upload`, and sources
are not packed into the exe.

## Source snapshot (disk brains)

On a successful BUILD or UPDATE from a **live** docs folder, each scanned file
is copied into `brain/sources/` (`io.copyFile`, binary-safe for PDF/DOCX).
`brain.json` keeps `sourceFolder` as the live path and records
`sourceSnapshot: "sources"`. Snapshot files whose relative path is no longer
in the scan are deleted (always; `--remove-orphans` still only drops notes).

UPDATE and `/admin/upload` resolve the docs root in this order:

1. `--docs` if set and the directory exists (if `--docs` is set but missing, fail — do not fall through)
2. `catalog.sourceFolder` if that directory still exists
3. `brain/sources/` if that directory exists
4. else fail (hint: run BUILD/UPDATE once with `--docs` to populate the snapshot)

Copying a disk brain to another PC therefore still allows UPDATE/upload against
the snapshot. Upload accepts `.md` / `.html` / `.htm` / `.pdf` / `.docx` (same
types as BUILD). If neither live docs nor a snapshot exist, upload creates
`sources/` so the first file can land. Packed ASK (`embed:`) stays read-only.

## ASK citations (P1)

Answers may contain `[nota: slug]`. The ASK UI rewrites those to in-page links
(`#src-{slug}`), marks cited source chips, and opens a note preview (`GET /note`).
When the original file exists, an **Original** link opens `GET /source?slug=`
in a new tab: `brain/sources/` snapshot first, then the live `sourceFolder` if
that folder is not the brain dir. Packed ASK (`embed:`) has no originals.

`POST /ask` flushes the pending panel fragment immediately (`res.fragment`) so
the browser is not blocked on the LLM. The finished panel is pushed on SSE
event `ask-panel`. Live progress still uses `@LIVE("/ask/live")`. The live
dock **Stop** button POSTs `/ask/stop` for this conversation (cookie JWT) and
calls `cancelThink` so GGUF/HTTP generation aborts. It does not stop the ASK
server (`pendingStop` remains console-only).

## Retrieval eval (P1)

Offline lexical gold set: [`eval/questions.json`](eval/questions.json) +
[`eval/catalog.json`](eval/catalog.json). Runner:
`malda Examples/Agents/sb/eval/run_retrieval_eval.malda` (no LLM).

## Conversation-aware retrieval (P1)

ASK retrieves with the raw question first. If that is weak/empty and the
conversation already has completed turns, `runAskTurn` retries once with
`expandAskRetrievalQuery` (current question + last 1–2 questions + last
source titles, capped). The LLM still sees the raw question. Pending ASK
placeholders are skipped. A strong first hit (topic switch) is not expanded.
Audit field: `expanded`.

## Semantic rerank (P1)

GraphMemory ASK reranks hybrid hits with `rerankMode: cross` by default
(local, no extra model). If `model.onnx` + `vocab.txt` are present
(`malda memory download-rerank` or `MALDA_MEMORY_RERANK_MODEL_PATH`), ASK
uses `onnx`. CLI: `--rerank off|cross|onnx` (env `MALDA_BRAIN_RERANK`).
No-op on the lexical host. LLM rerank is not used.

## ASK live draft (P1)

When `onAgentProgress(liveChannel)` is set, `think()` streams answer tokens
as `{ phase: "draft", text }` over the existing ASK SSE (content kind only,
throttled). The UI `#ask-live-draft` pane already listens for this phase.

## GraphMemory UPDATE (semantic host)

`indexBrainAfterFinalize` receives `{ mode, forceFull, catalog, removedNodeIds }`.
Unchanged notes keep `memoryNodeId` and are skipped; new/changed notes are upserted;
deleted notes are `forget`n. Full rebuild when `--reindex-memory`, artifacts are
missing, the embed fingerprint (`embedMode` / `embedDim`) changed, or the catalog
has no `memoryNodeId`s (upgrade from older brains).
