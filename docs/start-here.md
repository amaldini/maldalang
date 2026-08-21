# Start Here

MALDA is easiest to learn when you choose the path that matches what you are trying to do first. The platform fits into four connected layers:

- **MALDA Core**: language syntax, runtime, compiler/transpiler, test workflow, and IDE foundations
- **MALDA AI**: prompt blocks, LLM clients, agents, tools, and MCP integration
- **MALDA Web**: REST APIs, routed pages, browser JavaScript output, and full-stack starters
- **MALDA Workflow/Cloud**: durable workflows, scaffolding, deploy validation, and operational baselines

**Editors:** Desktop IDE = full Windows IDE; Web IDE = browser learning playground (not Desktop parity); VS Code + LSP = cross-platform editing. See the root `README.md` IDE section.

## What MALDA looks like

If you already program, run this first. It needs **no API key**:

```bash
malda Examples/Basics/first_look.malda
```

That file binds a `prompt` to a `schema` with `-> Review`. Without `await`, the call is a rendered template (testable, no model call). `validate("Review", …)` is the same check `await` would run on the model JSON. Type annotations elsewhere are IDE/LSP hints, not runtime checks.

Then pick a path below. If you are new to programming, start with **Learn Programming**.

Use the route below that matches your immediate goal.

## 1. Learn Programming

Choose this path if you are new to programming, new to MALDA syntax, or want to teach the basics first.

Start with:

1. `Examples/Basics/first_look.malda` (characteristic constructs; skip if you just want syntax)
2. `Examples/Basics/hello_world.malda`
3. `Examples/Basics/variables_arithmetic.malda`
4. `Examples/Basics/input_example.malda`
5. `Examples/Basics/conditionals.malda`
6. `Examples/Basics/while_loop.malda`
7. `Examples/Basics/for_loop.malda`
8. `Examples/Basics/functions.malda`
9. `Examples/Basics/lambda.malda`

Why this path:

- It builds MALDA Core fundamentals before agents, APIs, or workflows
- The examples are short enough to run, modify, and compare side by side

## 2. Build An AI App

Choose this path if you already know the syntax and want to work with prompts, agents, and tool calling.

Start with:

1. `Examples/Prompts/basic_prompt.malda`
2. `Examples/Prompts/prompt_with_agent.malda`
3. `README.md` section `Creating Your First AI Agent`

This path emphasizes **MALDA AI** on top of MALDA Core.

**Autonomous PRD loop (advanced):** `Examples/RalphWiggum/` ships the Ralph Wiggum reference loop and a Snake demo. Requires an OpenRouter API key (or configured provider). Read `Examples/RalphWiggum/README.md` or run `Examples/RalphWiggum/snake-demo/run-ralph.bat` from the repo root on Windows.

## 3. Build An API Or Full-Stack App

Choose this path if you want a runnable starter that shows MALDA's web surface and project scaffolding.

Start with one of these commands:

```bash
malda new webapi my-api
malda new fullstack my-app
```

Then in the generated project:

1. Read the generated `README.md`
2. Run `malda test`
3. Run the generated app entry point
4. Open the health endpoint or sample UI path described in the template readme

What to expect today:

- `webapi` is the smaller starting point for API-first services
- `fullstack` is the better starting point if you want both API routes and a small server-driven UI sample
- The starters include test/config/deploy baselines, but they are still scaffolds, not finished product stacks
- In Desktop IDE you can keep one physical `app.malda` and opt into virtual tabs by adding `// @malda-section Name` separators; `include` files still work as regular physical files

This path emphasizes **MALDA Web** plus the scaffolding and deploy baseline from **MALDA Workflow/Cloud**.

For sessions, cookie login, CSRF forms, and the lightweight job queue, follow the end-to-end
walkthrough: [`docs/tutorials/fullstack-sessions-auth.md`](tutorials/fullstack-sessions-auth.md).

For tagged `catch`, `result`/`option`, and `schema`/`validate`, see
[`docs/tutorials/errors-and-validation.md`](tutorials/errors-and-validation.md).

## 4. Build a Browser Game

Choose this path if you want a canvas `game.*` loop in the browser (Love2D / Pico-8 / Phaser territory, not Unity).

```bash
malda new game my-game
cd my-game
malda play app.malda
```

`malda play` compiles `--mode js`, copies `malda-js-runtime.js`, writes a host page, and prints a local URL. Ctrl+C stops the server. Add `--open` to launch a browser when the OS allows it. Put images in `assets/` so relative `game.loadImage("assets/...")` works in the preview.

What to expect today:

- JavaScript backend only — do not run the starter with the interpreter
- `malda play` is the inner loop; itch.io-style packaging is still `malda compile --mode pwa -o dist`
- Samples and smoke hosts live under `Examples/Games/`

This path emphasizes **MALDA Web** (browser JS) plus the canvas API in chapter 26 of the Reference Manual.

## 5. Build Workflows

Choose this path if your first use case is durable background processing, instance inspection, retries, and operational workflow tooling.

Start with:

- `README.md` section `Durable Workflow Operations (Sprint 7)`
- `README.md` section `Deploy Skeleton (Sprint 8 Baseline)`

Key commands:

```bash
malda workflow list --status FAILED --limit 50
malda workflow dlq list --pending-only --limit 100
malda workflow maintenance run --dry-run --format json
```

This path emphasizes **MALDA Workflow/Cloud**.

## Which Path Should I Pick?

- Run `Examples/Basics/first_look.malda` first if you want the characteristic constructs before a tutorial
- Choose `Learn Programming` if the syntax is new
- Choose `Build An AI App` if prompts and agents are your first priority
- Choose `Build An API Or Full-Stack App` if you want a scaffold you can extend immediately
- Choose `Build a Browser Game` if you want a canvas `game.*` loop (`malda new game` / `malda play`)
- Choose `Build Workflows` if durable operations are the main reason you are evaluating MALDA
- Choose `Share Code With Workspace Packages` if you are splitting libs across `packages/` or file modules

## 6. Share Code With Workspace Packages

Choose this path if you want multi-file libraries without publishing to a registry.

Start with:

1. `Examples/Modules/selective_import.malda` (file module + selective import)
2. `Examples/Modules/workspace_package.malda` (repo `packages/malda-demo-math`)
3. [`docs/workspace-packages.md`](workspace-packages.md)

Quick checks from the repo root:

```bash
malda Examples/Modules/workspace_package.malda
malda list --workspace
```

Put your own libs under `packages/<name>/`. Installed copies live in `~/.maldalang/packages` if you run `malda install ./path`. There is no public MALDA package hub yet.

## Useful Reference Docs

- Root overview and command reference: `README.md`
- Language reference: `ReferenceManual/`
- Profiling guide: `docs/profiling.md`
- Debugging transpile failures: `docs/debugging-transpile.md`
- Interpret-mode debug (`malda debug-adapter`): `docs/debugging-interpret.md`
- Native numeric transpile rollout: `docs/native-numeric-rollout.md`
- JavaScript/browser backend notes: `docs/javascript-backend.md`
- Server-driven UI runtime / UIHost: `docs/ui-framework.md`
- Workspace packages: `docs/workspace-packages.md`
