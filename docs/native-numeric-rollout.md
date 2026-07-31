# Native Numeric Rollout (Transpile)

Questa nota documenta l'implementazione del rollout "native numeric" per la transpilation C#, con fallback dinamico compatibile.

## Obiettivo

Ridurre i costi runtime dei path numerici caldi facendo emettere al transpiler codice C# nativo (`double`) quando il tipo e' provabile, mantenendo la semantica legacy su casi misti/dinamici.

## Flag di rollout

Il controllo avviene con:

- `--typed-transpile-level 0|1|2`

Valori:

- `0` = **legacy dynamic** (comportamento storico, nessuna ottimizzazione typed).
- `1` = **typed-safe** (default): typing conservativo, fallback dinamico quando non c'e' certezza.
- `2` = **typed-aggressive**: include anche lowering per container numerici (`List<double>`) su hint supportati.

Esempio:

```powershell
dotnet run --project MaldaLang -- compile "Examples/Basics/hello_world.malda" --mode transpile -o "artifacts/native_numeric_level2.exe" --typed-transpile-level 2
```

## Stato implementazione per fase

### Fase 1 - Type environment

- Scope typed consolidato in `MaldaLang.Compiler/CSharpTranspiler.cs`.
- Registrazione/lookup coerente per variabili locali e parametri.
- Fallback conservativo a `object`.
- Hint non supportati gestiti con diagnostica compile-time (eccezione esplicita lato transpiler).

### Fase 2 - Propagazione tipi espressioni

- Propagazione `double` su:
  - letterali numerici,
  - identificatori typed,
  - call con return signature nota,
  - alcuni accessi indicizzati typed.
- Mantiene fallback dinamico su rami non dimostrabili.

### Fase 3 - Lowering operatori nativi

- Per operatori numerici (`+ - * / %`) emette operatori C# nativi quando entrambi gli operandi sono provati `double`.
- Per casi misti resta sul path `RuntimeHelpers`.

### Fase 4 - Specializzazione callsite

- Mappa firme funzione (parametri/return typed) in stato transpiler.
- Coercion argomenti a callsite quando la signature e' nota.
- Path dinamico invariato per callee non risolti.

### Fase 5 - Fast path built-in numerici

- Fast path diretto per built-in hot quando tipo noto:
  - `float(...)`
  - `abs(...)`
- Fallback a `BuiltInFunctions.CallBuiltIn` nei casi dinamici.

### Fase 6 - Typed class/actor fields

- Flusso field hint parser/AST/transpiler completato.
- Emissione typed e coercion coerente su assegnazioni ai campi.

### Fase 7 - Typed containers (aggressive)

- Introdotto `DoubleArray` transpiled type (livello 2).
- Runtime helpers generati:
  - `CoerceToDoubleList(object?)`
  - `ArrayAppendDouble(List<double>, object?)`
  - `GetIndexedDouble(List<double>, object?)`
- Supporto su `append`, `[]`, assegnazioni indicizzate e `length` per container typed.

### Fase 8 - Feature flags e safety

- Wiring end-to-end del livello typed:
  - CLI (`MaldaLang/Program.cs`)
  - compiler entrypoints (`MaldaLang.Compiler/Compiler.cs`)
  - transpiler (`MaldaLang.Compiler/CSharpTranspiler.cs`)
- Default sicuro su livello `1`.

### Fase 9 - Test e validazione

Test aggiunti/estesi:

- `MaldaLang.Tests/TranspilerStrongTypingTests.cs`
- `MaldaLang.Tests/TypeHintTests.cs`

Esecuzione mirata validata:

```powershell
dotnet test MaldaLang.Tests --filter "FullyQualifiedName~TranspilerStrongTypingTests|FullyQualifiedName~TypeHintTests"
```

## Procedura A/B consigliata

Per confronto controllato:

1. Compila lo stesso sorgente con livello `0` e `2`.
2. Esegui in **serie** (non in parallelo) per evitare conflitti di porta o risorse condivise.
3. Raccogli profile JSON (`--profile --profile-format json`).
4. Confronta almeno:
   - `float`
   - `numOr`
   - runtime totale
5. Usa media multi-run prima di dichiarare miglioramenti.

## Note operative

- L'interprete non e' stato modificato in questo rollout.
- Il livello `2` va usato inizialmente su workload controllati.
- Se serve rollback rapido: usare `--typed-transpile-level 0`.
