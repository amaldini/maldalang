# Potential Features for the MALDA Language

This document describes language features that could be added to MALDA in the future. They are ideas for consideration, not commitments. Pattern matching, destructuring, and sum types have already been implemented; the rest are proposed.

---

## Already Implemented

### Pattern Matching
- **Status:** Implemented
- **Syntax:** `match value { case pattern: body; default: body; }`
- Supports literal, identifier, wildcard, array, and object patterns; rest patterns in arrays; nested patterns; default case.
- See the Reference Manual (Control Structures) and tests for details.

### Destructuring
- **Status:** Implemented
- **Syntax:** `var [x, y, ...rest] = arr;` and `var { name, age } = obj;`
- Array and object destructuring in declarations and assignments; rest pattern; nested destructuring.
- See the Reference Manual (Variables) and tests for details.

### Sum Types / Tagged Unions (Lightweight ADTs)
- **Status:** Implemented
- **Syntax:** `type Result = Ok(value) | Err(message);` with constructor calls and variant patterns in `match`.
- Type declarations with one or more constructors (with optional payload parameters); constructors used as functions (e.g. `Ok(42)`); matching via `case Ok(v):` / `case Err(msg):` with payload binding.
- See the Reference Manual (Data Types, Control Structures) and tests for details.

### First-Class Async / Task
- **Status:** Implemented
- **Syntax:** `var t = async fetchData(url);` then `var data = await t;`
- First-class Task values; `async expr` creates a Task from an expression (function call or value); `await expr` waits for a Task and yields its result.
- See the Reference Manual (Expressions) and tests for details.
- **Note:** Structured concurrency (`all()` function) is implemented as a built-in that composes multiple Tasks into one.

### Built-in "AI Blocks" (Prompt Enhancements)
- **Status:** Implemented
- **Syntax:** 
  - Statement-based body: `prompt summarize(text) { system "You are a summarizer."; user text; }`
  - Direct await execution: `var s = await summarize(longDoc);`
- Prompts support two body syntaxes: object literal (backward compatible) and statement-based (new). When awaited, prompts execute LLM calls directly and return response strings.
- See the Reference Manual (Functions) and tests for details.

---

## High-Value Candidates

### 2. Declarative Data Schemas and Validation
- **Idea:** Inline schema literals to validate objects and drive tooling/UI.
- **Example:**
  ```malda
  schema User { name: string; age: int?; email: string; }
  var user = validate(User, inputObj);  // throws or returns { ok, errors }
  ```
- **Why:** Aligns with AI/tools (e.g. JSON from LLMs, planning tools).

### 3. Pipelines and Collection Comprehensions
- **Idea:** Pipe operator and list/dict comprehensions.
- **Example:**
  ```malda
  var result = data |> filter((x) => x.active) |> map((x) => x.score) |> sort() |> reverse();
  var evens = [x for x in range(10) if x % 2 == 0];
  var scoreByName = { u.name: u.score for u in users };
  ```
- **Why:** Readable data flows and concise collections; good for automations/agents.

### 4. Structured Error Handling (try/catch/using)
- **Idea:** Richer try/catch (e.g. by error tag/kind) and `using`/defer for resources.
- **Example:**
  ```malda
  try { dangerous(); } catch (e if e.kind == "IO") { print("IO: " + e.message); } catch (e) { print(e); }
  using f = openFile("log.txt") { f.write("hello"); }
  defer { cleanup(); }
  ```
- **Why:** Clearer error handling and resource lifecycle.

### 5. Actor-Centric Language Sugar
- **Status:** Implemented
- **Idea:** Actor message declarations and typed `receive` patterns.
- **Example:**
  ```malda
  actor Counter {
    message Inc(amount);
    message Get() -> int;
    var value = 0;

    on start() {
      var running = true;
      while (running) {
        var msg = receive();
        match msg {
          case Inc(n): value = value + n;
          case Get(): reply(value);
          case "stop": running = false;
          default: {};
        }
      }
    }
  }
  ```
- **Why:** Documents actor contracts and reduces stringly-typed message handling.
- **See:** Reference Manual, Actors chapter (message declarations and `receive()` patterns).

### 6. Modules / Imports with Explicit Exports
- **Idea:** Formal modules and import/export.
- **Example:**
  ```malda
  module math-utils {
    export function clamp(x, min, max) { ... }
    function internalHelper() { ... }
  }
  import { clamp } from "math-utils";
  ```
- **Why:** Scales beyond single files and clarifies public API (helps tools like `getSymbols`).

### 7. Declarative Config and Metadata (Attributes)
- **Idea:** Attributes on functions, actors, prompts.
- **Example:**
  ```malda
  @[deprecated("Use newFoo instead")] function foo() { }
  @[tool("filesystem.read")] function safeRead(path) { ... }
  @[timeout(1000)] prompt planTask(task) { ... }
  ```
- **Why:** Convention-driven behavior (tools, security, timeouts) without extra boilerplate.

### 8. Lightweight Type Hints (Gradual Typing Direction)
- **Idea:** Optional, non-enforced type hints for tooling and docs.
- **Example:**
  ```malda
  function add(a, b) -> int { return a + b; }
  ```
- **Why:** Better editor/AI support and a path to optional static analysis later.

### 9. Time- or Resource-Bounded Blocks
- **Idea:** Language-level time/resource bounds.
- **Example:**
  ```malda
  within(500) {
    // code that should finish in <= 500ms
  } onTimeout { print("Timed out"); }
  ```
- **Why:** Expresses robustness and latency budgets directly; useful for agents and automation.

---

## Summary Table

*Implemented: Pattern matching, destructuring, sum types (tagged unions), async/await, prompt enhancements (statement-based syntax and direct await execution).*
*Structured concurrency via the `all()` function is also implemented.*

| Feature                     | Category        | Effort (rough) | Impact |
|----------------------------|-----------------|----------------|--------|
| Data schemas / validation   | Data / tooling  | Medium         | High   |
| Pipelines & comprehensions | Expressions     | Low–Medium     | Medium |
| try/catch/using/defer       | Control flow    | Low–Medium     | Medium |
| Actor protocols / sugar    | Actors          | Medium         | High   |
| Modules / imports          | Structure       | High           | High   |
| Attributes / metadata      | Declarative     | Low–Medium     | Medium |
| Type hints (informational) | Typing          | Low            | Medium |
| Time-bounded blocks         | Control flow    | Low–Medium     | Medium |

---

## Notes

- Prioritisation can follow: **pattern matching**, **destructuring**, **sum types**, **async/await**, and **prompt enhancements** (done); then **actor sugar** and **pipelines/comprehensions** for maximum leverage with reasonable cost.
- This list is for planning and discussion; actual design and implementation would need detailed specs and compatibility checks with the current interpreter and transpiler.
