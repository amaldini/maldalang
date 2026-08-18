# MALDA — public announcement copy

English copy for announcing MALDA (Hacker News "Show HN", Lobsters,
r/ProgrammingLanguages, r/dotnet, a blog post). Claims are tied to code in this
repository, and known weaknesses are stated up front.

Launch-only notes (title A/B, HN formatting tips, prepared thread answers, checklist)
are kept locally and are not part of this repository.

Sections:

1. [Short version — Show HN post](#1-short-version--show-hn-post)
2. [Long version — blog post](#2-long-version--blog-post)
3. [Comparison with other languages and stacks](#3-comparison-with-other-languages-and-stacks)

---

## 1. Short version — Show HN post

Submit the **repository link**, not a text post: HN does not turn urls into links in the
text field of a submission, and the FAQ is explicit — *"if you want to submit a link with
comments, just submit the link, then add a regular comment"*. A blog post as the submitted
url would also be off topic for Show HN, which wants something readers can try.

| Field | Value |
|-------|-------|
| Title | `Show HN: MALDA – a language where LLM prompts and tools are syntax` |
| URL | `https://github.com/amaldini/maldalang` |

Then post the block below as the **first comment**. It is plain text in HN comment format:
no markdown, blank lines separate paragraphs, code is indented two spaces (kept under 70
columns so it does not force horizontal scrolling), and bare urls link themselves. Copy it
verbatim.

```text
Hi HN. MALDA is a programming language where LLM prompts, tools,
agents, HTTP endpoints and durable workflows are syntax instead of
library glue. prompt, schema, actor, spawn and send are keywords;
tools, endpoints and pages are declarations the parser understands.

A prompt is a declaration. Without await you get the rendered
template (testable, no API key). With await you call the model;
-> Review validates the JSON against the schema:

  schema Review {
      summary: string;
      issues: string[];
  }

  prompt codeReview(code, language) -> Review {
      system: "You are an expert reviewer of {language}.",
      user: "Review this {language} code:\n\n{code}"
  }

  var rendered = codeReview(src, "python");
  io.print(rendered.user);

The same source runs three ways: interpreted, transpiled to C# and
built into a .NET executable, or compiled to browser JavaScript.
That last part is what macros over an existing language would not
have given me. The JS path is a real subset — no agents, servers
or workflows. Constructs compose: a @MCPTool function is the same
function an agent calls as a tool and a @POST handler invokes.

The two largest programs in the repo were written entirely by
coding agents, in a language that is in no model's training data.
Second Brain (~7,600 lines with shared libs) distills a docs
folder into a linked note tree and serves an ASK web UI; malda
compile --embed-folder ships it as a single .exe.
Examples/RalphWiggum/ is a PRD-driven coding agent (4,049 lines,
eleven files). That works because docs/llm/ is a language pack —
idioms, a parser-aligned BNF, a built-in list, few-shots. An
agent reads a few thousand tokens and writes idiomatic MALDA;
when it does not, the interpreter is the feedback loop. It does
not conjure an ecosystem, but the learning curve is no longer
the main cost of a small language.

My background is business software in C# and Java; this is the
first compiler I have written. Grammar and semantics are mine to
defend; much of the argument was shared with models. Geoff
Huntley's Ralph Wiggum loop (https://ghuntley.com/cursed/)
convinced me one person could attempt it. In place of asking for
trust: guard tests if reserved words drift from the lexer or a
built-in goes undocumented; every runnable snippet in the
reference manual executed by the test suite; a ~100-case Tier 0
matrix across backends; interpret/transpile pairs that must
match stdout. C# on .NET 8, hand-written recursive-descent
parser, ~350 built-ins, ~1,900 tests. Dual MIT OR Apache-2.0
with a runtime exception. No CLA.

Honest about where it is. Public core is 1.0.1 (Spec Final 1.0
and the toolchain share that number). Publish is the type
boundary; runtime is still dynamically typed. I run a Second
Brain instance that others use. The large systems already in
flight are not being rewritten for an experiment. Full IDE is
WPF (Windows-only); CLI and browser playground run on Linux and
macOS in CI. Durable workflows are local SQLite, not a cluster;
determinism is a deny-list of built-in names.

Longer write-up, objections, comparison table:
https://github.com/amaldini/maldalang/blob/main/docs/announcement.md

Happy to answer "why not a library?", "why not macros?", and
anything else.
```

---

## 2. Long version — blog post

### MALDA: prompts, tools and agents as language constructs

This did not start as a complaint about Python. I have spent years writing business software
in C#, Java and VB.NET — PDM/PLM systems, CAD integrations, integrations with ERP and other
business systems, data processing, the kind of system where a process waits three days because
somebody has to approve it. The hard part in that work is the domain and getting existing
systems to agree with each other, not language internals. **I had never written a compiler or
an interpreter before this one.** If some of the choices below look like they come from that background rather than from
programming-language research, they do.

What changed recently was not the languages, it was how much of the code I type. With coding
agents I write far less by hand than I did two years ago, and I missed being in the source. A
language turned out to be the project where being in the source still means something: an agent
will happily type a parser, but somebody still has to decide what the grammar *means*, and
those decisions are the whole job. I did not make them in isolation either. Most of the design
in here came out of long arguments with models — propose a construct, hear what it collides
with, throw it away — which makes the project its own subject: not only a language for AI work,
but an attempt at what designing a language looks like when agents write most of the code.

The specific spark was Geoff Huntley's ["Ralph Wiggum" technique](https://ghuntley.com/ralph/)
— a coding agent in a `while true` loop — and what he pointed it at: he ran it for three
months and got [`cursed`](https://ghuntley.com/cursed/), a working esoteric language with an
LLVM backend. That was the evidence that a language is now within reach of one person with
an agent. What I wanted out of it was different: not a language built to prove the technique,
but one whose subject is the work that has taken over my days — talking to models, giving them
tools, and wiring several of them together without the program collapsing into glue.

I should be straight about where that leaves MALDA. I run a Second Brain instance that others
use. The large systems already in flight are not being rewritten for an experiment. MALDA is
still where I experiment — with the AI constructs themselves, and with how a language gets
designed now that the design conversation includes a model. The claim is that one program has
left the REPL, not that the language has earned a rewrite of everything else.

So the constructs I kept re-gluing became syntax: prompts, tools, agents, HTTP endpoints,
durable workflows. The obvious alternative was a better library, and I tried that first — a
prompt registry, a workflow wrapper, validation on top of dictionaries. It works, and it
leaves every invariant with the caller; each library also brings its own idea of what an
"agent" is, so the glue stops being plumbing and becomes translation between three opinions.
What I wanted instead was for the *parser* to know what a prompt is, so that completion,
hover, diagnostics and a second backend all came out of one definition.

There is a loop in this that I did not plan. `Examples/RalphWiggum/` — a PRD-driven coding
agent named after Huntley's technique — is where that shows up most clearly. The technique
that convinced me a language was buildable is the thing one of the language's main examples
implements. Both it and the larger Second Brain showcase were written entirely by coding
agents, in a language that appears nowhere in any model's training data — which turned out
to be the most interesting result in the project, and is the part I would argue about first.
More on that below. `Examples/Agents/secondbrain_semantic.malda` is the larger of the two by
line count (~7,600 with shared libs): same agent-and-tools surface, aimed at knowledge
instead of a coding loop — explore docs, distill linked notes, serve an ASK web UI (auth,
admin, tag filters, CLI modes), retrieve with GraphMemory, optionally embed the brain into a
published binary.

That loop is what the last two letters of the name carry. MALDA is a Multi Agent Language with
Development Automation, and the automation runs in both directions: coding agents write MALDA
programs, which is why `docs/llm/` ships a language pack for a reader that has never seen the
syntax (and why Desktop / Web IDE Ask now load that same pack from the embedded runtime),
and MALDA programs automate development work in turn — RalphWiggum is one of them doing
exactly that; Second Brain is another, for documentation rather than a PRD checklist.

In place of asking for trust on the agent-written parts: guard tests that fail the build if
the manual's reserved words drift from the lexer or a built-in is undocumented, every
runnable snippet in the reference manual executed by the test suite, a ~100-case
Tier 0 conformance matrix run across backends, and curated interpret/transpile pairs that
must produce the same stdout. That catches drift, not bad taste. Judge the taste
from the syntax below.

#### What that actually means

**A prompt is a declaration.** It has a name, parameters, and interpolation into role
sections. The parser knows about it, which means the language server knows about it too.
The smallest complete example is `Examples/Basics/first_look.malda` (offline, no API key):

```malda
schema Review {
    summary: string;
    issues: string[];
}

prompt codeReview(code, language) -> Review {
    system: "You are an expert reviewer of {language}.",
    user: "Review this {language} code:\n\n{code}"
}

var rendered = codeReview("function add(a, b) { return a + b; }", "javascript");
io.print(rendered.user);
```

Calling it without `await` gives you the rendered prompt object, which is handy for tests
and for inspection. Calling it with `await` sends it to the configured model and, when the
prompt is bound with `-> Review`, validates the JSON against the schema. Prompt
parameters are deliberately name-only — no type annotations — because a prompt is a text
template, and pretending otherwise added ceremony without buying safety. Metadata like
`model`, `temperature`, `tools` and `maxTokens` belongs in the declaration:

```malda
prompt advancedPrompt(task) {
    system: "You are an expert assistant.",
    user: "Help me with: {task}",
    model: "openai/gpt-4",
    temperature: 0.7,
    tools: ["read_file", "write_file", "grep"],
    maxTokens: 2000
}
```

**A tool is a decorated function.** No JSON schema by hand, no registration boilerplate:

```malda
@Tool("calculate_sum", "Adds two numbers together")
function calculateSum(a, b) {
    return int(a) + int(b);
}

var agent = new Agent("Assistant", "Helper", "You are a helpful assistant.", new OpenRouterClient("openai/gpt-4"));
agent.addTool("calculate_sum");
print(agent.think("What is 15 + 27?").content);
```

Swap `@Tool` for `@MCPTool` and the same function is exposed over the Model Context
Protocol; `new MCPServer().start()` is the whole server.

**Web endpoints and pages are decorators.** Here is a complete contact-form application,
abridged from `Examples/ui_contact_form.malda`:

```malda
var server = new HttpServer(8080);

@AIPAGE("/", "Contact form with name, email, and message fields")
function homePage() {
    return "";
}

@POST("/submit")
function handleSubmit(body) {
    print("Name: " + body.name);
    print("Email: " + body.email);
    print("Message: " + body.message);

    return {
        "status": 200,
        "body": "<html><body><h1>Thank You!</h1><a href='/'>Back</a></body></html>"
    };
}

server.start();
```

`@AIPAGE` takes a description instead of markup and has a model generate the HTML on first
access. `@PAGE` is the version where you write the markup yourself, and `@GET`/`@POST`/
`@PUT`/`@DELETE`/`@PATCH` on a `RestServer` give you a JSON API with optional Swagger.

**Actors are built in.** `spawn` creates one, `send` posts a message, `on` declares a
handler over private state:

```malda
actor Counter {
    var count = 0;

    on increment() {
        count = count + 1;
        print($"Counter incremented to {count}");
    }
}

var counter = spawn Counter();
send counter.increment();
```

**And a durable workflow is a block of statements.** This one comes last because it is the
least fashionable of the constructs and the easiest to oversell, but it is the reason the
grammar has `step` in it. Durable execution engines usually ask you to express reliability
as a pattern you apply correctly: decorate this, make that deterministic, remember not to
call `now()` there. Here the reliability vocabulary is grammar, so the runtime can hold the
line instead of documenting it — `now()` and `random()` in a workflow body are a runtime
error (`WF1001`), and file or HTTP built-ins outside a `step` are another (`WF1002`):

```malda
workflow OnboardCustomer(input) {
    step validated = validateInput(input)
        retry 3 backoff "exponential" delay 1000 maxDelay 30000
        timeout 120000;

    approval approved = approval("sales-manager", {"customerId": input.customerId})
        timeout 86400000
        onReject notifyRejected(input.customerId);

    wait docs = awaitSignal("docs_uploaded", {"customerId": input.customerId})
        timeout 259200000;

    step account = createAccount(validated)
        retry 2 backoff "linear" delay 1000
        compensate deleteAccount(account.id);

    return {"accountId": account.id, "status": "onboarded"};
}
```

`step`, `retry`, `backoff`, `compensate`, `approval`, `wait` and `timeout` are reserved
words, and `awaitSignal` is the fixed right-hand side the parser requires after `wait`.
State persists to `./.malda/workflows.db`, a SQLite file, and you drive
instances from the CLI (`malda workflow start`, `approve`, `signal`, plus dead-letter and
maintenance commands). A workflow that waits three days for a document upload is a normal
thing to write, and there is no broker or cluster to stand up first.

Worth stating precisely, because "durable" is an overloaded word. Recovery is step-level
memoization: a `step` that already succeeded returns its persisted result instead of
re-executing, and its output round-trips through JSON, so it must be JSON-shaped. The
determinism boundary is enforced by refusal rather than by a replay-time detector, and what
does the refusing is a fixed deny-list — non-deterministic names such as `now`, `random*`,
`randn` and `sleep`, plus side-effecting ones (`writeFile`, `runCommand`, `http*` and
friends). Everything else is deterministic by default, so a model call, a SQL write or .NET
interop placed outside a `step` is *not* caught for you. And one SQLite file means durability
across a process restart on one machine, not high availability: lose the machine and you lose
the instance. This is the small, local end of durable execution, not a Temporal replacement.

#### The point is that these compose

Individually, each of the constructs above has a good library somewhere, and nothing stops
you from importing all of them into one Python file. What you get when you do is an
integration layer: three opinions about what a tool is, three ways to configure a model, and
a deployment that has to carry all of it. In MALDA a single program can be, simultaneously:

- a **REST service** — `@GET` / `@POST` handlers on a `RestServer`, with Swagger,
- a **multi-agent system** — several `Agent` objects, each with its own role and
  conversation, passing work to each other,
- an **MCP server** — the same functions exposed to external clients by adding `@MCPTool`,
  so other tools can call into your program,
- an **MCP client** — consuming tools from other MCP servers over STDIO,
- a **durable workflow host** — long-running `workflow` instances persisted to SQLite,
- a **coding agent** — an autonomous loop that reads a spec, edits files, runs validation
  and commits,
- a **second brain** — CodingAgents that distill a docs folder into linked notes and serve
  an ASK web UI over the catalog (optional `--embed-folder` for a portable ASK-only `.exe`).

There is no adapter between those roles: a function decorated with `@MCPTool` is the same
function an agent can call as a tool and the same function a `@POST` handler can invoke.
Composition across files is `include` (parse-time splice) or `import` / selective
`import { … } from` (module export surface, including `export type` / `export schema`).

The existence proof for the coding-agent item is in the repository. `Examples/RalphWiggum/` is a
PRD-driven autonomous coding agent — **4,049 lines of MALDA** across eleven files, nine of
them the agent loop and two an interactive interview entry point — that reads a markdown
checklist, implements one open item per iteration using an agent with file and git tools,
validates what it wrote, persists resumable state and a knowledge graph between iterations,
detects when it is stalling, and optionally commits:

```malda
include "ralph/00-env.malda";
include "ralph/01-cli.malda";
include "ralph/02-prd.malda";
include "ralph/03-validation.malda";
include "ralph/04-state-memory.malda";
include "ralph/05-loop.malda";
include "ralph/06-report.malda";
include "ralph/07-notify.malda";
```

The second-brain item is larger by source size: `Examples/Agents/secondbrain_semantic.malda`
(~7,600 lines with `secondbrain_ask_ui_lib.malda`, `secondbrain_cli_lib.malda`, and
`secondbrain_cli_apply_lib.malda`) explores a
documentation tree, proposes a theme taxonomy, distills hierarchical notes, indexes them in
GraphMemory, then serves ASK over HTTP — cookie JWT auth, multi-user admin, tag filters,
non-interactive `build` / `update` / `ask` CLI, English/Italian UI — with a lexical sibling at
`secondbrain.malda` for A/B retrieval. `malda compile … --embed-folder secondbrain_semantic`
bakes the notes into the executable so ASK does not need a folder on disk.

The detail that matters more than the line count is *how* those agent-written showcases
were possible (the same is true of two private applications of mine, one of them
substantial). MALDA does not exist in any model's training data, so this is not recall —
it works because the repo ships a language pack for exactly that purpose (`docs/llm/`:
idioms, a parser-aligned BNF, a minimal built-in list, and a few-shot folder, with a
suggested load order for a token budget). An agent reads a few thousand tokens and writes
idiomatic MALDA; when it does not, the interpreter is the feedback loop. Desktop and Web
IDE Ask sessions materialize the same pack — embedded in `malda.dll`, not a
checkout-path prompt.

That points at the thing I did not expect when I started. "New language" used to imply "no
documentation your tools understand, and nobody who can write it"; a compact, machine-readable
language pack turns that into a solved onboarding problem for the one collaborator most of us
now work with. It does not conjure an ecosystem — an agent cannot import NumPy for you — but
it does mean the learning curve is no longer the main cost of a small language.

I am not claiming either showcase is better than the coding agents you already use. The claim
is narrower: they are substantial programs written *in* the language rather than in the host
runtime, and they were produced the same way the language was.

#### Why a language instead of a library?

This is the fair objection, and I want to answer it directly rather than wave at it.

You *can* build all of this as libraries — people have, and some of those libraries are
excellent. The bet MALDA makes is about what you get once a construct has syntax:

- **Tooling gets a real target.** A prompt with a name and parameters is a symbol. The
  shared language service behind the Desktop IDE, the browser playground and the LSP can
  offer completion, hover and diagnostics on it. Prompts hidden in dictionaries and YAML
  are invisible to every editor.
- **Some invariants move from the user to the runtime.** Because `step` and `compensate`
  are statements, journaling, memoized recovery and compensation ordering are the
  implementation's problem, and the determinism boundary is a diagnostic instead of a
  paragraph in the docs. That is narrower than it sounds — see the deny-list caveat above —
  but it is the difference between a rule you must remember and a rule that fails loudly.
- **One source, several targets.** The same file can be interpreted while you iterate,
  transpiled to C# and built into a .NET executable for delivery, or compiled to browser
  JavaScript. `@client()` / `@server()` / `@shared()` partition a full-stack program
  written as one program.
- **Less impedance mismatch.** `await someLlmPrompt(x)` reads like a call, because the
  language treats "ask a model" as an ordinary effect rather than an SDK ritual.

The nearer objection is not "why not a library" but **"why not macros"** — Lisp and Racket
have made languages-as-libraries work for decades, and Elixir, Kotlin and Ruby all host
convincing DSLs. That would have been less work and it would have inherited an ecosystem.
What it does not give you is a second and third backend: `#lang` and macro expansion bottom
out in the host runtime, whereas the point of a separate front end here is that one file can
be walked by an interpreter, emitted as C# and built into an executable, or emitted as
browser JavaScript. If I only wanted prompts to be syntax, macros would have been the right
answer, and I would have kept an ecosystem.

Putting a fast-moving vocabulary in a grammar is the other real risk. An earlier revision
had a `chain` keyword for LCEL-style pipelines; MCP arrived later, ACP later still, and
`chain` turned out to be ordinary `function` + `|>` with fashion naming. Grammar is the
worst layer to be wrong in, so the split I aim for is that syntax covers the parts that have
stopped moving — a template with holes, `step` / `retry` / `compensate`, message passing —
while everything model-facing (providers, tool protocols, model names) stays in the library
layer where it can be replaced without a language change. Dropping `chain` is the correction.

And the cost, which is real: no ecosystem. No NumPy, no crates.io, no npm. A new language
starts with whatever its standard library has — about 300 built-ins here, spanning
strings, collections, files, HTTP, JSON, SQL databases, graphs, vector search and process
control — plus .NET interop as the escape hatch. If your problem needs a mature library
that exists only in Python, use Python.

#### How it is built

Plain C# on .NET 8, with a recursive-descent parser rather than a generated one:

```text
.malda source → Lexer → Parser → AST
                              ├─ tree-walking Interpreter  (run now)
                              ├─ C# transpiler             (.exe / .dll)
                              └─ JS / PWA transpiler       (browser bundle)
```

Language intelligence lives in one shared service consumed by the WPF Desktop IDE, the
Blazor browser playground and the LSP server, so the three do not drift.

Current numbers, all checkable in the repo: ~350 built-in functions in the registry,
~1,900 tests, a multi-chapter HTML reference manual whose runnable snippets are executed by
the test suite, a ~100-case conformance matrix for the Tier 0 kernel across backends, and
curated interpret/transpile pairs that must match stdout on the same file.
Guard tests also fail the build if the manual's reserved-word list drifts from the lexer or
if a built-in is added without being documented anywhere.

#### What is not good yet

- **1.0 is not a checked language or a cluster.** Spec Final 1.0 and
  the CLI/Desktop `<Version>` match ([`docs/releases/v1.0.1.md`](releases/v1.0.1.md)).
  Publish (`compile --mode transpile` / `publish`) refuses emit on type-hint Errors;
  interpret stays dynamic. Tier 0 is the conformance gate; prompts, workflows and HTTP
  remain platform tiers with an honest backend capability matrix.
- **Type annotations are hints.** `var count: int = 0;` and `function add(a: int) -> int`
  parse and feed the IDE. Mismatches on literals, assignments, known identifiers, operators,
  selected builtins and `->` call results emit **Errors** by default in LSP/Desktop (opt-out;
  CLI `--strict-types` also enables match/`@pure`/bounds/const). Nothing enforces hints at
  runtime. There are sum types and `match`, and `schema` / `validate` for JSON, but there is
  no full static type checker. If you want a checked language today, this is not one.
- **Windows tilt.** The reference IDE is WPF, so Windows-only, and `MaldaLang.sln` cannot
  build on other platforms because of it. CI builds the CLI, compiler, language server and
  browser playground on Linux and macOS and runs `Examples/Basics/first_look.malda` through
  the CLI there, but only a small guard subset runs outside Windows — not the full suite.
- **The JavaScript backend is a genuine subset.** Tier 0 language plus DOM, game and
  three.js bindings. No agents, LLM clients, MCP, HTTP servers, or workflows in the
  browser path.
- **C# transpile covers the built-in registry** (including tool factories, git helpers,
  embeddings, and .NET interop entry points). Remaining gaps are deeper parity issues —
  for example static calls on a `DotNetTypeInstance` handle — not “built-in not emitted.”
  Large showcases (Ralph, Second Brain) are too big for CI smoke (`n/a` in
  `TranspileSmokeTests`); curated interpret/transpile pairs cover smaller examples.
- **Durable workflows are the local end of durable execution.** Step-level memoized
  recovery, JSON-shaped step outputs, a fixed deny-list for the determinism check, and a
  single SQLite file with no failover (documented single-writer / read-only ops model). Good
  enough for a workflow that waits three days on one box; not a distributed engine.
- **Transpiled executables need .NET 8** on the target unless you publish self-contained.
- **Benchmarks are modest samples only.** Micro timings live under `docs/benchmarks.md` /
  `docs/benchmarks-sample-results.json` — useful as regression smoke, not as a performance
  claim.
- **No public package registry yet.** Workspace `packages/`, selective `import { … } from`,
  and `export type` / `export schema` cover local composition; there is no npm-like hub.
- **Model access** is OpenRouter-first, with a small local GGUF model (Qwen2.5-0.5B, via
  LLamaSharp) auto-downloaded as an offline fallback. The fallback proves the pipeline
  works without an API key; it is not a production-quality model.

#### Licensing

Dual MIT OR Apache-2.0, at your option — the same arrangement Rust uses. One choice covers
the runtime, the compiler, the IDEs, the reference manual and the examples. The toolchain
injects runtime code into what it produces, and a runtime exception confirms that this
creates no attribution obligation for your program. The name "MALDA" is a trademark and is
not covered by either licence.

To answer the open-core question before it is asked: what is in this repository is the whole
language — runtime, compiler, both IDEs, the language server, the manual — and it stays under
MIT OR Apache-2.0. There is no CLA, so I could not quietly relicense your contributions even
if I wanted to, and I have no plan to relicense mine. I do keep some product applications
built *on* MALDA private; those are apps on top of the language rather than parts of the
core held back from it. If something in the core turns out to be missing because it lives
in a private app, that is a bug in this repository and I would rather hear about it.

#### Try it

```bash
git clone https://github.com/amaldini/maldalang.git
cd maldalang
dotnet build MaldaLang
dotnet run --project MaldaLang -- Examples/Basics/first_look.malda
```

`MaldaLang.sln` also builds the WPF Desktop IDE, so it is Windows-only; the CLI project
above is the one that works on Linux and macOS.

Then the contact form above, then `Examples/Prompts/` and `Examples/Workflows/`. The
agent showcases are `Examples/RalphWiggum/` (PRD loop) and
`Examples/Agents/secondbrain_semantic.malda` (docs → notes → ASK; lexical sibling at
`secondbrain.malda`). The browser playground (`dotnet run --project MaldaLang.IDE`) is the
fastest way to poke at the language without installing an editor extension.

I would especially like to hear where the syntax feels wrong, and which of the
limitations above you consider disqualifying.

---

## 3. Comparison with other languages and stacks

| Stack | Real overlap | How MALDA differs | What they do better |
|---|---|---|---|
| **Python + LangChain / LlamaIndex** | prompts, agents, tools, RAG pipelines | prompts, pipe pipelines and tools are declarations the parser and language server understand, not runtime-configured objects | ecosystem, ML libraries, hiring pool, everything about maturity |
| **BAML / DSPy / Pydantic AI** | prompts and tool schemas as declarations with real editor tooling — the closest thing to MALDA's central claim | one general-purpose language covers prompts *and* workflows, actors, endpoints and a compiled binary, instead of a DSL embedded in a host program | BAML actually type-checks prompt inputs and outputs and generates clients for languages you already use; adopting any of them costs you nothing else |
| **TypeScript + AI SDKs** | agents, streaming, web endpoints | one language covers browser, server and a compiled binary, with `@client`/`@server` partitioning | a real type system, npm, editor tooling that took a decade to build |
| **Temporal / Restate / Inngest** | durable execution, retries, signals, compensation | `step`/`retry`/`compensate`/`approval`/`awaitSignal` are grammar, and the determinism boundary is a diagnostic; state is a local SQLite file, no cluster | proven at scale, polyglot SDKs, real replay with non-determinism detection over the whole workflow, failover across machines, serious observability |
| **Elixir / OTP + Phoenix** | actors, message passing, live UI | AI constructs, durable workflows, REST and native compilation in one toolchain | genuine fault tolerance, preemptive scheduler, twenty years of production use |
| **Ballerina** | network and service concepts as first-class syntax — the closest philosophical relative | the first-class concepts are AI orchestration and durable workflows rather than integration and data types | static typing, sequence-diagram tooling, real cross-platform maturity |
| **Darklang** | ambition to collapse "write, deploy, run" for small AI/web apps | plain files, git, self-hosted, offline-capable, compiles to an ordinary executable | it explored this space first and learned expensive lessons about hosted-only tooling |
| **Mojo** | the "AI-native language" label | targets orchestration — prompts, agents, workflows, endpoints — not kernels or GPUs | numeric performance, Python compatibility, a funded compiler team |
| **Go / Rust** | shipping a single compiled binary | dynamic and quick to write, with AI and web behaviour in the standard library | performance, memory safety, correctness guarantees, ecosystems |
| **Bash / Make + curl** | the thing people actually use to glue LLM calls together | structure, persistence, and tooling that survives the second week | zero installation, universal availability |

Positioning in one line: *MALDA is what you would get if "call a model", "run this
reliably for three days", and "serve this over HTTP" were as ordinary in a language as
`for` and `try`.*

Honest scope: *it competes with the glue code, not with Python's ecosystem.*
