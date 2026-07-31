# Plan: Add PWA target to `malda compile` / transpile

**Goal:** Allow `malda compile --target pwa` (or `--mode pwa`) to produce a Progressive Web App: transpiled JavaScript plus a minimal PWA shell (manifest, service worker, HTML entry).

**Scope:** Reuse existing MALDA→JS transpilation; add a new compilation mode that writes JS into an output directory and generates PWA artifacts.

---

## 1. Current state

- **CLI:** `malda compile <input.malda> [-o path] [--mode interpreter|transpile|dll|js] [--target js]`
- **Compiler:** `MaldaLang.Compiler.Compiler` has `CompilationMode`: Interpreter, TranspileToCSharp, TranspileToDll, **JavaScript**.
- **JS output:** `CompileToJavaScript(sourcePath, outputPath)` writes a single `.js` file (and optional `.js.map`). No manifest, service worker, or HTML.

---

## 2. Design decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **PWA as mode vs target** | Add both `--mode pwa` and `--target pwa` (same as `js`) | Consistency with existing `--target js`; users can say "compile to PWA". |
| **Output semantics** | PWA mode ⇒ output is a **directory** | PWA = multiple files (JS, manifest, SW, index.html). |
| **Default output when PWA** | Directory named from input, e.g. `./MyApp` for `MyApp.malda` | Mirrors "output is a folder"; avoid overwriting a single file. |
| **Manifest/SW content** | Minimal embedded templates in compiler (name from input/app) | No external config file in v1; keep implementation simple. |
| **Source map** | Emit `.js` + `.js.map` into PWA output dir | Same as current JS mode; helps debugging. |

---

## 3. Artifacts to generate (PWA mode)

1. **`<outputDir>/index.html`**  
   - Minimal HTML5 page.  
   - Links to manifest; loads main script (e.g. `app.js`).  
   - Registers service worker from a small inline or linked script.

2. **`<outputDir>/manifest.webmanifest`** (or `manifest.json`)  
   - `name`, `short_name` (from app name or input filename).  
   - `start_url`: `"/"` or `"./index.html"`.  
   - `display`: `standalone`.  
   - `icons`: optional placeholder (e.g. one 192x192 data URL or omit for v1).

3. **`<outputDir>/sw.js`** (service worker)  
   - Minimal fetch handler: cache-first for same-origin assets (e.g. `.js`, `.html`, manifest).  
   - No external dependencies; keep it short and static.

4. **`<outputDir>/<app>.js`** (+ `<app>.js.map`)**  
   - Existing transpiled JS and source map; same content as current `--mode js` output.

---

## 4. Files to modify / add

### 4.1 Compiler

| File | Change |
|------|--------|
| `MaldaLang.Compiler/Compiler.cs` | (1) Add `CompilationMode.PWA`. (2) In `Compile()`, add `if (mode == CompilationMode.PWA) return CompileToPwa(...)`. (3) Add `CompileToPwa(string sourcePath, string outputDir)` that: reads source; calls existing `TranspileToJavaScriptArtifactsFromSource`; creates `outputDir`; writes JS + map into it; writes generated `index.html`, `manifest.webmanifest`, `sw.js` (templates in code or embedded resources). Return `CompilationResult { Success, OutputPath = outputDir }`. |

**Optional:** Extract small helper class `PwaShellGenerator` in same project (or same file) that takes (outputDir, appName, mainScriptName) and writes the three static files, to keep `Compiler.cs` readable.

### 4.2 CLI (Program.cs)

| Location | Change |
|----------|--------|
| `TryParseCompilationMode` | Add `case "pwa": compilationModeStr = "PWA"; isPwaMode = true; return true;`. (Introduce `isPwaMode` out parameter; can share with JS for “output is directory” behavior.) |
| `TryParseCompilationTarget` | Add `if (normalized == "pwa") { compilationModeStr = "PWA"; isPwaMode = true; return true; }`. |
| Compile path (all three: `-c`, `compile` subcommand, REPL compile) | When mode is PWA: (1) If `-o` is set, treat it as directory (create if missing); else default e.g. `Path.Combine(CurrentDirectory, Path.GetFileNameWithoutExtension(inputPath))`. (2) Ensure output path has no `.js` extension when in PWA mode (use directory). (3) Call compiler with `CompilationMode.PWA` and directory path. |
| Help / usage strings | Update to include `pwa`: e.g. "Use 'interpreter', 'transpile', 'dll', 'js', or 'pwa'" and "Use 'js' or 'pwa'" for `--target`. Mention that PWA output is a directory. |

**Note:** `GetCompilationOutputType()` should return something like `"PWA"` for PWA mode (for messages). Extension for default output when generating path: PWA ⇒ no extension (directory).

---

## 5. Implementation order

1. **Compiler:** Add `CompilationMode.PWA` and `CompileToPwa(sourcePath, outputDir)` with minimal templates (index.html, manifest.webmanifest, sw.js) and existing JS + map writing. No CLI yet; test via unit test or small driver that calls `Compiler.Compile(..., CompilationMode.PWA, ...)`.
2. **CLI:** Add `isPwaMode` (or equivalent) and PWA branch in `TryParseCompilationMode` / `TryParseCompilationTarget`; wire output directory handling and call `Compile(..., PWA, ...)`. Update help.
3. **Docs:** Add one-line to main help and, if present, to any “compile” or “transpile” section in the reference manual / README (e.g. “`--target pwa` produces a PWA directory”).

---

## 6. Acceptance criteria

- [ ] `malda compile app.malda --target pwa` creates a directory (default: `./app`) containing `index.html`, `manifest.webmanifest`, `sw.js`, `app.js`, `app.js.map`.
- [ ] `malda compile app.malda -o dist --mode pwa` creates `dist/` with the same artifacts; main script name derived from `app.malda` (e.g. `app.js`).
- [ ] Serving the output directory over HTTP (or via a simple static server) allows “Add to Home Screen” / install prompt when opened in a PWA-capable browser (manifest and SW present and valid).
- [ ] Existing `--mode js` / `--target js` behavior unchanged (single file output).
- [ ] Help text documents `pwa` as a mode and target option and states that PWA output is a directory.

---

## 7. Out of scope (v1)

- Custom manifest name/icons via CLI flags or config file.
- Placeholder icon generation (optional single icon or link to external icon).
- Offline-first or advanced SW strategies (beyond simple cache for app assets).
- Separate `malda transpile` subcommand (PWA is a target of `compile` only).

---

## 8. Testing

- **Unit:** New test in `MaldaLang.Tests` (or `MaldaLang.Compiler.Tests` if present): compile a minimal `.malda` with `CompilationMode.PWA` to a temp directory; assert directory exists and contains `index.html`, `manifest.webmanifest`, `sw.js`, one `.js` and one `.js.map` with expected content (e.g. manifest has `"display": "standalone"`).
- **Manual:** Run `malda compile Examples/Web/js/some.malda --target pwa -o pwa-out`, serve `pwa-out` with a static server, open in browser and confirm installability / SW registration if desired.

---

## 9. Reference: PWA checklist (minimal)

- [Manifest](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps/Installable_PWAs): `name`, `short_name`, `start_url`, `display`, optional `icons`.
- [Service worker](https://developer.mozilla.org/en-US/docs/Web/API/Service_Worker_API): register from main page; SW caches app shell / JS for offline.
- HTTPS (or localhost) required for install prompt; our plan only generates files, deployment is user’s responsibility.
