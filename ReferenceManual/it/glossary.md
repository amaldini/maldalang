# Glossario italiano del Reference Manual

L’inglese in `ReferenceManual/` resta la fonte di verità. Questo file fissa
i termini della traduzione in `ReferenceManual/it/`, così i capitoli non
oscillano. Le keyword MALDA, i nomi built-in, i flag CLI e i path **non** si
traducono.

## Non tradurre (lasciare in inglese / come nel sorgente)

| Forma | Motivo |
|-------|--------|
| Keyword (`function`, `var`, `prompt`, `schema`, `match`, `await`, `actor`, …) | Sintassi |
| Built-in (`io.print`, `math.sqrt`, `str.upper`, `ui.button`, `validate`, …) | API |
| Flag e comandi CLI (`malda compile`, `--mode transpile`, `--strict-types`) | Superficie del tool |
| Codici diagnostica (`WF1001`, `UI1002`, `CS1001`) | Identificatori |
| Path di repo (`Examples/Basics/hello_world.malda`, `docs/spec/…`) | Navigazione |
| Contenuto di ogni `<pre><code>` (inclusi commenti `//` nel codice) | I test confrontano i blocchi con l’inglese |
| `data-expect` e l’output stampato dagli snippet | Esecuzione |
| Nomi di protocollo e prodotti (`MCP`, `ACP`, `OpenAI`, `GGUF`, `WebView2`) | Nomi propri |
| `MALDA`, `VectorDB`, `GraphMemory`, `UIHost`, `HttpServer` | Nomi propri del linguaggio |

## Termini fissi

| English | Italiano |
|---------|----------|
| Reference Manual | Manuale di riferimento |
| Home (nav / breadcrumb) | Indice |
| Language Fundamentals | Fondamenti del linguaggio |
| Standard Library | Libreria standard |
| AI & Agents | AI e agenti |
| Web | Web |
| Platform | Piattaforma |
| Reference (category) | Riferimento |
| built-in / built-in function | built-in / funzione built-in |
| standard library | libreria standard |
| transpile / transpiler | transpile / transpiler |
| interpreter | interprete |
| compiler | compilatore |
| runtime | runtime |
| toolchain | toolchain |
| language server | language server |
| Desktop IDE | Desktop IDE |
| Web IDE | Web IDE |
| playground | playground |
| prompt (language construct) | `prompt` (nel codice); “prompt” nel testo |
| schema (language construct) | `schema` (nel codice); “schema” nel testo |
| agent | agente |
| actor | actor (nel codice); “actor” nel testo, plurale “actor” |
| durable workflow | workflow durevole |
| decorator | decoratore |
| snippet | snippet |
| reserved word / keyword | parola riservata / keyword |
| identifier | identificatore |
| lexical structure | struttura lessicale |
| control structure | struttura di controllo |
| expression | espressione |
| statement | istruzione |
| assignment | assegnamento |
| scope | scope |
| type hint | type hint (suggerimento di tipo IDE/LSP) |
| type annotation | annotazione di tipo |
| property testing | property testing |
| shrinking | shrinking |
| grammar | grammatica |
| appendix | appendice |
| See Also | Vedi anche |
| Previous | Precedente |
| Next | Successivo |
| Print / PDF | Stampa / PDF |
| Copy / Copied! | Copia / Copiato! |
| What ships today | Cosa viene distribuito oggi |
| Quick Start | Avvio rapido |
| Table of Contents | Indice dei capitoli |
| Getting Started | Per iniziare |
| Overview | Panoramica |
| Examples | Esempi |
| Version | Versione |
| AI-First Programming Language | linguaggio di programmazione AI-First |
| capability token | token di capability |
| mint (a token) | emettere (un token) |
| confine / attenuate | restringere |
| unforgeable | non contraffabile |
| forged dict | dict contraffatto |
| host (program that mints tokens) | host |

## Intestazioni di pagina

- `<html lang="it">`
- Header: `Manuale di riferimento MALDA™`
- Sottotitolo: `Il linguaggio di programmazione AI-First - Versione {CLI}`
- `<title>` capitolo: `{N}. {Titolo} - Manuale di riferimento MALDA`
- Breadcrumb: `Indice / {N}. {Titolo}`
- Footer di licenza: lasciare i nomi MIT / Apache License 2.0; “at your option” → “a tua scelta”

## Link e asset

Pagine in `ReferenceManual/it/`:

- CSS/JS condivisi: `href="../styles.css"` (e analoghi)
- Capitoli fratelli: `01-introduction.html` (stessa cartella)
- Documenti del repo: `href="../../docs/..."` (un livello in più rispetto all’inglese)
