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
