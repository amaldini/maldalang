# MALDA syntax pack (for writing programs)

*Applies to: MALDA 0.1.0*

Compact rules for generating correct `.malda`. Prefer this over scraping HTML manuals.

## Style preferences

- Use **`function`**, not `fn` / `def` (all three parse; `function` is preferred in docs/examples).
- Call standard-library functions through their **namespace**: `math.sqrt(16)`, `str.upper(s)`, `io.print(x)`.
- Statements end with **`;`**. The one optional case is a `match { }` **statement**, whose
  trailing `;` may be omitted (`match expr { ... }` or `match expr { ... };`). A `match`
  used as an **expression** is part of a larger statement and still needs that statement's
  `;`.
- Blocks use `{ }` like C-family languages.
- Dynamic typing; optional type hints exist (`: Type`) but are not required for most examples.
- **Prompt parameters are name-only** — write `prompt greet(name) { ... }`, never `prompt greet(name: string)`.
- Prompt `-> ReturnType` is informational only; do not treat it as enforced static typing.
- Interpolate with a **`$`-prefixed** string: `$"total: {n}"`, `$"{a} of {b}"`. The braces
  take any expression (`{n * 2}`, `{math.sqrt(x)}`, `{items[0]}`), and `$` strings compose
  with `AnsiConsole` markup. A plain string does **not** interpolate — `"total: {n}"` prints
  the literal `{n}`. Prompt bodies interpolate without the `$`. Concatenation (`+ string(x)`)
  still works when you prefer it.

## Which spelling to use for standard-library calls

Most stdlib functions answer to three names. They all run, but only one is current:

```malda
io.print(math.sqrt(16));      // preferred: namespaced
io.print(Math.sqrt(16));      // deprecated module alias (capital M)
io.print(sqrt(16));           // deprecated flat alias
```

The language server emits a `malda-style` warning on the last two — *Prefer 'math.sqrt(...)'
instead of 'sqrt(...)' (deprecated flat alias)* — so code written with them arrives with
warnings attached. Use `math.`, `str.` and `io.` in new code. The `few-shot/` snippets model
that preferred style. `Examples/` and the reference manual still use flat spellings in many
places; read those as equivalent, do not copy the style.

Which names are namespaced is listed in the `call` column of
[`malda-builtins.tsv`](malda-builtins.tsv). Names that never had a namespace — `parseJSON`,
`toJSON`, `sleep`, `range`, `exit` — are written bare.

## Core constructs

```malda
io.print("Hello");

var x = 10;
var name = "Ada";
var items = [1, 2, 3];
items.append(4);          // member-style method — NOT a free function, and there is no `arr` namespace
var last = items.pop();   // remove last;  items.shift() removes first
var first = items[0];
var n = items.length;     // property, not a call: items.length() is an error
                          // str.length(items) also works
var person = { "name": "Ada", "age": 36 };
person.age = 37;

if (x > 5) {
    io.print("big");
} else {
    io.print("small");
}

while (x > 0) {
    x = x - 1;
    if (x == 5) { continue; }
    if (x == 0) { break; }
}

for (var i = 0; i < 3; i = i + 1) {
    io.print(i);
}

foreach (var item in items) {
    io.print(item);
}

function add(a, b) {
    return a + b;
}

var double = (n) => n * 2;
```

## Classes

```malda
class Counter {
    var value = 0;

    function Counter() {
    }

    function inc() {
        this.value = this.value + 1;
        return this.value;
    }
}

var c = new Counter();
io.print(c.inc());
```

## Prompts (AI)

```malda
prompt greet(name) {
    user: "Hello, {name}!"
}

var g = greet("Ada");
io.print(g.user);
```

## Actors (concurrency)

Actor handlers do not see the `io` / `math` / `str` namespaces — use the flat `print`
alias inside `on` handlers. Outside the actor, prefer `io.print`.

```malda
actor Counter {
    var count = 0;

    on increment() {
        count = count + 1;
    }

    on get() {
        print(count);
    }
}

var a = spawn Counter();
sleep(100);
send a.increment();
send a.get();
sleep(500);
```

Prefer `sleep(...)` for timing in examples/tests (not busy-wait loops). Copy actor patterns from `Examples/Actors/`.

## Web / REST (server)

```malda
@GET("/api/health")
function health() {
    return parseJSON("{\"ok\": true}");
}
```

Decorators like `@GET`, `@POST`, `@PAGE` attach to **function** declarations.

## Common mistakes (avoid)

| Wrong / JS-like | MALDA |
|-----------------|--------|
| `const x = 1` | `var x = 1` |
| `let x = 1` | `var x = 1` |
| `function f(x: number)` on prompts | `prompt f(x)` name-only |
| `console.log(x)` | `io.print(x)` |
| `println(x)` | `io.print(x)` — `println` does not exist |
| `fn f() {}` in docs | prefer `function f() {}` |
| Omitting `;` on statements | Required — without it the CLI reports a parse error and exits non-zero |
| Inventing Python `def` style indent blocks | use `{ }` |
| `"total: {n}"` | `$"total: {n}"` — or `"total: " + string(n)`. Plain strings do not interpolate |

Those are the errors the parser catches for you. The ones it does not catch are in
[`malda-gotchas.md`](malda-gotchas.md); read that before declaring a program correct.

## Operators

- Logic: `and` / `or` / `not` (also `&&` `||` `!`)
- Equality: `==` `!=`
- Arithmetic: `+ - * / %`
- Lambdas / return markers: `=>` or `->` (same token)

## Where to go deeper

- Silent failures: [`malda-gotchas.md`](malda-gotchas.md)
- Grammar: [`malda-grammar.md`](malda-grammar.md)
- Builtins: [`malda-builtins-min.md`](malda-builtins-min.md), lookup table [`malda-builtins.tsv`](malda-builtins.tsv)
- Spec semantics: `docs/spec/malda-language-1.0.md`
- Full chapters: `ReferenceManual/`
- Real programs: `Examples/`
