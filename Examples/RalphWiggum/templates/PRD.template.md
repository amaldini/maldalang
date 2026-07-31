# PRD — {{PROJECT_TITLE}}

## Visione prodotto

{{VISION}}

**Vincoli non negoziabili**
- {{CONSTRAINTS}}

**Metriche di successo**
- {{METRICS}}

---

## Checklist feature (priorità alta → bassa)

Implementare **una feature per iterazione**. Segnare `[DONE]` solo quando la feature è completa, la validazione Ralph passa, ed è testabile.

- [TODO] [P0] **F1 — First feature** (depends: none)
  - Files: {{PRIMARY_FILES}}
  - Verify: {{VERIFY_FILES}}
  - Acceptance: {{ACCEPTANCE_F1}}

- [TODO] [P1] **F2 — Next feature** (depends: F1)
  - Acceptance: {{ACCEPTANCE_F2}}

---

## Note per l'agente

- Lavorare **solo** nella directory del progetto.
- Una feature per iterazione Ralph; non segnare `[DONE]` se la validazione fallisce.
- Quando tutte le voci aperte sono `[DONE]`, rispondere con `TASK_COMPLETE`, `RALPH_DONE`, o `{"ralph":"done"}`.
- Opzionale: copiare `templates/ralph-validate.bat.sample` in `.ralph-validate.bat` e adattare i test.
