// Generate Table of Contents on the home page (grouped by category).
// Prefers chapters.json over HTTP; falls back to FALLBACK_TOC_CHAPTERS offline.

const TOC_CATEGORY_LABELS_IT = {
    'Language Fundamentals': 'Fondamenti del linguaggio',
    'Standard Library': 'Libreria standard',
    'AI & Agents': 'AI e agenti',
    'Web': 'Web',
    'Platform': 'Piattaforma',
    'Reference': 'Riferimento'
};

function isItalianManual() {
    return document.documentElement.lang === 'it';
}

function tocCategoryLabel(category) {
    if (!isItalianManual()) {
        return category;
    }
    return TOC_CATEGORY_LABELS_IT[category] || category;
}

const TOC_CATEGORY_ORDER = [
    'Language Fundamentals',
    'Standard Library',
    'AI & Agents',
    'Web',
    'Platform',
    'Reference'
];

const FALLBACK_TOC_CHAPTERS = [
    { file: "01-introduction.html", title: "Introduction", num: "1", category: "Language Fundamentals", description: "Language overview and features" },
    { file: "02-tools.html", title: "Tools & Tooling", num: "2", category: "Language Fundamentals", description: "Interpreter, compiler, package manager, and CLI command index" },
    { file: "03-lexical-structure.html", title: "Lexical Structure", num: "3", category: "Language Fundamentals", description: "Comments, identifiers, keywords, operators" },
    { file: "04-data-types.html", title: "Data Types", num: "4", category: "Language Fundamentals", description: "Primitive types, sum types, schema, typeOf" },
    { file: "05-variables.html", title: "Variables", num: "5", category: "Language Fundamentals", description: "Declaration, assignment, scope" },
    { file: "06-arrays.html", title: "Arrays", num: "6", category: "Language Fundamentals", description: "Array operations and methods" },
    { file: "07-expressions.html", title: "Expressions", num: "7", category: "Language Fundamentals", description: "Arithmetic, comparison, logical operations" },
    { file: "08-control-structures.html", title: "Control Structures", num: "8", category: "Language Fundamentals", description: "Conditionals and loops" },
    { file: "09-functions.html", title: "Functions", num: "9", category: "Language Fundamentals", description: "Function declaration, calls, decorators" },
    { file: "10-prompts.html", title: "Prompts", num: "10", category: "Language Fundamentals", description: "Prompt templates, await, schemas, and tools" },
    { file: "11-classes-objects.html", title: "Classes & Objects", num: "11", category: "Language Fundamentals", description: "OOP features, inheritance" },
    { file: "12-input-output.html", title: "Input/Output", num: "12", category: "Standard Library", description: "Console, files, paths, environment, host I/O, and capability tokens" },
    { file: "13-built-in-functions.html", title: "Built-in Functions", num: "13", category: "Standard Library", description: "Stdlib catalog: conversion, math, strings, files, JSON, dates; indexes into host, AI, and web helpers" },
    { file: "14-neural-nets.html", title: "Neural Nets", num: "14", category: "Standard Library", description: "Dense layers, activations, local derivatives, and one backward step" },
    { file: "15-graphs.html", title: "Graphs", num: "15", category: "Standard Library", description: "Graph data structures and algorithms" },
    { file: "16-vectordb.html", title: "VectorDB", num: "16", category: "Standard Library", description: "Vector database for similarity search" },
    { file: "17-database.html", title: "Database Support", num: "17", category: "Standard Library", description: "SQLite, SQL Server, and PostgreSQL clients" },
    { file: "18-actors.html", title: "Actors", num: "18", category: "AI & Agents", description: "Actor model and message passing" },
    { file: "19-agent-orchestration.html", title: "Agent Orchestration", num: "19", category: "AI & Agents", description: "LLM clients, agents, tools, teams, and agent kits" },
    { file: "20-graph-memory.html", title: "GraphMemory", num: "20", category: "AI & Agents", description: "Semantic memory system for AI agents" },
    { file: "21-mcp-server.html", title: "MCP Server", num: "21", category: "AI & Agents", description: "MCP protocol support" },
    { file: "22-acp.html", title: "ACP (Agent Communication Protocol)", num: "22", category: "AI & Agents", description: "Agent communication and collaboration protocol" },
    { file: "23-durable-workflows.html", title: "Durable Workflows", num: "23", category: "AI & Agents", description: "Durable workflow syntax, runtime model, CLI, DLQ, and operations" },
    { file: "24-agentic-runs.html", title: "Agentic Runs", num: "24", category: "AI & Agents", description: "How to test, record, time-limit, remember, restrict, and diagnose agent programs" },
    { file: "25-web-ui-hub.html", title: "Web UI Overview", num: "25", category: "Web", description: "Choose server components, HttpServer pages, browser JS, or games" },
    { file: "26-web-ui.html", title: "Web UI Server Components", num: "26", category: "Web", description: "Server components, fragments, live updates, and ui.*" },
    { file: "27-http-server-html-ui.html", title: "HttpServer & HTML UI Generation", num: "27", category: "Web", description: "Route-first hosting, @PAGE, @AIPAGE, and HTML generation" },
    { file: "28-browser-javascript-backend.html", title: "Browser JavaScript UI Backend", num: "28", category: "Web", description: "Browser-hosted MALDA compiled to JavaScript" },
    { file: "29-rest-api.html", title: "REST API Server", num: "29", category: "Web", description: "Creating REST APIs" },
    { file: "30-rest-web-client.html", title: "REST Web Client", num: "30", category: "Web", description: "Making HTTP requests to REST APIs" },
    { file: "31-full-stack-development.html", title: "Full-Stack Development with MALDA", num: "31", category: "Web", description: "End-to-end full-stack architecture and implementation flow" },
    { file: "32-dotnet-interop.html", title: ".NET Interop", num: "32", category: "Platform", description: "Loading and using external .NET libraries" },
    { file: "33-device-integration.html", title: "Device Integration", num: "33", category: "Platform", description: "Control physical devices (Arduino, ESP32, IoT, etc.)" },
    { file: "34-personal-assistant.html", title: "Personal Assistant and CLI", num: "34", category: "Platform", description: "Assistant commands, scheduling, and channels" },
    { file: "35-examples.html", title: "Examples", num: "35", category: "Reference", description: "First Look, Examples/ catalog, and one snippet per track" },
    { file: "36-property-testing.html", title: "Property Testing", num: "36", category: "Reference", description: "Deterministic property tests, shrinking, and regression workflows" },
    { file: "37-grammar.html", title: "Grammar", num: "37", category: "Reference", description: "BNF-like grammar specification" },
    { file: "38-appendix.html", title: "Appendix", num: "38", category: "Reference", description: "Reserved words, operator precedence" },
    { file: "39-appendix-gpu-billiards.html", title: "Appendix: GPU Billiards", num: "39", category: "Reference", description: "Playable compiled GPU billiards showcase" },
];

const FALLBACK_TOC_CHAPTERS_IT = [
    { file: "01-introduction.html", title: "Introduzione", num: "1", category: "Language Fundamentals", description: "Panoramica del linguaggio e delle funzionalità" },
    { file: "02-tools.html", title: "Strumenti e toolchain", num: "2", category: "Language Fundamentals", description: "Interprete, compilatore, gestore di pacchetti e indice dei comandi CLI" },
    { file: "03-lexical-structure.html", title: "Struttura lessicale", num: "3", category: "Language Fundamentals", description: "Commenti, identificatori, keyword, operatori" },
    { file: "04-data-types.html", title: "Tipi di dati", num: "4", category: "Language Fundamentals", description: "Tipi primitivi, tipi somma, schema, typeOf" },
    { file: "05-variables.html", title: "Variabili", num: "5", category: "Language Fundamentals", description: "Dichiarazione, assegnamento, scope" },
    { file: "06-arrays.html", title: "Array", num: "6", category: "Language Fundamentals", description: "Operazioni e metodi sugli array" },
    { file: "07-expressions.html", title: "Espressioni", num: "7", category: "Language Fundamentals", description: "Operazioni aritmetiche, di confronto e logiche" },
    { file: "08-control-structures.html", title: "Strutture di controllo", num: "8", category: "Language Fundamentals", description: "Condizionali e cicli" },
    { file: "09-functions.html", title: "Funzioni", num: "9", category: "Language Fundamentals", description: "Dichiarazione, chiamate, decoratori" },
    { file: "10-prompts.html", title: "Prompt", num: "10", category: "Language Fundamentals", description: "Template di prompt, await, schema e tool" },
    { file: "11-classes-objects.html", title: "Classi e oggetti", num: "11", category: "Language Fundamentals", description: "Funzionalità OOP, ereditarietà" },
    { file: "12-input-output.html", title: "Input/Output", num: "12", category: "Standard Library", description: "Console, file, path, ambiente, I/O dell'host e token di capability" },
    { file: "13-built-in-functions.html", title: "Funzioni built-in", num: "13", category: "Standard Library", description: "Catalogo stdlib: conversioni, math, stringhe, file, JSON, date; indici verso helper host, AI e web" },
    { file: "14-neural-nets.html", title: "Reti neurali", num: "14", category: "Standard Library", description: "Layer densi, attivazioni, derivate locali e un passo di backward" },
    { file: "15-graphs.html", title: "Grafi", num: "15", category: "Standard Library", description: "Strutture dati a grafo e algoritmi" },
    { file: "16-vectordb.html", title: "VectorDB", num: "16", category: "Standard Library", description: "Database vettoriale per la similarity search" },
    { file: "17-database.html", title: "Supporto database", num: "17", category: "Standard Library", description: "Client SQLite, SQL Server e PostgreSQL" },
    { file: "18-actors.html", title: "Actor", num: "18", category: "AI & Agents", description: "Modello ad actor e message passing" },
    { file: "19-agent-orchestration.html", title: "Orchestrazione di agenti", num: "19", category: "AI & Agents", description: "Client LLM, agenti, tool, team e kit di agenti" },
    { file: "20-graph-memory.html", title: "GraphMemory", num: "20", category: "AI & Agents", description: "Memoria semantica per agenti AI" },
    { file: "21-mcp-server.html", title: "Server MCP", num: "21", category: "AI & Agents", description: "Supporto del protocollo MCP" },
    { file: "22-acp.html", title: "ACP (Agent Communication Protocol)", num: "22", category: "AI & Agents", description: "Protocollo di comunicazione e collaborazione tra agenti" },
    { file: "23-durable-workflows.html", title: "Workflow durevoli", num: "23", category: "AI & Agents", description: "Sintassi dei workflow durevoli, modello di runtime, CLI, DLQ e operazioni" },
    { file: "24-agentic-runs.html", title: "Esecuzioni agentiche", num: "24", category: "AI & Agents", description: "Come testare, registrare, limitare nel tempo, ricordare, restringere e diagnosticare i programmi agentici" },
    { file: "25-web-ui-hub.html", title: "Panoramica Web UI", num: "25", category: "Web", description: "Scegliere componenti server, pagine HttpServer, JS nel browser o giochi" },
    { file: "26-web-ui.html", title: "Componenti server Web UI", num: "26", category: "Web", description: "Componenti server, fragment, aggiornamenti live e ui.*" },
    { file: "27-http-server-html-ui.html", title: "HttpServer e generazione UI HTML", num: "27", category: "Web", description: "Hosting route-first, @PAGE, @AIPAGE e generazione HTML" },
    { file: "28-browser-javascript-backend.html", title: "Backend UI JavaScript nel browser", num: "28", category: "Web", description: "MALDA ospitato nel browser e compilato in JavaScript" },
    { file: "29-rest-api.html", title: "Server REST API", num: "29", category: "Web", description: "Creare REST API" },
    { file: "30-rest-web-client.html", title: "Client REST Web", num: "30", category: "Web", description: "Richieste HTTP verso REST API" },
    { file: "31-full-stack-development.html", title: "Sviluppo full-stack con MALDA", num: "31", category: "Web", description: "Architettura full-stack end-to-end e flusso di implementazione" },
    { file: "32-dotnet-interop.html", title: "Interop .NET", num: "32", category: "Platform", description: "Caricare e usare librerie .NET esterne" },
    { file: "33-device-integration.html", title: "Integrazione dispositivi", num: "33", category: "Platform", description: "Controllare dispositivi fisici (Arduino, ESP32, IoT, ecc.)" },
    { file: "34-personal-assistant.html", title: "Assistente personale e CLI", num: "34", category: "Platform", description: "Comandi dell'assistente, scheduling e canali" },
    { file: "35-examples.html", title: "Esempi", num: "35", category: "Reference", description: "First Look, catalogo Examples/ e un snippet per traccia" },
    { file: "36-property-testing.html", title: "Property testing", num: "36", category: "Reference", description: "Property test deterministici, shrinking e workflow di regressione" },
    { file: "37-grammar.html", title: "Grammatica", num: "37", category: "Reference", description: "Specifica della grammatica in stile BNF" },
    { file: "38-appendix.html", title: "Appendice", num: "38", category: "Reference", description: "Parole riservate, precedenza degli operatori" },
    { file: "39-appendix-gpu-billiards.html", title: "Appendice: biliardo GPU", num: "39", category: "Reference", description: "Showcase biliardo GPU compilato e giocabile" },
];

function chaptersToTocItems(chapters) {
    const items = [];
    let num = 0;
    chapters.forEach(function(chapter) {
        if (chapter.isHome || chapter.isCourse) {
            return;
        }
        num += 1;
        items.push({
            file: chapter.file,
            title: chapter.title,
            num: String(num),
            category: chapter.category || 'Reference',
            description: chapter.description || ''
        });
    });
    return items;
}

function chapterLabel(chapter) {
    const numPrefix = chapter.num ? chapter.num + '. ' : '';
    return numPrefix + chapter.title;
}

function renderTableOfContents(tocContainer, chapters) {
    let tocHTML = '';

    TOC_CATEGORY_ORDER.forEach(function(category) {
        const categoryChapters = chapters
            .filter(function(ch) { return ch.category === category; })
            .sort(function(a, b) { return parseInt(a.num, 10) - parseInt(b.num, 10); });

        if (categoryChapters.length === 0) {
            return;
        }

        tocHTML += '<details class="toc-category" open>\n';
        tocHTML += '  <summary>' + tocCategoryLabel(category) + '</summary>\n';
        tocHTML += '  <ul>\n';

        categoryChapters.forEach(function(chapter) {
            tocHTML += '    <li><a href="' + chapter.file + '">' + chapterLabel(chapter) + '</a>';
            if (chapter.description) {
                tocHTML += ' - ' + chapter.description;
            }
            tocHTML += '</li>\n';
        });

        tocHTML += '  </ul>\n';
        tocHTML += '</details>\n';
    });

    const hub = chapters.find(function(ch) { return ch.file === '25-web-ui-hub.html'; });
    if (hub) {
        tocHTML += '\n<div class="info-box" style="margin-top: 20px;">\n';
        tocHTML += '    <strong>Web UI:</strong> ' + (isItalianManual()
            ? 'Non sai quale capitolo UI leggere? Inizia da <a href="' + hub.file + '">' + chapterLabel(hub) + '</a>.'
            : 'Not sure which UI chapter to read? Start with <a href="' + hub.file + '">' + chapterLabel(hub) + '</a>.') + '\n';
        tocHTML += '</div>\n';
    }

    tocContainer.innerHTML = tocHTML;

    const navFooter = document.querySelector('.nav-footer');
    if (navFooter && chapters.length > 0) {
        const nextLink = navFooter.querySelector('a[href*="01-introduction"], a:last-child');
        if (nextLink) {
            const firstChapter = chapters[0];
            nextLink.textContent = (isItalianManual() ? 'Successivo: ' : 'Next: ') + chapterLabel(firstChapter) + '→';
            nextLink.setAttribute('href', firstChapter.file);
        }
    }
}

async function generateTableOfContents() {
    const tocContainer = document.getElementById('table-of-contents');
    if (!tocContainer) {
        return;
    }

    try {
        const response = await fetch('chapters.json');
        if (response.ok) {
            const data = await response.json();
            renderTableOfContents(tocContainer, chaptersToTocItems(data.chapters));
            return;
        }
    } catch (err) {
        // file:// and other offline contexts fall back to embedded data
    }

    renderTableOfContents(tocContainer, isItalianManual() ? FALLBACK_TOC_CHAPTERS_IT : FALLBACK_TOC_CHAPTERS);
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', generateTableOfContents);
} else {
    generateTableOfContents();
}
