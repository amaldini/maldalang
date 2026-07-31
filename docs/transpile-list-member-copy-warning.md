# Warning: liste su membri oggetto in modalità transpile (`GetArray` / copie)

Questa nota serve a riconoscere **in anticipo** bug del tipo *“append su una lista e `length` letto su un’altra”* quando il valore è memorizzato come `List<RuntimeValue>` e viene esposto tramite accesso a proprietà su dizionari (`obj.member`).

## Sintomi tipici

- Un ciclo del tipo `while (oggetto.lista.length < n) { oggetto.lista.append(...); }` **non termina** o sembra ignorare gli `append`.
- In profiling compaiono **milioni** di chiamate a funzioni che dovrebbero essere eseguite **O(1) o O(n)** rispetto a `n` (es. creazione elementi in un numero proporzionale al target, non proporzionale al numero di iterazioni del loop “infinito”).
- Comportamento **diverso** tra interprete e programma transpilato, se l’interprete non passa dallo stesso percorso di copia.

## Causa tecnica

Storicamente, `RuntimeHelpers.GetArray` materializzava una **nuova** `List<object>` (copia) **a ogni lettura** su `List<RuntimeValue>`. Ora la conversione è **memorizzata per identità** (vedi sotto).

Se ogni accesso crea una copia nuova:

1. **`append`** muta una copia temporanea non salvata nel contenitore.
2. **`length`** (o `.Count` sulla lista) legge **un’altra** copia, ancora allineata allo stato originale → la lunghezza **non aumenta** come ci si aspetta.

Il risultato è coerente con profiling che mostra ripetute allocazioni e chiamate “di setup” in loop.

## Mitigazione generale (runtime transpilato)

**`GetArray`** usa una cache per **identità**: `ConditionalWeakTable<List<RuntimeValue>, List<object>>`. La prima volta che si converte un dato `List<RuntimeValue>`, si calcola la `List<object>` e si memorizza; le letture successive sulla **stessa istanza** restituiscono lo **stesso** riferimento. Così `append` e `length` concordano anche senza passare da un dizionario.

File: `MaldaLang.Compiler/CSharpTranspiler.cs` (generazione di `GetArray`, campo `__rvListToObjectListCache`, helper `MaterializeRuntimeValueList`).

**Nota:** se in futuro il `List<RuntimeValue>` venisse mutato **direttamente** dal lato C#/interprete dopo la prima materializzazione, la `List<object>` in cache potrebbe non riflettere più quelle mutazioni (scenario raro nel transpile puro; le mutazioni tipiche passano dalla `List<object>` restituita da `GetArray`).

## Come investigare casi simili in futuro

1. Verificare che **`GetArray`** non crei copie duplicate (cache per identità attiva nel transpiler).
2. Confrontare **transpile vs interpretato** su un micro-test: `append` + `length` sullo stesso membro.
3. Se emergono sintomi analoghi senza passare da `GetArray` sulla stessa istanza, cercare altri percorsi di materializzazione.
4. Ricordare che il ramo **`ObjectInstance`** di `GetObjectMember` può avere dinamiche diverse rispetto ai dizionari: se emergono sintomi analoghi su oggetti Malda non basati su dizionario, valutare una mitigazione coerente anche lì.

## Riepilogo operativo

| Aspetto | Dettaglio |
|--------|-----------|
| Pattern a rischio | `oggetto.campoLista.append(...)` + `oggetto.campoLista.length` con storage `List<RuntimeValue>` |
| Indizio forte | Loop su `length` che non converge; profiling con chiamate ripetute sproporzionate |
| Direzione fix | **Un solo** riferimento `List<object>` per istanza di `List<RuntimeValue>` (`GetArray` + `ConditionalWeakTable`) |

---

*Aggiunto per documentare l’incidente “annealing chains” e analoghi. La cache in `GetArray` è la risposta a livello di linguaggio/runtime transpilato; aggiornare questo file se si estende il supporto ad altri tipi di contenitori o membri.*
