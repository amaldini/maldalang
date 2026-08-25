# Tutorial: errors, Result/Option, and schema validate

Short recipes for structured failure handling. Prefer these over inventing exception hierarchies.

## Tagged `catch`

Throw an object with a `kind` (or any field) and filter in `catch`:

```malda
try {
    throw dict { "kind": "IO", "message": "disk full" };
} catch (e if e.kind == "IO") {
    io.print("IO: " + e.message);
} catch (e) {
    io.print("other: " + string(e));
}
```

Runnable sample: [`docs/llm/few-shot/11_errors_match.malda`](../llm/few-shot/11_errors_match.malda),
[`Examples/Basics/errors_and_match.malda`](../../Examples/Basics/errors_and_match.malda).

## `result` / `option` stdlib

Use the globals `result` and `option` for explicit success/failure without throwing:

```malda
function parseAge(raw) {
    var n = toIntOrNull(raw);
    if (n == null) {
        return result.err("not an int");
    }
    return result.ok(n);
}

var r = parseAge("12");
if (result.isOk(r)) {
    io.print(result.unwrapOr(r, 0));
} else {
    io.print("failed");
}

function doubleAge(n) {
    return result.ok(n * 2);
}
io.print(result.unwrapOr(parseAge("12") |> result.andThen(doubleAge), 0));
```

Helpers live on the `result` / `option` modules (`result.ok`, `result.err`, `result.isOk`, `result.unwrapOr`, `result.andThen`, …). Use `map` to transform an `Ok`/`Some` payload; use `andThen` when the next step itself returns a Result or Option.

## `schema` + `validate`

Declare a reusable JSON-shaped schema; `validate(nameOrObject, value)` returns
`{ ok: true, data }` or `{ ok: false, error }` (no throw on mismatch):

```malda
schema Person {
    name: string;
    age: int;
}

var checked = validate("Person", dict { "name": "Ada", "age": 36 });
if (checked.ok) {
    io.print(checked.data.name);
}
```

- From a JSON string: `parseJson(text, "Person")` (throws on mismatch).
- After HTML forms: `bindForm` → `validate`, with session flash on failure — see
  [`Examples/Web/form_validate_flash.malda`](../../Examples/Web/form_validate_flash.malda).
- Offline schema-only sample: [`Examples/Basics/schema_validate.malda`](../../Examples/Basics/schema_validate.malda).
- Bound to a prompt: `prompt codeReview(...) -> Review` names the schema `await` will validate. Offline stand-in: [`Examples/Basics/first_look.malda`](../../Examples/Basics/first_look.malda).
- Manual: Reference Manual §13.7.1 (`validate`) / §13.7.1.1 (`asVariant`) and schema section in Functions.

## Sum types: `validate` then `asVariant`

`validate("Intent", dict)` checks the tagged JSON `{ "tag": "Buy", … }` and leaves
`data` as a dict. `asVariant("Intent", dict)` throws on mismatch and returns a
variant for `match`. `await prompt … -> Intent` still yields a variant directly.

```malda
type Intent = Search(query: string) | Buy(sku: string, qty: int);

var checked = validate("Intent", tagged);
if (checked.ok) {
    match asVariant("Intent", checked.data) {
        case Buy(sku, qty): io.print($"buy {sku} x {qty}");
        case Search(q): io.print($"search {q}");
    }
}
```

Runnable sample: [`Examples/Basics/as_variant.malda`](../../Examples/Basics/as_variant.malda),
[`docs/llm/few-shot/25_as_variant.malda`](../llm/few-shot/25_as_variant.malda).

## What not to do

- Do not treat type hints (`var n: int = …`) as runtime validation — use `validate` or `toIntOrNull`.
- Do not confuse `validate(schema, value)` with `memory.validate()` on GraphMemory.
