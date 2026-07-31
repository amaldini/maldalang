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
Hi HN. MALDA is a programming language where the things I kept writing glue
code for — LLM prompts, tools, agents, HTTP endpoints — are language
constructs instead of library calls. prompt, chain, schema, actor, spawn and
send are keywords; tools, endpoints and pages are declarations the parser
understands.

A prompt is a declaration, not a string in a dictionary:

  setDefaultAgent(new Agent("Reviewer", "helper",
      "You review code.", new OpenRouterClient()));

  prompt codeReview(code, language) {
      system: "You are an expert code reviewer of {language}.",
      user: "Review this {language} code:\n\n{code}"
  }

  var review = await codeReview(source, "python");

Without await you get the rendered prompt object back instead, which is
what makes prompts testable. A tool is a decorated function: @Tool exposes
it to an agent, @MCPTool exposes the same function over the Model Context
Protocol, and new MCPServer().start() is the whole server.

The part I did not expect is about who writes it. The largest MALDA program
in the repo is a PRD-driven coding agent — 4,049 lines across eleven files,
which reads a checklist, implements one item per iteration, validates its
own output and can commit — and it was written entirely by a coding agent,
in a language that appears nowhere in any model's training data. That works
because the repo ships a language pack for that reader (docs/llm/: idioms,
a parser-aligned BNF, a minimal built-in list, few-shots, with a load order
for a token budget). An agent reads a few thousand tokens and writes
idiomatic MALDA; when it does not, the interpreter is the feedback loop.

I think that changes the arithmetic on small languages. "New language" used
to mean "no tooling, no docs your editor understands, and nobody who can
write it." A compact, machine-readable language pack turns the last of
those into an onboarding problem you can solve in an afternoon, for the
collaborator most of us now work with. It does not conjure an ecosystem —
an agent cannot import NumPy for you — but the learning curve is no longer
the main cost of a small language.

The same source runs three ways: interpreted, transpiled to C# and built
into a .NET executable, or compiled to browser JavaScript. That last part
is what macros over an existing language would not have given me. And the
constructs compose: one program can be a REST service (@GET/@POST, plus an
@AIPAGE decorator that has a model generate the page from a description), a
multi-agent system, an MCP server exposing its own functions as tools, an
MCP client consuming someone else's, and a host for durable workflows with
step/retry/compensate/approval in the grammar — with no adapter between the
roles, because a @MCPTool function is the same function an agent calls as a
tool and a @POST handler invokes.

How it was built, since that is the fair question. Heavily with coding
agents. My background is business software in C# and Java, and this is the
first compiler or interpreter I have written. The grammar and semantics
calls are mine to defend, but the typing and much of the argument that
produced them were shared with models, which makes this partly an
experiment in designing a language when the design conversation includes
one. Geoff Huntley's "Ralph Wiggum" loop and the cursed language he got out
of it (https://ghuntley.com/cursed/) convinced me one person could attempt
it; the agent example above is named after it.

In place of asking for trust: guard tests that fail the build if the
manual's reserved words drift from the lexer or a built-in goes
undocumented, every runnable snippet in the 35-chapter reference manual
executed by the test suite, and a 100-case conformance matrix across
backends. That catches drift, not bad taste. Implementation is C# on
.NET 8 — hand-written lexer and recursive-descent parser, no ANTLR, a
tree-walking interpreter, ~300 built-ins, ~1,370 tests. Dual licensed
MIT OR Apache-2.0 with a runtime exception, so compiled programs carry no
attribution obligation. No CLA.

Honest about where it is: this is the first public drop of the core (0.1.0)
and the spec is Draft 1.0. Type annotations parse and feed the language
server but there is no static checker yet — it is dynamically typed at
runtime. I do not run it in my day job either; that is large systems already
in flight, and they do not get rewritten for an experiment. The full IDE is
WPF, so Windows-only; the CLI, compiler and browser playground build and run
on Linux in CI, macOS is untested. The JavaScript backend is a real subset —
no agents or servers in the browser path. Durable workflows are the local
end of durable execution: step-level memoization on one SQLite file, durable
across a restart but not highly available, and the determinism check is a
deny-list of 16 built-in names. No benchmarks yet.

Happy to answer the obvious questions ("why not a library?", "why not macros
over an existing language?") and anything else.
```

<details>
<summary>Earlier longer draft of the same comment (kept for reference)</summary>

> Hi HN. MALDA is a programming language I've been building where the things I kept
> writing glue code for — LLM prompts, tools, HTTP endpoints, durable workflows, actors —
> are language constructs instead of library calls. `prompt`, `workflow`, `step`,
> `compensate`, `chain`, `schema`, `actor` and `spawn` are keywords; agents, tools and
> endpoints are declarations the parser understands.
>
> A prompt is a declaration, not a string in a dictionary:
>
> ```malda
> setDefaultAgent(new Agent("Reviewer", "helper", "You review code.", new OpenRouterClient()));
>
> prompt codeReview(code, language) {
>     system: "You are an expert code reviewer specializing in {language}.",
>     user: "Review this {language} code:\n\n{code}"
> }
>
> var review = await codeReview(source, "python");
> ```
>
> Call it without `await` and you get the rendered prompt back instead, which is what makes
> prompts testable.
>
> A durable workflow is a block of statements, not a set of decorators you have to apply
> correctly. `retry`, `compensate`, `approval` and `awaitSignal` are part of the grammar,
> `now()` outside a step is a diagnostic rather than a footgun, and state persists to a local
> SQLite file, so there is no cluster to operate:
>
> ```malda
> workflow OnboardCustomer(input) {
>     step validated = validateInput(input)
>         retry 3 backoff "exponential" delay 1000 maxDelay 30000
>         timeout 120000;
>
>     approval approved = approval("sales-manager", {"customerId": input.customerId})
>         timeout 86400000
>         onReject notifyRejected(input.customerId);
>
>     wait docs = awaitSignal("docs_uploaded", {"customerId": input.customerId})
>         timeout 259200000;
>
>     step account = createAccount(validated)
>         retry 2 backoff "linear" delay 1000
>         compensate deleteAccount(account.id);
>
>     return {"accountId": account.id, "status": "onboarded"};
> }
> ```
>
> The same source runs three ways: interpreted (`malda app.malda`), transpiled to C# and
> built into a .NET executable (`malda compile app.malda --mode transpile -o app.exe`), or
> compiled to browser JavaScript/PWA. There are also actors with `spawn`/`send`/`on`
> handlers, `@GET`/`@POST` REST decorators, an `@AIPAGE` decorator that generates a page
> from a natural-language description, and an MCP server/client you get by decorating a
> function with `@MCPTool`.
>
> What I actually wanted was for those to compose. One program can be a REST service, a
> multi-agent system, an MCP server exposing its own functions as tools, an MCP client
> consuming someone else's, and a durable workflow host — with no adapter between the roles,
> because a `@MCPTool` function is the same function an agent calls as a tool and a `@POST`
> handler invokes. The largest thing written this way is in the repo: a PRD-driven autonomous
> coding agent in `Examples/RalphWiggum/`, 4,049 lines of MALDA, which reads a checklist,
> implements one item per iteration, validates its own output, keeps a resumable knowledge
> graph between iterations and can commit.
>
> Two things about how this was made, because they are the fair question. My background is
> business software in C# and Java, and this is the first compiler or interpreter I have
> written — the durable-workflow vocabulary comes from years of processes that wait for
> someone to approve them, not from language research. I also write much less code by hand
> than I did two years ago, and a language is the project where what is left is the deciding:
> an agent will type a parser, but someone still has to decide what the grammar means — and I
> made those calls in a running argument with models, so this is also an experiment in
> designing a language when the design conversation includes one. Geoff Huntley's "Ralph
> Wiggum" loop and the `cursed` language he got out of it (https://ghuntley.com/cursed/)
> convinced me one person could attempt this, which is what the example above is named after.
> So: MALDA was built heavily with coding agents, the grammar and semantics decisions are mine
> to defend and much of the typing — and of the argument — was not. And those 4,049 lines of MALDA
> were written entirely by a coding agent, even though MALDA is in no model's training data,
> because the repo ships a language pack for that reader (`docs/llm/`: idioms, a parser-aligned
> BNF, a minimal built-in list, few-shots). A few thousand tokens in, an agent writes idiomatic
> MALDA and the interpreter is the feedback loop. That does not conjure an ecosystem, but the
> learning curve is no longer the main cost of a small language.
>
> Implementation is C# on .NET 8: a lexer and a recursive-descent parser written directly
> rather than generated (no ANTLR, no yacc), a tree-walking interpreter, a C# transpiler, and
> a narrower JavaScript backend. Roughly 300 built-in
> functions, ~1,370 tests, a 35-chapter HTML reference manual kept in sync with the code by
> guard tests, and a 100-case conformance suite for the Tier 0 kernel across backends. Dual
> licensed MIT OR Apache-2.0, with a runtime exception so programs you compile carry no
> attribution obligation. The core stays under those licences: no CLA, no relicensing plan.
> Those guard tests and the conformance matrix are also the answer to "who checked the
> agent-written parts" — they fail the build on drift, which is not the same as good taste.
>
> Honest about where it is: this is the first public drop of the core (0.1.0), not a 1.0
> release. I do not run it in my day job either — that is large systems already in flight, and
> they do not get rewritten for an experiment; MALDA is where I try things out.
> The language spec is Draft 1.0. Type annotations parse and feed the language server but
> there is no static checker yet — it is dynamically typed at runtime. The full-featured
> IDE is WPF, so Windows-only; the CLI, the compiler and the browser playground build and
> run on Linux in CI, but macOS is untested and the full test suite only runs on Windows.
> The JavaScript backend is a real subset — no agents or servers in the browser path.
> Workflow recovery is step-level memoization on one SQLite file — durable across a restart,
> not highly available — and the determinism check is a deny-list of 16 built-in names, so a
> model call outside a `step` is not caught for you. There are no benchmarks yet.
>
> Repo, examples and the reference manual: https://github.com/amaldini/maldalang
>
> Happy to answer the obvious questions ("why not a library?", "why not macros over an
> existing language?") and anything else.

</details>

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

I should be straight about where that leaves MALDA. I do not use it in my day job: that is
large systems already in flight, and nobody rewrites those for an experiment. MALDA is where I
experiment — with the AI constructs themselves, and with how a language gets designed now that
the design conversation includes a model. The claim is that it is a good place to try those
things, not that it has earned production.

So the constructs I kept re-gluing became syntax: prompts, tools, agents, HTTP endpoints,
durable workflows. The obvious alternative was a better library, and I tried that first — a
prompt registry, a workflow wrapper, validation on top of dictionaries. It works, and it
leaves every invariant with the caller; each library also brings its own idea of what an
"agent" is, so the glue stops being plumbing and becomes translation between three opinions.
What I wanted instead was for the *parser* to know what a prompt is, so that completion,
hover, diagnostics and a second backend all came out of one definition.

There is a loop in this that I did not plan. `Examples/RalphWiggum/` — the largest program
written in MALDA — is a PRD-driven coding agent named after Huntley's technique. The
technique that convinced me a language was buildable is the thing the language's biggest
example implements. And it was written entirely by a coding agent, in a language that
appears nowhere in any model's training data — which turned out to be the most interesting
result in the project, and is the part I would argue about first. More on that below.

That loop is what the last two letters of the name carry. MALDA is a Multi Agent Language with
Development Automation, and the automation runs in both directions: coding agents write MALDA
programs, which is why `docs/llm/` ships a language pack for a reader that has never seen the
syntax, and MALDA programs automate development work in turn — RalphWiggum is one of them
doing exactly that.

**And how it was built, since that is the first question anyone should ask.** Heavily with
coding agents. The grammar, the semantics, the tier split and every "no, not like that" are
mine to defend, and I have read what went in — but much of the typing was not mine, and neither
were all the arguments that got me to those calls; pretending otherwise would be absurd for a
project whose premise is that agents changed how code gets written. What I can offer in place of trust is machine-checked consistency: guard tests that
fail the build if the manual's reserved words drift from the lexer or a built-in is
undocumented, every runnable snippet in the reference manual executed by the test suite, and
a 100-case conformance matrix run across backends. That catches drift, not bad taste. Judge
the taste from the syntax below.

#### What that actually means

**A prompt is a declaration.** It has a name, parameters, and interpolation into role
sections. The parser knows about it, which means the language server knows about it too:

```malda
prompt codeReview(code, language) {
    system: "You are an expert code reviewer specializing in {language}.",
    user: """
    Please review this {language} code:

    {code}

    Provide feedback on quality, potential bugs, and best practices.
    """
}
```

Calling it without `await` gives you the rendered prompt object, which is handy for tests
and for inspection. Calling it with `await` sends it to the configured model. Prompt
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
verbatim from `Examples/ui_contact_form.malda`:

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
does the refusing is a deny-list of sixteen names — four non-deterministic built-ins (`now`,
`random`, `randomInt`, `randomFloat`) and twelve side-effecting ones (`writeFile`,
`runCommand`, `http*` and friends). Everything else is deterministic by default, so a model
call, a SQL write or .NET interop placed outside a `step` is *not* caught for you. And one
SQLite file means durability across a process restart on one machine, not high availability:
lose the machine and you lose the instance. This is the small, local end of durable
execution, not a Temporal replacement.

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
  and commits.

There is no adapter between those roles: a function decorated with `@MCPTool` is the same
function an agent can call as a tool and the same function a `@POST` handler can invoke.
Composition across files is `include`, which splices at parse time.

The existence proof for the last item is in the repository. `Examples/RalphWiggum/` is a
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

One detail matters more than the line count: **those 4,049 lines were written entirely by a
coding agent.** So were two private applications of mine, one of them substantial. MALDA does
not exist in any model's training data, so this is not recall — it works because the repo
ships a language pack for exactly that purpose (`docs/llm/`: idioms, a parser-aligned BNF, a
minimal built-in list, and a few-shot folder, with a suggested load order for a token budget).
An agent reads a few thousand tokens and writes idiomatic MALDA; when it does not, the
interpreter is the feedback loop.

That points at the thing I did not expect when I started. "New language" used to imply "no
documentation your tools understand, and nobody who can write it"; a compact, machine-readable
language pack turns that into a solved onboarding problem for the one collaborator most of us
now work with. It does not conjure an ecosystem — an agent cannot import NumPy for you — but
it does mean the learning curve is no longer the main cost of a small language.

I am not claiming Ralph is better than the coding agents you already use. The claim is
narrower: it is the largest program in the language, it is written *in* the language rather
than in the host runtime, and it was produced the same way the language was.

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

Putting a fast-moving vocabulary in a grammar is the other real risk, and the repository
shows it: `chain` is 2023 vocabulary, MCP arrived later, ACP later still. Grammar is the
worst layer to be wrong in, so the split I aim for is that syntax covers the parts that have
stopped moving — a template with holes, `step` / `retry` / `compensate`, message passing —
while everything model-facing (providers, tool protocols, model names) stays in the library
layer where it can be replaced without a language change. Where that line is drawn wrong,
`chain` is the honest example.

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

Current numbers, all checkable in the repo: ~300 built-in functions in the registry,
~1,370 tests, 137 `.malda` examples, a 35-chapter HTML reference manual whose runnable
snippets are executed by the test suite, and a 100-case conformance matrix for the Tier 0
kernel across backends. Guard tests also fail the build if the manual's reserved-word list
drifts from the lexer or if a built-in is added without being documented anywhere.

#### What is not good yet

- **This is a first public drop (0.1.0), not a 1.0 release.** Tagged `v0.1.0`. The
  language spec (`docs/spec/malda-language-1.0.md`) is Draft 1.0: the Tier 0
  kernel is normative, while prompts, workflows and HTTP are specified as platform tiers.
- **Type annotations are hints.** `var count: int = 0;` and `function add(a: int) -> int`
  parse and feed the IDE, but nothing enforces them at runtime or during transpilation.
  There are sum types and `match`, and `schema` declarations for JSON, but there is no
  static type checker. If you want a checked language today, this is not one.
- **Windows tilt.** The reference IDE is WPF, so Windows-only, and `MaldaLang.sln` cannot
  build on other platforms because of it. CI does build the CLI, compiler, language server
  and browser playground on Linux and runs an example through the CLI there, but only a
  two-test guard subset runs on Linux, and macOS is not tested at all.
- **The JavaScript backend is a genuine subset.** Tier 0 language plus DOM, game and
  three.js bindings. No agents, LLM clients, MCP or HTTP servers in the browser path.
- **C# transpile covers the built-in registry** (including tool factories, git helpers,
  embeddings, and .NET interop entry points). Remaining gaps are deeper parity issues —
  for example static calls on a `DotNetTypeInstance` handle — not “built-in not emitted.”
  The Ralph agent compiles to a working `.exe`.
- **Durable workflows are the local end of durable execution.** Step-level memoized
  recovery, JSON-shaped step outputs, a 16-name deny-list for the determinism check, and a
  single SQLite file with no failover. Good enough for a workflow that waits three days on
  one box; not a distributed engine.
- **Transpiled executables need .NET 8** on the target unless you publish self-contained.
- **No benchmarks.** The interpreter walks the AST, and I have not published performance
  numbers. Treat throughput claims as absent, not as good.
- **The package manager needs a registry** and there is no public package ecosystem yet.
  In practice you compose with `include` and `import` over local files.
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
dotnet build MaldaLang.sln
dotnet run --project MaldaLang -- Examples/Basics/hello_world.malda
```

Then the contact form above, then `Examples/Prompts/` and `Examples/Workflows/`. The
browser playground (`dotnet run --project MaldaLang.IDE`) is the fastest way to poke at the
language without installing an editor extension.

I would especially like to hear where the syntax feels wrong, and which of the
limitations above you consider disqualifying.

---

## 3. Comparison with other languages and stacks

| Stack | Real overlap | How MALDA differs | What they do better |
|---|---|---|---|
| **Python + LangChain / LlamaIndex** | prompts, agents, tools, RAG chains | prompts, chains and tools are declarations the parser and language server understand, not runtime-configured objects | ecosystem, ML libraries, hiring pool, everything about maturity |
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
