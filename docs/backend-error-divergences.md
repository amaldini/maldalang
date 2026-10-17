# Backend error-path divergences (to review)

**Status:** open — not fixed, not yet decided  
**Found:** 2026-10-17, while adding error paths to the differential fuzz

The interpreter, C# transpile and JavaScript transpile disagree on the cases below. None of
them is documented in [`javascript-backend.md`](javascript-backend.md) or the Reference
Manual. Each needs a decision: align the backends (and on which behavior), or document the
divergence and report it with a diagnostic.

| Case | Interpreter | C# transpile | JavaScript transpile |
|------|-------------|--------------|----------------------|
| Read out of range: `[1, 2, 3][3]`, `a[-1]`, `"abc"[5]` | throws | throws | `null` |
| Division by zero: `5 / 0`, `5 % 0`, `5.0 / 0` | throws | throws | `Infinity` / `NaN` |
| `int("abc")` | throws | `0` | `0` |
| Write past the end: `a[5] = 1` on a 3-item array | pads with `null` | throws | pads with holes, prints `[1, 2, 3, , , 1]` |

Where to look:

- JavaScript: index access and division in `Examples/Web/wwwroot/malda-js-runtime.js` and
  `MaldaLang.Compiler/JsTranspiler.Expressions.cs`.
- C#: index assignment and `CoerceToInt` in the transpiled `RuntimeHelpers`
  (`MaldaLang.Compiler/CSharpTranspiler.RuntimeHelpers.*.cs`).
- Interpreter: `int()` conversion and index assignment.

`ErrorPathStatement` in `MaldaLang.Tests/Fuzz/BackendFuzzProgramGenerator.cs` skips these
cases on purpose. Add them to the generator once the backends agree.
