// Navigation and Interactive Features

const NAV_CATEGORY_ORDER = [
    'Language Fundamentals',
    'Standard Library',
    'AI & Agents',
    'Web',
    'Platform',
    'Reference'
];

const NAV_CATEGORY_LABELS_IT = {
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

function manualPageName() {
    const path = window.location.pathname || '';
    const page = path.split('/').pop() || 'index.html';
    return page.split('?')[0] || 'index.html';
}

function peerLocaleHref() {
    const page = manualPageName();
    return isItalianManual() ? '../' + page : 'it/' + page;
}

function categoryLabel(category) {
    if (!isItalianManual()) {
        return category;
    }
    return NAV_CATEGORY_LABELS_IT[category] || category;
}

function manualStrings() {
    if (isItalianManual()) {
        return {
            openContents: 'Apri indice',
            closeContents: 'Chiudi indice',
            print: 'Stampa / PDF',
            printShort: 'PDF',
            printTitle: 'Stampa questo capitolo (A4, codice adattato alla pagina)',
            copy: 'Copia',
            copied: 'Copiato!',
            copyAria: 'Copia il codice negli appunti',
            langSwitch: 'English',
            langSwitchShort: 'EN',
            langSwitchTitle: 'English version of this page',
            searchLabel: 'Cerca nel manuale',
            searchPlaceholder: 'Cerca (cap, prompt, workflow…)',
            searchHint: 'Digita / oppure Ctrl+K',
            searchNoResults: 'Nessun termine o sezione corrisponde',
            searchGlossary: 'Glossario',
            searchHeading: 'Sezione',
            searchChapter: 'Capitolo',
            browseGlossary: 'Glossario',
            headerSearch: 'Cerca',
            headerSearchShort: 'Cerca',
            headerSearchTitle: 'Apri la ricerca del manuale'
        };
    }
    return {
        openContents: 'Open contents',
        closeContents: 'Close contents',
        print: 'Print / PDF',
        printShort: 'PDF',
        printTitle: 'Print this chapter (A4, code wrapped to the page)',
        copy: 'Copy',
        copied: 'Copied!',
        copyAria: 'Copy code to clipboard',
        langSwitch: 'Italiano',
        langSwitchShort: 'IT',
        langSwitchTitle: 'Versione italiana di questa pagina',
        searchLabel: 'Search the manual',
        searchPlaceholder: 'Search (cap, prompt, workflow…)',
        searchHint: 'Type / or Ctrl+K',
        searchNoResults: 'No matching terms or sections',
        searchGlossary: 'Glossary',
        searchHeading: 'Section',
        searchChapter: 'Chapter',
        browseGlossary: 'Glossary',
        headerSearch: 'Search',
        headerSearchShort: 'Find',
        headerSearchTitle: 'Open manual search'
    };
}

document.addEventListener('DOMContentLoaded', function() {
    if (document.body.style.zoom !== undefined) {
        document.body.style.zoom = '1';
    }

    injectHreflang();
    rewriteMarkdownLinksForGitHubPages();

    injectNavbar().then(function() {
        initNavigation();
        initCollapsibleNav();
        initNavDrawer();
        initManualSearch();
    });

    initHeaderActions();
    syncHeaderOffset();
    requestAnimationFrame(syncHeaderOffset);
    window.addEventListener('resize', syncHeaderOffset);
    initCodeCopy();
    initSmoothScroll();
    highlightActiveSection();
    initGlossaryPage();
});

function injectHreflang() {
    const page = manualPageName();
    const enHref = isItalianManual() ? '../' + page : page;
    const itHref = isItalianManual() ? page : 'it/' + page;
    const links = [
        { hreflang: 'en', href: enHref },
        { hreflang: 'it', href: itHref }
    ];
    links.forEach(function(item) {
        const existing = document.head.querySelector('link[rel="alternate"][hreflang="' + item.hreflang + '"]');
        if (existing) {
            return;
        }
        const link = document.createElement('link');
        link.rel = 'alternate';
        link.hreflang = item.hreflang;
        link.href = item.href;
        document.head.appendChild(link);
    });
}

// GitHub Pages publishes only ReferenceManual/*, so ../docs/*.md would 404 above
// the project root. Keep relative hrefs in the HTML for clones; rewrite them
// here when the chapter site is served from *.github.io.
function githubPagesBlobBase() {
    const host = location.hostname || '';
    if (!host.endsWith('.github.io')) {
        return null;
    }

    const user = host.slice(0, -'.github.io'.length);
    if (!user) {
        return null;
    }

    const segments = (location.pathname || '').split('/').filter(Boolean);
    const repo = (segments.length > 0 && segments[0].indexOf('.') === -1)
        ? segments[0]
        : (user + '.github.io');
    return 'https://github.com/' + user + '/' + repo + '/blob/main/';
}

function rewriteMarkdownLinksForGitHubPages() {
    const blobBase = githubPagesBlobBase();
    if (!blobBase) {
        return;
    }

    const virtualPage = 'https://pages.invalid/ReferenceManual/'
        + (isItalianManual() ? 'it/' : '')
        + 'page.html';

    document.querySelectorAll('a[href]').forEach(function(anchor) {
        const href = anchor.getAttribute('href');
        if (!href || /^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(href)) {
            return;
        }

        const hashIndex = href.indexOf('#');
        const pathPart = hashIndex >= 0 ? href.slice(0, hashIndex) : href;
        const hash = hashIndex >= 0 ? href.slice(hashIndex) : '';
        if (!/\.md$/i.test(pathPart)) {
            return;
        }

        let resolved;
        try {
            resolved = new URL(pathPart, virtualPage);
        } catch (e) {
            return;
        }

        const repoPath = resolved.pathname.replace(/^\/+/, '');
        if (!repoPath) {
            return;
        }

        anchor.setAttribute('href', blobBase + repoPath + hash);
    });
}

async function injectNavbar() {
    const nav = document.querySelector('nav');
    if (!nav) {
        console.error('Nav element not found');
        return;
    }

    try {
        const response = await fetch('chapters.json');
        if (response.ok) {
            const data = await response.json();
            const items = buildNavItemsFromChapters(data.chapters);
            renderCollapsibleNav(nav, items);
            return;
        }
    } catch (err) {
        // file:// and other offline contexts fall back to embedded data
    }

    renderCollapsibleNav(nav, getFallbackNavItems());
}

function buildNavItemsFromChapters(chapters) {
    const items = [{ href: 'index.html', text: 'Home', category: null, num: null }];
    let num = 0;

    chapters.forEach(function(chapter) {
        if (chapter.isHome) {
            return;
        }
        num += 1;
        items.push({
            href: chapter.file,
            text: num + '. ' + chapter.title,
            category: chapter.category || 'Reference',
            num: num
        });
    });

    return items;
}

function getFallbackNavItems() {
    return isItalianManual() ? FALLBACK_NAV_ITEMS_IT : FALLBACK_NAV_ITEMS;
}

const FALLBACK_NAV_ITEMS = [
        { href: "index.html", text: "Home", category: null },
        { href: "01-introduction.html", text: "1. Introduction", category: "Language Fundamentals" },
        { href: "02-tools.html", text: "2. Tools & Tooling", category: "Language Fundamentals" },
        { href: "03-lexical-structure.html", text: "3. Lexical Structure", category: "Language Fundamentals" },
        { href: "04-data-types.html", text: "4. Data Types", category: "Language Fundamentals" },
        { href: "05-variables.html", text: "5. Variables", category: "Language Fundamentals" },
        { href: "06-arrays.html", text: "6. Arrays", category: "Language Fundamentals" },
        { href: "07-expressions.html", text: "7. Expressions", category: "Language Fundamentals" },
        { href: "08-control-structures.html", text: "8. Control Structures", category: "Language Fundamentals" },
        { href: "09-functions.html", text: "9. Functions", category: "Language Fundamentals" },
        { href: "10-prompts.html", text: "10. Prompts", category: "Language Fundamentals" },
        { href: "11-classes-objects.html", text: "11. Classes & Objects", category: "Language Fundamentals" },
        { href: "12-input-output.html", text: "12. Input/Output", category: "Standard Library" },
        { href: "13-built-in-functions.html", text: "13. Built-in Functions", category: "Standard Library" },
        { href: "14-graphs.html", text: "14. Graphs", category: "Standard Library" },
        { href: "15-vectordb.html", text: "15. VectorDB", category: "Standard Library" },
        { href: "16-database.html", text: "16. Database Support", category: "Standard Library" },
        { href: "17-actors.html", text: "17. Actors", category: "AI & Agents" },
        { href: "18-agent-orchestration.html", text: "18. Agent Orchestration", category: "AI & Agents" },
        { href: "19-graph-memory.html", text: "19. GraphMemory", category: "AI & Agents" },
        { href: "20-mcp-server.html", text: "20. MCP Server", category: "AI & Agents" },
        { href: "21-acp.html", text: "21. ACP (Agent Communication Protocol)", category: "AI & Agents" },
        { href: "22-durable-workflows.html", text: "22. Durable Workflows", category: "AI & Agents" },
        { href: "23-web-ui-hub.html", text: "23. Web UI Overview", category: "Web" },
        { href: "24-web-ui.html", text: "24. Web UI Server Components", category: "Web" },
        { href: "25-http-server-html-ui.html", text: "25. HttpServer & HTML UI Generation", category: "Web" },
        { href: "26-browser-javascript-backend.html", text: "26. Browser JavaScript UI Backend", category: "Web" },
        { href: "27-rest-api.html", text: "27. REST API Server", category: "Web" },
        { href: "28-rest-web-client.html", text: "28. REST Web Client", category: "Web" },
        { href: "29-full-stack-development.html", text: "29. Full-Stack Development with MALDA", category: "Web" },
        { href: "30-dotnet-interop.html", text: "30. .NET Interop", category: "Platform" },
        { href: "31-device-integration.html", text: "31. Device Integration", category: "Platform" },
        { href: "32-personal-assistant.html", text: "32. Personal Assistant and CLI", category: "Platform" },
        { href: "33-examples.html", text: "33. Examples", category: "Reference" },
        { href: "34-property-testing.html", text: "34. Property Testing", category: "Reference" },
        { href: "35-grammar.html", text: "35. Grammar", category: "Reference" },
        { href: "36-appendix.html", text: "36. Appendix", category: "Reference" },
        { href: "37-appendix-gpu-billiards.html", text: "37. Appendix: GPU Billiards", category: "Reference" },
];

const FALLBACK_NAV_ITEMS_IT = [
        { href: "index.html", text: "Indice", category: null },
        { href: "01-introduction.html", text: "1. Introduzione", category: "Language Fundamentals" },
        { href: "02-tools.html", text: "2. Strumenti e toolchain", category: "Language Fundamentals" },
        { href: "03-lexical-structure.html", text: "3. Struttura lessicale", category: "Language Fundamentals" },
        { href: "04-data-types.html", text: "4. Tipi di dati", category: "Language Fundamentals" },
        { href: "05-variables.html", text: "5. Variabili", category: "Language Fundamentals" },
        { href: "06-arrays.html", text: "6. Array", category: "Language Fundamentals" },
        { href: "07-expressions.html", text: "7. Espressioni", category: "Language Fundamentals" },
        { href: "08-control-structures.html", text: "8. Strutture di controllo", category: "Language Fundamentals" },
        { href: "09-functions.html", text: "9. Funzioni", category: "Language Fundamentals" },
        { href: "10-prompts.html", text: "10. Prompt", category: "Language Fundamentals" },
        { href: "11-classes-objects.html", text: "11. Classi e oggetti", category: "Language Fundamentals" },
        { href: "12-input-output.html", text: "12. Input/Output", category: "Standard Library" },
        { href: "13-built-in-functions.html", text: "13. Funzioni built-in", category: "Standard Library" },
        { href: "14-graphs.html", text: "14. Grafi", category: "Standard Library" },
        { href: "15-vectordb.html", text: "15. VectorDB", category: "Standard Library" },
        { href: "16-database.html", text: "16. Supporto database", category: "Standard Library" },
        { href: "17-actors.html", text: "17. Actor", category: "AI & Agents" },
        { href: "18-agent-orchestration.html", text: "18. Orchestrazione di agenti", category: "AI & Agents" },
        { href: "19-graph-memory.html", text: "19. GraphMemory", category: "AI & Agents" },
        { href: "20-mcp-server.html", text: "20. Server MCP", category: "AI & Agents" },
        { href: "21-acp.html", text: "21. ACP (Agent Communication Protocol)", category: "AI & Agents" },
        { href: "22-durable-workflows.html", text: "22. Workflow durevoli", category: "AI & Agents" },
        { href: "23-web-ui-hub.html", text: "23. Panoramica Web UI", category: "Web" },
        { href: "24-web-ui.html", text: "24. Componenti server Web UI", category: "Web" },
        { href: "25-http-server-html-ui.html", text: "25. HttpServer e generazione UI HTML", category: "Web" },
        { href: "26-browser-javascript-backend.html", text: "26. Backend UI JavaScript nel browser", category: "Web" },
        { href: "27-rest-api.html", text: "27. Server REST API", category: "Web" },
        { href: "28-rest-web-client.html", text: "28. Client REST Web", category: "Web" },
        { href: "29-full-stack-development.html", text: "29. Sviluppo full-stack con MALDA", category: "Web" },
        { href: "30-dotnet-interop.html", text: "30. Interop .NET", category: "Platform" },
        { href: "31-device-integration.html", text: "31. Integrazione dispositivi", category: "Platform" },
        { href: "32-personal-assistant.html", text: "32. Assistente personale e CLI", category: "Platform" },
        { href: "33-examples.html", text: "33. Esempi", category: "Reference" },
        { href: "34-property-testing.html", text: "34. Property testing", category: "Reference" },
        { href: "35-grammar.html", text: "35. Grammatica", category: "Reference" },
        { href: "36-appendix.html", text: "36. Appendice", category: "Reference" },
        { href: "37-appendix-gpu-billiards.html", text: "37. Appendice: biliardo GPU", category: "Reference" },
];

// Synced from glossary.json / it/glossary.json by scripts/sync-reference-manual-search-index.py
const FALLBACK_GLOSSARY_EN = [
    {
        "id": "capability-tokens",
        "term": "Capability tokens",
        "aliases": [
            "cap",
            "cap.fileRead",
            "cap.fileWrite",
            "cap.dirList",
            "cap.confine",
            "cap.read",
            "cap.write",
            "cap.list",
            "cap.is",
            "capability",
            "capability token"
        ],
        "href": "12-input-output.html#capability-tokens",
        "summary": "A permission object (a key to one file or folder) so a tool cannot invent a path. The host mints cap.fileRead / fileWrite / dirList; cap.confine narrows to a relative path. There is no flat cap() alias.",
        "also": [
            "13-built-in-functions.html#capability-tokens",
            "18-agent-orchestration.html#capability-tokens-for-tools"
        ]
    },
    {
        "id": "prompt",
        "term": "prompt",
        "aliases": [
            "prompts",
            "prompt block"
        ],
        "href": "10-prompts.html",
        "summary": "First-class prompt templates. Await runs the model; without await the call is a rendered template."
    },
    {
        "id": "schema",
        "term": "schema",
        "aliases": [
            "schemas",
            "structured output"
        ],
        "href": "10-prompts.html",
        "summary": "Named shape for prompt / LLM JSON. validate(schema, value) checks a payload against it."
    },
    {
        "id": "validate",
        "term": "validate",
        "aliases": [
            "validate()"
        ],
        "href": "13-built-in-functions.html#validate",
        "summary": "validate(schema, value) checks a dict against a schema name. Same check await would run on model JSON."
    },
    {
        "id": "await-prompt",
        "term": "await prompt",
        "aliases": [
            "await",
            "runPrompt"
        ],
        "href": "10-prompts.html",
        "summary": "Execute a prompt against an LLM. Without await, a prompt call only renders the template."
    },
    {
        "id": "closed-api",
        "term": "Closed APIs and runProgram",
        "aliases": [
            "api",
            "program",
            "runProgram",
            "Mode C"
        ],
        "href": "10-prompts.html",
        "summary": "Closed api / program(ApiName) values run deterministically with runProgram — no further LLM calls."
    },
    {
        "id": "modules",
        "term": "include, using, import, export",
        "aliases": [
            "include",
            "using",
            "import",
            "export",
            "modules"
        ],
        "href": "01-introduction.html#modules",
        "summary": "How MALDA composes source files: include, using, import, and export."
    },
    {
        "id": "keywords",
        "term": "Keywords",
        "aliases": [
            "reserved words",
            "reserved"
        ],
        "href": "03-lexical-structure.html#keywords",
        "summary": "Lexer reserved words. fn and def are not reserved; the parser rejects them as function keywords.",
        "also": [
            "36-appendix.html"
        ]
    },
    {
        "id": "const",
        "term": "const",
        "aliases": [
            "constant",
            "constants"
        ],
        "href": "05-variables.html#const",
        "summary": "Constant bindings. Constants are shallow — the binding cannot be reassigned, nested values still can."
    },
    {
        "id": "sum-types",
        "term": "Sum types",
        "aliases": [
            "type",
            "tagged union",
            "variant",
            "asVariant"
        ],
        "href": "04-data-types.html",
        "summary": "Tagged unions declared with type. Match on variants; asVariant(typeName, value) wraps a payload.",
        "also": [
            "13-built-in-functions.html#asVariant"
        ]
    },
    {
        "id": "dict",
        "term": "dict",
        "aliases": [
            "dictionary",
            "dictionaries"
        ],
        "href": "04-data-types.html",
        "summary": "Dictionary literals and methods. Also a keyword for the dict constructor."
    },
    {
        "id": "match",
        "term": "match",
        "aliases": [
            "case",
            "pattern matching"
        ],
        "href": "08-control-structures.html",
        "summary": "Pattern matching with match / case / default."
    },
    {
        "id": "defer",
        "term": "defer and using",
        "aliases": [
            "defer",
            "using",
            "cleanup"
        ],
        "href": "08-control-structures.html#cleanup",
        "summary": "Deterministic cleanup: defer runs at scope exit; using disposes a resource."
    },
    {
        "id": "lambda",
        "term": "Lambda expressions",
        "aliases": [
            "lambda",
            "=>",
            "arrow"
        ],
        "href": "09-functions.html#lambda-expressions",
        "summary": "Anonymous functions with => expression or block bodies, including closure capture."
    },
    {
        "id": "decorators",
        "term": "Decorators",
        "aliases": [
            "@",
            "@Tool",
            "@pure",
            "@effects",
            "@within",
            "@budget",
            "@GET",
            "@PAGE",
            "@AIPAGE"
        ],
        "href": "09-functions.html",
        "summary": "At-decorators on functions: @Tool, @pure, @effects, @within, @budget, and HTTP / page routes."
    },
    {
        "id": "effects-cap",
        "term": "@effects and @pure",
        "aliases": [
            "@effects",
            "@pure",
            "malda-pure",
            "malda-effects"
        ],
        "href": "09-functions.html",
        "summary": "@pure forbids I/O. @effects lists allowed side effects. @effects(\"cap\") marks handlers that mint or consume capability tokens.",
        "also": [
            "13-built-in-functions.html#capability-tokens"
        ]
    },
    {
        "id": "interpolation",
        "term": "String interpolation",
        "aliases": [
            "$\"",
            "interpolated string"
        ],
        "href": "07-expressions.html",
        "summary": "Interpolated strings with $\"...\". Type annotations elsewhere are IDE/LSP hints, not runtime checks."
    },
    {
        "id": "pipe",
        "term": "Pipe operator",
        "aliases": [
            "|>",
            "pipe"
        ],
        "href": "07-expressions.html",
        "summary": "Forward pipe |>. Binds looser than the ternary, so a ? b : c |> f pipes the whole conditional."
    },
    {
        "id": "null-conditional",
        "term": "Null-conditional access",
        "aliases": [
            "?.",
            "?[",
            "null-conditional"
        ],
        "href": "07-expressions.html#null-conditional",
        "summary": "Safe member / index access: a?.b and a?[i]."
    },
    {
        "id": "null-coalesce",
        "term": "Null-coalescing",
        "aliases": [
            "??",
            "null-coalesce"
        ],
        "href": "07-expressions.html#null-coalesce",
        "summary": "The ?? operator supplies a fallback when the left side is null."
    },
    {
        "id": "comprehensions",
        "term": "Comprehensions",
        "aliases": [
            "list comprehension",
            "dictionary comprehension"
        ],
        "href": "07-expressions.html#comprehensions",
        "summary": "List and dictionary comprehensions."
    },
    {
        "id": "async-await",
        "term": "async and await",
        "aliases": [
            "async",
            "await"
        ],
        "href": "07-expressions.html",
        "summary": "await is a unary prefix operator. await a.b() awaits the call; (await a).b() needs parentheses."
    },
    {
        "id": "io",
        "term": "io.*",
        "aliases": [
            "io",
            "io.print",
            "io.readFile",
            "print",
            "readFile",
            "writeFile"
        ],
        "href": "12-input-output.html",
        "summary": "Console, files, paths, environment, and host I/O. Prefer namespaced io.* calls; flat print still runs."
    },
    {
        "id": "stdlib-namespaces",
        "term": "Stdlib namespaces",
        "aliases": [
            "math",
            "str",
            "io",
            "flat alias"
        ],
        "href": "13-built-in-functions.html#stdlib-namespaces",
        "summary": "Prefer math / str / io namespaces. Flat aliases are deprecated — do not add new ones."
    },
    {
        "id": "glob-grep",
        "term": "glob and grep",
        "aliases": [
            "glob",
            "grep"
        ],
        "href": "13-built-in-functions.html",
        "summary": "File search helpers. Default glob cap is 200 results (hard cap 500); grep defaults to recursive search.",
        "also": [
            "12-input-output.html"
        ]
    },
    {
        "id": "result-option",
        "term": "result and option",
        "aliases": [
            "result",
            "option",
            "result.ok",
            "option.some"
        ],
        "href": "13-built-in-functions.html#result-option",
        "summary": "Explicit success / absence wrappers instead of null."
    },
    {
        "id": "grounded",
        "term": "grounded.wrap",
        "aliases": [
            "grounded",
            "citations"
        ],
        "href": "13-built-in-functions.html#grounded-values",
        "summary": "Wrap a value with retrieval citations. GraphMemory ASK can return grounded hits."
    },
    {
        "id": "evalPrompt",
        "term": "evalPrompt",
        "aliases": [
            "evalPrompt()"
        ],
        "href": "13-built-in-functions.html#evalPrompt",
        "summary": "Run a prompt against a fixture without calling a model — useful in tests."
    },
    {
        "id": "actor",
        "term": "Actors",
        "aliases": [
            "actor",
            "spawn",
            "send",
            "receive",
            "self"
        ],
        "href": "17-actors.html",
        "summary": "Native actor model: spawn, send, receive, self, and isolated state.",
        "also": [
            "17-actors.html#actor-state-isolation"
        ]
    },
    {
        "id": "agent",
        "term": "Agent",
        "aliases": [
            "agents",
            "think",
            "CodingAgent",
            "GitAgent",
            "DevAgent",
            "HumanAgent"
        ],
        "href": "18-agent-orchestration.html",
        "summary": "Built-in Agent class and specialized agents. think() runs a turn with tools."
    },
    {
        "id": "agent-team",
        "term": "Agent teams",
        "aliases": [
            "agents.team",
            "agents.define",
            "AgentTeam",
            "handoff",
            "team.handoff",
            "team.review",
            "team.reject",
            "team.consult",
            "team.run",
            "team.decompose"
        ],
        "href": "18-agent-orchestration.html#declarative-agent-teams",
        "summary": "Declarative multi-agent teams: agents.define / agents.team bind role specs to a directed graph. Typed hops stay validate-only unless think is true.",
        "also": [
            "13-built-in-functions.html#agent-teams",
            "14-graphs.html"
        ]
    },
    {
        "id": "tool",
        "term": "Tools",
        "aliases": [
            "@Tool",
            "Tool",
            "createReadFileTool",
            "createWebFetchTool"
        ],
        "href": "18-agent-orchestration.html",
        "summary": "Agent tools: @Tool decorator, new Tool, and built-in tool factories."
    },
    {
        "id": "llm-client",
        "term": "LLM clients",
        "aliases": [
            "LLMClient",
            "OpenRouterClient",
            "LlamaCppClient",
            "Conversation"
        ],
        "href": "18-agent-orchestration.html",
        "summary": "Built-in LLM clients and Conversation. A default local GGUF model is used when no client is passed."
    },
    {
        "id": "ralph",
        "term": "Ralph Wiggum",
        "aliases": [
            "ralph",
            "Ralph",
            "PRD loop"
        ],
        "href": "18-agent-orchestration.html#ralph-wiggum",
        "summary": "PRD-driven autonomous agent loop."
    },
    {
        "id": "graphmemory",
        "term": "GraphMemory",
        "aliases": [
            "GraphMemory",
            "semantic memory",
            "ask"
        ],
        "href": "19-graph-memory.html",
        "summary": "Semantic memory: knowledge graph plus vector search for agents."
    },
    {
        "id": "vectordb",
        "term": "VectorDB",
        "aliases": [
            "VectorDB",
            "searchSimilar",
            "embeddings"
        ],
        "href": "15-vectordb.html",
        "summary": "In-process vector database for similarity search and embeddings."
    },
    {
        "id": "graph",
        "term": "graph",
        "aliases": [
            "graphs",
            "directed",
            "undirected"
        ],
        "href": "14-graphs.html",
        "summary": "Graph literals, algorithms, and serialization."
    },
    {
        "id": "mcp",
        "term": "MCP Server",
        "aliases": [
            "MCP",
            "mcp"
        ],
        "href": "20-mcp-server.html",
        "summary": "Expose MALDA functions as Model Context Protocol tools."
    },
    {
        "id": "acp",
        "term": "ACP",
        "aliases": [
            "ACP",
            "Agent Communication Protocol"
        ],
        "href": "21-acp.html",
        "summary": "Agent Communication Protocol for multi-agent collaboration."
    },
    {
        "id": "workflow",
        "term": "Durable workflows",
        "aliases": [
            "workflow",
            "step",
            "approval",
            "compensate",
            "DLQ"
        ],
        "href": "22-durable-workflows.html",
        "summary": "Durable workflow syntax, storage, CLI, dead letters, and operations."
    },
    {
        "id": "web-ui",
        "term": "Web UI (ui.*)",
        "aliases": [
            "ui",
            "ui.button",
            "component",
            "property",
            "UIHost"
        ],
        "href": "24-web-ui.html",
        "summary": "Server-driven UI: component, property, and ui.* controls. Start at chapter 23 if choosing a UI model.",
        "also": [
            "23-web-ui-hub.html"
        ]
    },
    {
        "id": "page",
        "term": "@PAGE and @AIPAGE",
        "aliases": [
            "@PAGE",
            "@AIPAGE",
            "HttpServer"
        ],
        "href": "25-http-server-html-ui.html",
        "summary": "Route-first HTML pages on HttpServer, including LLM-generated @AIPAGE."
    },
    {
        "id": "rest",
        "term": "REST API",
        "aliases": [
            "@GET",
            "@POST",
            "@PUT",
            "@DELETE",
            "REST"
        ],
        "href": "27-rest-api.html",
        "summary": "Decorator-based REST routes on the MALDA HTTP server."
    },
    {
        "id": "game-three",
        "term": "game.* and three.*",
        "aliases": [
            "game",
            "three",
            "three.js",
            "@shader",
            "GLSL"
        ],
        "href": "26-browser-javascript-backend.html",
        "summary": "Browser games kit (JS backend): game.* , three.* scene API, and @shader() to GLSL.",
        "also": [
            "26-browser-javascript-backend.html#three-scene-api",
            "26-browser-javascript-backend.html#shader-kernels"
        ]
    },
    {
        "id": "fullstack",
        "term": "Full-stack",
        "aliases": [
            "@client",
            "@server",
            "fullstack"
        ],
        "href": "29-full-stack-development.html",
        "summary": "MALDA-native and hybrid full-stack apps: server UI or JS frontend plus MALDA backend."
    },
    {
        "id": "dotnet",
        "term": ".NET interop",
        "aliases": [
            "loadNativeModule",
            "createNativeCallback",
            "NuGet"
        ],
        "href": "30-dotnet-interop.html",
        "summary": "Load external .NET libraries and wrap MALDA functions as delegates."
    },
    {
        "id": "lsp",
        "term": "Language Server (LSP)",
        "aliases": [
            "LSP",
            "malda-lsp",
            "language server"
        ],
        "href": "02-tools.html#language-server",
        "summary": "Editor intelligence via malda-lsp. Interpret debug is malda debug-adapter, not the language server."
    },
    {
        "id": "debug",
        "term": "Interpret-mode debug",
        "aliases": [
            "debug-adapter",
            "DAP",
            "breakpoint"
        ],
        "href": "02-tools.html#interpret-mode-debug",
        "summary": "Source-level debug for the interpreter: malda debug-adapter (DAP). Do not mix DAP into malda-lsp."
    },
    {
        "id": "property-testing",
        "term": "Property testing",
        "aliases": [
            "@requires",
            "@targets",
            "runProperty",
            "property"
        ],
        "href": "34-property-testing.html",
        "summary": "Deterministic property tests, shrinking, and backend capability hints."
    },
    {
        "id": "skills",
        "term": "Skills",
        "aliases": [
            "skill",
            "malda skill"
        ],
        "href": "32-personal-assistant.html#skills",
        "summary": "Personal-assistant skills: reusable instruction packs for the CLI assistant."
    },
    {
        "id": "optional-packs",
        "term": "Optional packs",
        "aliases": [
            "optional pack",
            "vertical pack"
        ],
        "href": "36-appendix.html#optional-packs",
        "summary": "Domain packs stay out of OSS core. Load them with loadNativeModule; core does not auto-register pack globals.",
        "also": [
            "13-built-in-functions.html#optional-pack-builtins"
        ]
    },
    {
        "id": "repl",
        "term": "Interpreter and REPL",
        "aliases": [
            "REPL",
            "malda",
            "interpreter"
        ],
        "href": "02-tools.html",
        "summary": "Run .malda files or an interactive REPL. malda check diagnoses without executing."
    },
    {
        "id": "compile",
        "term": "Compiler / transpile",
        "aliases": [
            "compile",
            "transpile",
            "malda compile"
        ],
        "href": "02-tools.html",
        "summary": "malda compile produces a self-contained executable. Default runtime mode is Interpreter; use --mode transpile for typed C# publish."
    }
];

const FALLBACK_GLOSSARY_IT = [
    {
        "id": "capability-tokens",
        "term": "Token di capability",
        "aliases": [
            "cap",
            "cap.fileRead",
            "cap.fileWrite",
            "cap.dirList",
            "cap.confine",
            "cap.read",
            "cap.write",
            "cap.list",
            "cap.is",
            "capability",
            "capability token",
            "token di capability"
        ],
        "href": "12-input-output.html#capability-tokens",
        "summary": "Un oggetto-permesso (una chiave per un file o una cartella) così un tool non può inventare un path. L'host emette cap.fileRead / fileWrite / dirList; cap.confine restringe a un path relativo. Non esiste un alias piatto cap().",
        "also": [
            "13-built-in-functions.html#capability-tokens",
            "18-agent-orchestration.html#capability-tokens-for-tools"
        ]
    },
    {
        "id": "prompt",
        "term": "prompt",
        "aliases": [
            "prompts",
            "blocco prompt"
        ],
        "href": "10-prompts.html",
        "summary": "Template di prompt di prima classe. await esegue il modello; senza await la chiamata è solo il template reso."
    },
    {
        "id": "schema",
        "term": "schema",
        "aliases": [
            "schemas",
            "output strutturato"
        ],
        "href": "10-prompts.html",
        "summary": "Forma nominata per il JSON di prompt / LLM. validate(schema, value) controlla un payload rispetto allo schema."
    },
    {
        "id": "validate",
        "term": "validate",
        "aliases": [
            "validate()"
        ],
        "href": "13-built-in-functions.html#validate",
        "summary": "validate(schema, value) controlla un dict rispetto a un nome di schema. È lo stesso controllo che await eseguirebbe sul JSON del modello."
    },
    {
        "id": "await-prompt",
        "term": "await prompt",
        "aliases": [
            "await",
            "runPrompt"
        ],
        "href": "10-prompts.html",
        "summary": "Esegue un prompt su un LLM. Senza await, la chiamata al prompt rende solo il template."
    },
    {
        "id": "closed-api",
        "term": "API chiuse e runProgram",
        "aliases": [
            "api",
            "program",
            "runProgram",
            "Mode C"
        ],
        "href": "10-prompts.html",
        "summary": "I valori api / program(ApiName) chiusi girano in modo deterministico con runProgram — nessuna ulteriore chiamata LLM."
    },
    {
        "id": "modules",
        "term": "include, using, import, export",
        "aliases": [
            "include",
            "using",
            "import",
            "export",
            "moduli"
        ],
        "href": "01-introduction.html#modules",
        "summary": "Come MALDA compone i file sorgente: include, using, import ed export."
    },
    {
        "id": "keywords",
        "term": "Keyword",
        "aliases": [
            "parole riservate",
            "reserved words",
            "reserved"
        ],
        "href": "03-lexical-structure.html#keywords",
        "summary": "Parole riservate del lexer. fn e def non sono riservate; il parser le rifiuta come keyword di funzione.",
        "also": [
            "36-appendix.html"
        ]
    },
    {
        "id": "const",
        "term": "const",
        "aliases": [
            "costante",
            "costanti",
            "constant"
        ],
        "href": "05-variables.html#const",
        "summary": "Binding costanti. Le costanti sono shallow: il binding non si riassegna, i valori nidificati sì."
    },
    {
        "id": "sum-types",
        "term": "Tipi somma",
        "aliases": [
            "type",
            "tagged union",
            "variant",
            "asVariant",
            "unione taggata"
        ],
        "href": "04-data-types.html",
        "summary": "Unioni taggate dichiarate con type. Si fa match sulle varianti; asVariant(typeName, value) avvolge un payload.",
        "also": [
            "13-built-in-functions.html#asVariant"
        ]
    },
    {
        "id": "dict",
        "term": "dict",
        "aliases": [
            "dizionario",
            "dizionari",
            "dictionary"
        ],
        "href": "04-data-types.html",
        "summary": "Letterali e metodi dei dizionari. È anche una keyword per il costruttore dict."
    },
    {
        "id": "match",
        "term": "match",
        "aliases": [
            "case",
            "pattern matching"
        ],
        "href": "08-control-structures.html",
        "summary": "Pattern matching con match / case / default."
    },
    {
        "id": "defer",
        "term": "defer e using",
        "aliases": [
            "defer",
            "using",
            "cleanup"
        ],
        "href": "08-control-structures.html#cleanup",
        "summary": "Cleanup deterministico: defer gira all'uscita dello scope; using rilascia una risorsa."
    },
    {
        "id": "lambda",
        "term": "Espressioni lambda",
        "aliases": [
            "lambda",
            "=>",
            "arrow"
        ],
        "href": "09-functions.html#lambda-expressions",
        "summary": "Funzioni anonime con corpo espressione o blocco =>, inclusa la cattura delle closure."
    },
    {
        "id": "decorators",
        "term": "Decoratori",
        "aliases": [
            "@",
            "@Tool",
            "@pure",
            "@effects",
            "@within",
            "@budget",
            "@GET",
            "@PAGE",
            "@AIPAGE"
        ],
        "href": "09-functions.html",
        "summary": "Decoratori at- sulle funzioni: @Tool, @pure, @effects, @within, @budget e le route HTTP / page."
    },
    {
        "id": "effects-cap",
        "term": "@effects e @pure",
        "aliases": [
            "@effects",
            "@pure",
            "malda-pure",
            "malda-effects"
        ],
        "href": "09-functions.html",
        "summary": "@pure vieta l'I/O. @effects elenca i side effect ammessi. @effects(\"cap\") marca gli handler che emettono o consumano token di capability.",
        "also": [
            "13-built-in-functions.html#capability-tokens"
        ]
    },
    {
        "id": "interpolation",
        "term": "Interpolazione di stringhe",
        "aliases": [
            "$\"",
            "stringa interpolata"
        ],
        "href": "07-expressions.html",
        "summary": "Stringhe interpolate con $\"...\" . Le annotazioni di tipo altrove sono hint IDE/LSP, non controlli a runtime."
    },
    {
        "id": "pipe",
        "term": "Operatore pipe",
        "aliases": [
            "|>",
            "pipe"
        ],
        "href": "07-expressions.html",
        "summary": "Pipe in avanti |>. Lega più debole del ternario, quindi a ? b : c |> f passa all'f l'intero condizionale."
    },
    {
        "id": "null-conditional",
        "term": "Accesso null-conditional",
        "aliases": [
            "?.",
            "?[",
            "null-conditional"
        ],
        "href": "07-expressions.html#null-conditional",
        "summary": "Accesso sicuro a membri / indici: a?.b e a?[i]."
    },
    {
        "id": "null-coalesce",
        "term": "Null-coalescing",
        "aliases": [
            "??",
            "null-coalesce"
        ],
        "href": "07-expressions.html#null-coalesce",
        "summary": "L'operatore ?? fornisce un fallback quando il lato sinistro è null."
    },
    {
        "id": "comprehensions",
        "term": "Comprehension",
        "aliases": [
            "list comprehension",
            "dictionary comprehension"
        ],
        "href": "07-expressions.html#comprehensions",
        "summary": "Comprehension di liste e dizionari."
    },
    {
        "id": "async-await",
        "term": "async e await",
        "aliases": [
            "async",
            "await"
        ],
        "href": "07-expressions.html",
        "summary": "await è un operatore prefisso unario. await a.b() attende la chiamata; (await a).b() richiede le parentesi."
    },
    {
        "id": "io",
        "term": "io.*",
        "aliases": [
            "io",
            "io.print",
            "io.readFile",
            "print",
            "readFile",
            "writeFile"
        ],
        "href": "12-input-output.html",
        "summary": "Console, file, path, ambiente e I/O dell'host. Preferisci le chiamate namespaced io.*; print piatto gira ancora."
    },
    {
        "id": "stdlib-namespaces",
        "term": "Namespace della stdlib",
        "aliases": [
            "math",
            "str",
            "io",
            "alias piatti"
        ],
        "href": "13-built-in-functions.html#stdlib-namespaces",
        "summary": "Preferisci i namespace math / str / io. Gli alias piatti sono deprecati — non aggiungerne di nuovi."
    },
    {
        "id": "glob-grep",
        "term": "glob e grep",
        "aliases": [
            "glob",
            "grep"
        ],
        "href": "13-built-in-functions.html",
        "summary": "Helper di ricerca su file. Il cap di default di glob è 200 risultati (cap rigido 500); grep di default è ricorsivo.",
        "also": [
            "12-input-output.html"
        ]
    },
    {
        "id": "result-option",
        "term": "result e option",
        "aliases": [
            "result",
            "option",
            "result.ok",
            "option.some"
        ],
        "href": "13-built-in-functions.html#result-option",
        "summary": "Wrapper espliciti di successo / assenza al posto di null."
    },
    {
        "id": "grounded",
        "term": "grounded.wrap",
        "aliases": [
            "grounded",
            "citations",
            "citazioni"
        ],
        "href": "13-built-in-functions.html#grounded-values",
        "summary": "Avvolge un valore con citazioni di retrieval. GraphMemory ASK può restituire hit grounded."
    },
    {
        "id": "evalPrompt",
        "term": "evalPrompt",
        "aliases": [
            "evalPrompt()"
        ],
        "href": "13-built-in-functions.html#evalPrompt",
        "summary": "Esegue un prompt su un fixture senza chiamare un modello — utile nei test."
    },
    {
        "id": "actor",
        "term": "Actor",
        "aliases": [
            "actor",
            "spawn",
            "send",
            "receive",
            "self"
        ],
        "href": "17-actors.html",
        "summary": "Modello ad actor nativo: spawn, send, receive, self e stato isolato.",
        "also": [
            "17-actors.html#actor-state-isolation"
        ]
    },
    {
        "id": "agent",
        "term": "Agent",
        "aliases": [
            "agents",
            "agenti",
            "think",
            "CodingAgent",
            "GitAgent",
            "DevAgent",
            "HumanAgent"
        ],
        "href": "18-agent-orchestration.html",
        "summary": "Classe Agent built-in e agenti specializzati. think() esegue un turno con i tool."
    },
    {
        "id": "agent-team",
        "term": "Team di agenti",
        "aliases": [
            "agents.team",
            "agents.define",
            "AgentTeam",
            "handoff",
            "team.handoff",
            "team.review",
            "team.reject",
            "team.consult",
            "team.run",
            "team.decompose",
            "team dichiarativi"
        ],
        "href": "18-agent-orchestration.html#declarative-agent-teams",
        "summary": "Team multi-agente dichiarativi: agents.define / agents.team collegano spec di ruolo a un grafo diretto. Gli hop tipizzati restano solo validazione se think non è true.",
        "also": [
            "13-built-in-functions.html#agent-teams",
            "14-graphs.html"
        ]
    },
    {
        "id": "tool",
        "term": "Tool",
        "aliases": [
            "@Tool",
            "Tool",
            "createReadFileTool",
            "createWebFetchTool"
        ],
        "href": "18-agent-orchestration.html",
        "summary": "Tool degli agenti: decoratore @Tool, new Tool e factory built-in."
    },
    {
        "id": "llm-client",
        "term": "Client LLM",
        "aliases": [
            "LLMClient",
            "OpenRouterClient",
            "LlamaCppClient",
            "Conversation"
        ],
        "href": "18-agent-orchestration.html",
        "summary": "Client LLM built-in e Conversation. Se non passi un client viene usato un modello GGUF locale di default."
    },
    {
        "id": "ralph",
        "term": "Ralph Wiggum",
        "aliases": [
            "ralph",
            "Ralph",
            "PRD loop"
        ],
        "href": "18-agent-orchestration.html#ralph-wiggum",
        "summary": "Loop autonomo di agenti guidato da un PRD."
    },
    {
        "id": "graphmemory",
        "term": "GraphMemory",
        "aliases": [
            "GraphMemory",
            "memoria semantica",
            "ask"
        ],
        "href": "19-graph-memory.html",
        "summary": "Memoria semantica: grafo di conoscenza più ricerca vettoriale per gli agenti."
    },
    {
        "id": "vectordb",
        "term": "VectorDB",
        "aliases": [
            "VectorDB",
            "searchSimilar",
            "embeddings"
        ],
        "href": "15-vectordb.html",
        "summary": "Database vettoriale in-process per similarity search e embedding."
    },
    {
        "id": "graph",
        "term": "graph",
        "aliases": [
            "grafi",
            "graphs",
            "directed",
            "undirected"
        ],
        "href": "14-graphs.html",
        "summary": "Letterali a grafo, algoritmi e serializzazione."
    },
    {
        "id": "mcp",
        "term": "Server MCP",
        "aliases": [
            "MCP",
            "mcp"
        ],
        "href": "20-mcp-server.html",
        "summary": "Espone funzioni MALDA come tool del Model Context Protocol."
    },
    {
        "id": "acp",
        "term": "ACP",
        "aliases": [
            "ACP",
            "Agent Communication Protocol"
        ],
        "href": "21-acp.html",
        "summary": "Agent Communication Protocol per la collaborazione multi-agente."
    },
    {
        "id": "workflow",
        "term": "Workflow durevoli",
        "aliases": [
            "workflow",
            "step",
            "approval",
            "compensate",
            "DLQ"
        ],
        "href": "22-durable-workflows.html",
        "summary": "Sintassi dei workflow durevoli, storage, CLI, dead letter e operazioni."
    },
    {
        "id": "web-ui",
        "term": "Web UI (ui.*)",
        "aliases": [
            "ui",
            "ui.button",
            "component",
            "property",
            "UIHost"
        ],
        "href": "24-web-ui.html",
        "summary": "UI guidata dal server: component, property e controlli ui.*. Parti dal capitolo 23 se stai scegliendo un modello UI.",
        "also": [
            "23-web-ui-hub.html"
        ]
    },
    {
        "id": "page",
        "term": "@PAGE e @AIPAGE",
        "aliases": [
            "@PAGE",
            "@AIPAGE",
            "HttpServer"
        ],
        "href": "25-http-server-html-ui.html",
        "summary": "Pagine HTML route-first su HttpServer, incluso @AIPAGE generato da LLM."
    },
    {
        "id": "rest",
        "term": "REST API",
        "aliases": [
            "@GET",
            "@POST",
            "@PUT",
            "@DELETE",
            "REST"
        ],
        "href": "27-rest-api.html",
        "summary": "Route REST basate su decoratori sul server HTTP MALDA."
    },
    {
        "id": "game-three",
        "term": "game.* e three.*",
        "aliases": [
            "game",
            "three",
            "three.js",
            "@shader",
            "GLSL"
        ],
        "href": "26-browser-javascript-backend.html",
        "summary": "Kit giochi nel browser (backend JS): game.*, API scene three.* e @shader() verso GLSL.",
        "also": [
            "26-browser-javascript-backend.html#three-scene-api",
            "26-browser-javascript-backend.html#shader-kernels"
        ]
    },
    {
        "id": "fullstack",
        "term": "Full-stack",
        "aliases": [
            "@client",
            "@server",
            "fullstack"
        ],
        "href": "29-full-stack-development.html",
        "summary": "App full-stack native MALDA e ibride: UI server o frontend JS più backend MALDA."
    },
    {
        "id": "dotnet",
        "term": "Interop .NET",
        "aliases": [
            "loadNativeModule",
            "createNativeCallback",
            "NuGet"
        ],
        "href": "30-dotnet-interop.html",
        "summary": "Carica librerie .NET esterne e avvolge funzioni MALDA come delegate."
    },
    {
        "id": "lsp",
        "term": "Language Server (LSP)",
        "aliases": [
            "LSP",
            "malda-lsp",
            "language server"
        ],
        "href": "02-tools.html#language-server",
        "summary": "Intelligenza dell'editor via malda-lsp. Il debug in interpret è malda debug-adapter, non il language server."
    },
    {
        "id": "debug",
        "term": "Debug in modalità interpret",
        "aliases": [
            "debug-adapter",
            "DAP",
            "breakpoint"
        ],
        "href": "02-tools.html#interpret-mode-debug",
        "summary": "Debug a livello sorgente per l'interprete: malda debug-adapter (DAP). Non mescolare il DAP in malda-lsp."
    },
    {
        "id": "property-testing",
        "term": "Property testing",
        "aliases": [
            "@requires",
            "@targets",
            "runProperty",
            "property"
        ],
        "href": "34-property-testing.html",
        "summary": "Property test deterministici, shrinking e hint di capability del backend."
    },
    {
        "id": "skills",
        "term": "Skill",
        "aliases": [
            "skill",
            "malda skill"
        ],
        "href": "32-personal-assistant.html#skills",
        "summary": "Skill dell'assistente personale: pacchetti di istruzioni riutilizzabili per l'assistente CLI."
    },
    {
        "id": "optional-packs",
        "term": "Pack opzionali",
        "aliases": [
            "optional pack",
            "vertical pack",
            "pack opzionali"
        ],
        "href": "36-appendix.html#optional-packs",
        "summary": "I pack di dominio restano fuori dal core OSS. Caricali con loadNativeModule; il core non registra in automatico i global dei pack.",
        "also": [
            "13-built-in-functions.html#optional-pack-builtins"
        ]
    },
    {
        "id": "repl",
        "term": "Interprete e REPL",
        "aliases": [
            "REPL",
            "malda",
            "interprete",
            "interpreter"
        ],
        "href": "02-tools.html",
        "summary": "Esegui file .malda o un REPL interattivo. malda check diagnostica senza eseguire."
    },
    {
        "id": "compile",
        "term": "Compilatore / transpile",
        "aliases": [
            "compile",
            "transpile",
            "malda compile"
        ],
        "href": "02-tools.html",
        "summary": "malda compile produce un eseguibile self-contained. Il modo runtime di default è Interpreter; usa --mode transpile per la publish C# tipizzata."
    }
];

function renderCollapsibleNav(nav, items) {
    const homeItem = items.find(function(item) { return item.href === 'index.html'; });
    const chapterItems = items.filter(function(item) { return item.href !== 'index.html'; });
    const strings = manualStrings();

    let navHTML = '<div class="nav-search" role="search">';
    navHTML += '<label class="visually-hidden" for="manual-search-input">' + escapeHtml(strings.searchLabel) + '</label>';
    navHTML += '<input id="manual-search-input" class="nav-search-input" type="search" autocomplete="off" spellcheck="false" placeholder="' + escapeAttr(strings.searchPlaceholder) + '" aria-label="' + escapeAttr(strings.searchLabel) + '" aria-controls="manual-search-results" aria-expanded="false">';
    navHTML += '<p class="nav-search-hint">' + escapeHtml(strings.searchHint) + '</p>';
    navHTML += '<div id="manual-search-results" class="nav-search-results" hidden></div>';
    navHTML += '</div>';

    navHTML += '<ul class="nav-root">';

    if (homeItem) {
        navHTML += '<li class="nav-home"><a href="index.html">' + homeItem.text + '</a></li>';
    }

    navHTML += '<li class="nav-utility"><a href="glossary.html">' + escapeHtml(strings.browseGlossary) + '</a></li>';

    NAV_CATEGORY_ORDER.forEach(function(category) {
        const categoryItems = chapterItems
            .filter(function(item) { return item.category === category; })
            .sort(function(a, b) {
                const numA = parseInt((a.text.match(/^(\d+)\./) || [0, 0])[1], 10);
                const numB = parseInt((b.text.match(/^(\d+)\./) || [0, 0])[1], 10);
                return numA - numB;
            });

        if (categoryItems.length === 0) {
            return;
        }

        const categoryId = 'nav-cat-' + category.toLowerCase().replace(/[^a-z0-9]+/g, '-');
        navHTML += '<li class="nav-category" data-category="' + escapeAttr(category) + '">';
        navHTML += '<button type="button" class="nav-category-toggle" aria-expanded="false" aria-controls="' + categoryId + '">';
        navHTML += '<span class="nav-category-label">' + escapeHtml(categoryLabel(category)) + '</span>';
        navHTML += '<span class="nav-category-icon" aria-hidden="true"></span>';
        navHTML += '</button>';
        navHTML += '<ul class="nav-category-items" id="' + categoryId + '">';

        categoryItems.forEach(function(item) {
            navHTML += '<li><a href="' + escapeAttr(item.href) + '">' + escapeHtml(item.text) + '</a></li>';
        });

        navHTML += '</ul></li>';
    });

    navHTML += '</ul>';
    nav.innerHTML = navHTML;
}

function initCollapsibleNav() {
    const currentPage = window.location.pathname.split('/').pop() || 'index.html';
    const categories = document.querySelectorAll('.nav-category');

    categories.forEach(function(categoryEl) {
        const toggle = categoryEl.querySelector('.nav-category-toggle');
        const items = categoryEl.querySelector('.nav-category-items');
        if (!toggle || !items) {
            return;
        }

        const hasActive = items.querySelector('a[href="' + currentPage + '"]') !== null;
        setCategoryOpen(categoryEl, toggle, hasActive);

        toggle.addEventListener('click', function() {
            const isOpen = categoryEl.classList.contains('nav-category-open');
            setCategoryOpen(categoryEl, toggle, !isOpen);
        });
    });
}

function setCategoryOpen(categoryEl, toggle, open) {
    categoryEl.classList.toggle('nav-category-open', open);
    toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
}

function initNavigation() {
    const currentPage = window.location.pathname.split('/').pop() || 'index.html';
    const navLinks = document.querySelectorAll('nav a');

    navLinks.forEach(function(link) {
        const linkPage = link.getAttribute('href');
        if (linkPage === currentPage || (currentPage === '' && linkPage === 'index.html')) {
            link.classList.add('active');
        }
    });
}

function escapeHtml(text) {
    return String(text)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

function escapeAttr(text) {
    return escapeHtml(text).replace(/"/g, '&quot;');
}

function syncHeaderOffset() {
    const header = document.querySelector('header');
    if (!header) {
        return;
    }
    document.documentElement.style.setProperty('--header-offset', header.offsetHeight + 'px');
}

function initHeaderActions() {
    const header = document.querySelector('header');
    if (!header) {
        return;
    }

    if (!header.querySelector('.header-text')) {
        const title = header.querySelector('h1');
        const subtitle = header.querySelector('p');
        if (title) {
            const cluster = document.createElement('div');
            cluster.className = 'header-text';
            title.parentNode.insertBefore(cluster, title);
            cluster.appendChild(title);
            if (subtitle) {
                cluster.appendChild(subtitle);
            }
        }
    }

    if (!header.querySelector('.nav-toggle')) {
        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'nav-toggle';
        const strings = manualStrings();
        toggle.setAttribute('aria-label', strings.openContents);
        toggle.setAttribute('aria-expanded', 'false');
        toggle.setAttribute('aria-controls', 'manual-nav');
        toggle.innerHTML = '<span class="nav-toggle-bars" aria-hidden="true"></span>';
        toggle.addEventListener('click', function() {
            toggleMobileMenu();
        });
        header.insertBefore(toggle, header.firstChild);
    }

    if (!header.querySelector('.manual-actions')) {
        const actions = document.createElement('div');
        actions.className = 'manual-actions';

        const strings = manualStrings();

        const langSwitch = document.createElement('a');
        langSwitch.className = 'manual-action lang-switch';
        langSwitch.href = peerLocaleHref();
        langSwitch.title = strings.langSwitchTitle;
        langSwitch.setAttribute('aria-label', strings.langSwitchTitle);
        langSwitch.setAttribute('hreflang', isItalianManual() ? 'en' : 'it');
        langSwitch.setAttribute('lang', isItalianManual() ? 'en' : 'it');
        langSwitch.innerHTML = '<span class="action-label-full">' + escapeHtml(strings.langSwitch) + '</span>' +
            '<span class="action-label-short" aria-hidden="true">' + escapeHtml(strings.langSwitchShort) + '</span>';

        const printButton = document.createElement('button');
        printButton.type = 'button';
        printButton.className = 'manual-action';
        printButton.title = strings.printTitle;
        printButton.setAttribute('aria-label', strings.print);
        printButton.innerHTML = '<span class="action-label-full">' + escapeHtml(strings.print) + '</span>' +
            '<span class="action-label-short" aria-hidden="true">' + escapeHtml(strings.printShort) + '</span>';
        printButton.addEventListener('click', function() {
            window.print();
        });

        const searchButton = document.createElement('button');
        searchButton.type = 'button';
        searchButton.className = 'manual-action header-search';
        searchButton.title = strings.headerSearchTitle;
        searchButton.setAttribute('aria-label', strings.headerSearch);
        searchButton.innerHTML = '<span class="action-label-full">' + escapeHtml(strings.headerSearch) + '</span>' +
            '<span class="action-label-short" aria-hidden="true">' + escapeHtml(strings.headerSearchShort) + '</span>';
        searchButton.addEventListener('click', function() {
            focusManualSearch({ openDrawer: true });
        });

        actions.appendChild(langSwitch);
        actions.appendChild(searchButton);
        actions.appendChild(printButton);
        header.appendChild(actions);
    }
}

function initNavDrawer() {
    const nav = document.querySelector('nav');
    if (!nav) {
        return;
    }

    nav.id = nav.id || 'manual-nav';

    if (!document.querySelector('.nav-backdrop')) {
        const backdrop = document.createElement('div');
        backdrop.className = 'nav-backdrop';
        backdrop.setAttribute('hidden', '');
        backdrop.addEventListener('click', function() {
            setNavDrawerOpen(false);
        });
        document.body.appendChild(backdrop);
    }

    nav.querySelectorAll('a').forEach(function(link) {
        link.addEventListener('click', function() {
            setNavDrawerOpen(false);
        });
    });

    document.addEventListener('keydown', function(event) {
        if (event.key === 'Escape') {
            setNavDrawerOpen(false);
        }
    });
}

function setNavDrawerOpen(open) {
    const nav = document.querySelector('nav');
    const toggle = document.querySelector('.nav-toggle');
    const backdrop = document.querySelector('.nav-backdrop');
    if (!nav) {
        return;
    }

    document.body.classList.toggle('nav-drawer-open', open);
    nav.classList.toggle('mobile-open', open);

    if (toggle) {
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        const strings = manualStrings();
        toggle.setAttribute('aria-label', open ? strings.closeContents : strings.openContents);
    }

    if (backdrop) {
        if (open) {
            backdrop.removeAttribute('hidden');
        } else {
            backdrop.setAttribute('hidden', '');
        }
    }
}

// The highlighter wraps each source line in a block-level <span class="ln">,
// which strips the newline characters from textContent. Rebuild them here so
// copied snippets stay runnable.
function codeBlockText(block) {
    const lines = block.querySelectorAll('.ln');
    if (lines.length === 0) {
        return block.textContent;
    }
    return Array.prototype.map.call(lines, function(line) {
        return line.textContent;
    }).join('\n');
}

function initCodeCopy() {
    const codeBlocks = document.querySelectorAll('pre code');

    codeBlocks.forEach(function(block) {
        const pre = block.parentElement;
        if (!pre.querySelector('.copy-btn')) {
            const button = document.createElement('button');
            const strings = manualStrings();
            button.className = 'copy-btn';
            button.textContent = strings.copy;
            button.setAttribute('aria-label', strings.copyAria);

            button.addEventListener('click', function() {
                copyToClipboard(codeBlockText(block));
                button.textContent = strings.copied;
                setTimeout(function() {
                    button.textContent = strings.copy;
                }, 2000);
            });

            pre.style.position = 'relative';
            pre.appendChild(button);
        }
    });
}

function copyToClipboard(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).catch(function(err) {
            console.error('Failed to copy:', err);
            fallbackCopy(text);
        });
    } else {
        fallbackCopy(text);
    }
}

function fallbackCopy(text) {
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    try {
        document.execCommand('copy');
    } catch (err) {
        console.error('Fallback copy failed:', err);
    }
    document.body.removeChild(textarea);
}

function initSmoothScroll() {
    const links = document.querySelectorAll('a[href^="#"]');

    links.forEach(function(link) {
        link.addEventListener('click', function(e) {
            const href = this.getAttribute('href');
            if (href !== '#') {
                const target = document.querySelector(href);
                if (target) {
                    e.preventDefault();
                    target.scrollIntoView({
                        behavior: 'smooth',
                        block: 'start'
                    });
                }
            }
        });
    });
}

function highlightActiveSection() {
    const sections = document.querySelectorAll('h2[id], h3[id]');
    const navLinks = document.querySelectorAll('nav a[href^="#"]');

    if (sections.length === 0 || navLinks.length === 0) return;

    const observerOptions = {
        root: null,
        rootMargin: '-20% 0px -70% 0px',
        threshold: 0
    };

    const observer = new IntersectionObserver(function(entries) {
        entries.forEach(function(entry) {
            if (entry.isIntersecting) {
                const id = entry.target.getAttribute('id');
                navLinks.forEach(function(link) {
                    link.classList.remove('active');
                    if (link.getAttribute('href') === '#' + id) {
                        link.classList.add('active');
                    }
                });
            }
        });
    }, observerOptions);

    sections.forEach(function(section) {
        observer.observe(section);
    });
}

function toggleMobileMenu() {
    setNavDrawerOpen(!document.body.classList.contains('nav-drawer-open'));
}

function getFallbackGlossary() {
    return isItalianManual() ? FALLBACK_GLOSSARY_IT : FALLBACK_GLOSSARY_EN;
}

async function fetchJsonFile(name) {
    try {
        const response = await fetch(name);
        if (response.ok) {
            return await response.json();
        }
    } catch (err) {
        // file:// and other offline contexts fall back to embedded data
    }
    return null;
}

async function loadManualSearchData() {
    const glossaryData = await fetchJsonFile('glossary.json');
    const headingData = await fetchJsonFile('headings.json');
    const glossary = glossaryData && Array.isArray(glossaryData.terms)
        ? glossaryData.terms
        : getFallbackGlossary();
    const headings = headingData && Array.isArray(headingData) ? headingData : [];
    const chapters = getFallbackNavItems().filter(function(item) {
        return item.href !== 'index.html';
    });
    return { glossary: glossary, headings: headings, chapters: chapters };
}

function normalizeSearchText(value) {
    return String(value || '')
        .toLowerCase()
        .replace(/[“”"']/g, '')
        .replace(/[_()[\]{},:;]+/g, ' ')
        .replace(/\s+/g, ' ')
        .trim();
}

function scoreSearchHaystack(query, texts) {
    const q = normalizeSearchText(query);
    if (!q) {
        return 0;
    }

    let best = 0;
    texts.forEach(function(raw) {
        const hay = normalizeSearchText(raw);
        if (!hay) {
            return;
        }
        if (hay === q) {
            best = Math.max(best, 100);
            return;
        }
        if (hay.startsWith(q)) {
            best = Math.max(best, 86);
        }
        const parts = hay.split(/[\s./-]+/).filter(Boolean);
        if (parts.some(function(part) { return part === q; })) {
            best = Math.max(best, 80);
        } else if (parts.some(function(part) { return part.startsWith(q); })) {
            best = Math.max(best, 72);
        }
        if (hay.indexOf(q) !== -1) {
            best = Math.max(best, 48);
        }
    });
    return best;
}

function collectSearchResults(query, data) {
    const results = [];
    const seen = {};

    function addResult(item) {
        if (!item || !item.href || seen[item.kind + '::' + item.href]) {
            return;
        }
        if (item.score < 40) {
            return;
        }
        seen[item.kind + '::' + item.href] = true;
        results.push(item);
    }

    (data.glossary || []).forEach(function(term) {
        const texts = [term.term, term.id, term.summary].concat(term.aliases || []);
        addResult({
            kind: 'glossary',
            href: term.href,
            title: term.term,
            summary: term.summary || '',
            score: scoreSearchHaystack(query, texts) + 8
        });
    });

    (data.chapters || []).forEach(function(chapter) {
        addResult({
            kind: 'chapter',
            href: chapter.href,
            title: chapter.text,
            summary: chapter.category ? categoryLabel(chapter.category) : '',
            score: scoreSearchHaystack(query, [chapter.text, chapter.category || ''])
        });
    });

    (data.headings || []).forEach(function(heading) {
        const href = heading.id ? (heading.file + '#' + heading.id) : heading.file;
        addResult({
            kind: 'heading',
            href: href,
            title: heading.title,
            summary: heading.file.replace(/\.html$/, ''),
            score: scoreSearchHaystack(query, [heading.title, heading.id || ''])
        });
    });

    results.sort(function(a, b) {
        if (b.score !== a.score) {
            return b.score - a.score;
        }
        if (a.kind === 'glossary' && b.kind !== 'glossary') {
            return -1;
        }
        if (b.kind === 'glossary' && a.kind !== 'glossary') {
            return 1;
        }
        return a.title.localeCompare(b.title);
    });

    return results.slice(0, 20);
}

function highlightSearchMatch(text, query) {
    const source = String(text || '');
    const q = String(query || '').trim();
    if (!q) {
        return escapeHtml(source);
    }
    const lower = source.toLowerCase();
    const needle = q.toLowerCase();
    const index = lower.indexOf(needle);
    if (index < 0) {
        return escapeHtml(source);
    }
    return escapeHtml(source.slice(0, index)) +
        '<mark>' + escapeHtml(source.slice(index, index + q.length)) + '</mark>' +
        escapeHtml(source.slice(index + q.length));
}

function kindLabel(kind, strings) {
    if (kind === 'glossary') {
        return strings.searchGlossary;
    }
    if (kind === 'heading') {
        return strings.searchHeading;
    }
    return strings.searchChapter;
}

function renderSearchResults(container, results, query, strings) {
    if (!container) {
        return;
    }
    if (!query) {
        container.innerHTML = '';
        container.setAttribute('hidden', '');
        return;
    }

    container.removeAttribute('hidden');
    if (results.length === 0) {
        container.innerHTML = '<p class="nav-search-empty">' + escapeHtml(strings.searchNoResults) + '</p>';
        return;
    }

    let html = '<ul class="nav-search-list">';
    results.forEach(function(item, index) {
        html += '<li>';
        html += '<a href="' + escapeAttr(item.href) + '" data-search-index="' + index + '">';
        html += '<span class="nav-search-kind">' + escapeHtml(kindLabel(item.kind, strings)) + '</span>';
        html += '<span class="nav-search-title">' + highlightSearchMatch(item.title, query) + '</span>';
        if (item.summary) {
            html += '<span class="nav-search-summary">' + escapeHtml(item.summary) + '</span>';
        }
        html += '</a></li>';
    });
    html += '</ul>';
    container.innerHTML = html;
}

function focusManualSearch(options) {
    const openDrawer = options && options.openDrawer;
    if (openDrawer) {
        setNavDrawerOpen(true);
    }
    const input = document.getElementById('manual-search-input');
    if (!input) {
        return;
    }
    input.focus();
    input.select();
}

function isEditableTarget(target) {
    if (!target) {
        return false;
    }
    const tag = (target.tagName || '').toLowerCase();
    return tag === 'input' || tag === 'textarea' || tag === 'select' || target.isContentEditable;
}

function initManualSearch() {
    const nav = document.querySelector('nav');
    const input = document.getElementById('manual-search-input');
    const resultsEl = document.getElementById('manual-search-results');
    if (!nav || !input || !resultsEl) {
        return;
    }

    const strings = manualStrings();
    let searchData = {
        glossary: getFallbackGlossary(),
        headings: [],
        chapters: getFallbackNavItems().filter(function(item) { return item.href !== 'index.html'; })
    };
    let selectedIndex = -1;

    function currentLinks() {
        return resultsEl.querySelectorAll('.nav-search-list a');
    }

    function setSelected(index) {
        const links = currentLinks();
        selectedIndex = index;
        links.forEach(function(link, i) {
            link.classList.toggle('is-active', i === index);
        });
        input.setAttribute('aria-activedescendant', selectedIndex >= 0 && links[selectedIndex]
            ? 'search-hit-' + selectedIndex
            : '');
    }

    function paint() {
        const query = input.value.trim();
        nav.classList.toggle('nav-searching', query.length > 0);
        input.setAttribute('aria-expanded', query.length > 0 ? 'true' : 'false');
        const hits = query ? collectSearchResults(query, searchData) : [];
        renderSearchResults(resultsEl, hits, query, strings);
        resultsEl.querySelectorAll('.nav-search-list a').forEach(function(link, i) {
            link.id = 'search-hit-' + i;
            link.addEventListener('click', function() {
                setNavDrawerOpen(false);
            });
        });
        setSelected(hits.length ? 0 : -1);
    }

    input.addEventListener('input', paint);
    input.addEventListener('keydown', function(event) {
        const links = currentLinks();
        if (event.key === 'ArrowDown' && links.length) {
            event.preventDefault();
            setSelected(selectedIndex < links.length - 1 ? selectedIndex + 1 : 0);
        } else if (event.key === 'ArrowUp' && links.length) {
            event.preventDefault();
            setSelected(selectedIndex > 0 ? selectedIndex - 1 : links.length - 1);
        } else if (event.key === 'Enter' && selectedIndex >= 0 && links[selectedIndex]) {
            event.preventDefault();
            window.location.href = links[selectedIndex].getAttribute('href');
        } else if (event.key === 'Escape') {
            if (input.value) {
                input.value = '';
                paint();
                event.stopPropagation();
            }
        }
    });

    document.addEventListener('keydown', function(event) {
        if (event.defaultPrevented || event.altKey || event.metaKey && event.key !== 'k') {
            return;
        }
        const slash = event.key === '/';
        const chord = (event.ctrlKey || event.metaKey) && (event.key === 'k' || event.key === 'K');
        if (!slash && !chord) {
            return;
        }
        if (slash && isEditableTarget(event.target)) {
            return;
        }
        event.preventDefault();
        focusManualSearch({ openDrawer: true });
    });

    loadManualSearchData().then(function(data) {
        searchData = data;
        if (input.value.trim()) {
            paint();
        }
    });
}

function glossaryLetter(term) {
    const source = String(term || '').replace(/^[^A-Za-zÀ-ÿ]+/, '');
    const letter = source.charAt(0).toUpperCase();
    return letter && /[A-ZÀ-ÿ]/.test(letter) ? letter : '#';
}

function renderGlossaryPage(container, terms) {
    const strings = manualStrings();
    const sorted = terms.slice().sort(function(a, b) {
        return a.term.localeCompare(b.term, isItalianManual() ? 'it' : 'en', { sensitivity: 'base' });
    });

    const groups = {};
    sorted.forEach(function(term) {
        const letter = glossaryLetter(term.term);
        if (!groups[letter]) {
            groups[letter] = [];
        }
        groups[letter].push(term);
    });

    const letters = Object.keys(groups).sort();
    const lettersEl = document.getElementById('glossary-letters');
    if (lettersEl) {
        lettersEl.innerHTML = letters.map(function(letter) {
            return '<a href="#glossary-' + encodeURIComponent(letter) + '">' + escapeHtml(letter) + '</a>';
        }).join('');
    }

    let html = '';
    letters.forEach(function(letter) {
        html += '<section class="glossary-letter" id="glossary-' + escapeAttr(letter) + '">';
        html += '<h2>' + escapeHtml(letter) + '</h2>';
        html += '<dl class="glossary-list">';
        groups[letter].forEach(function(term) {
            html += '<dt id="' + escapeAttr(term.id) + '"><a href="' + escapeAttr(term.href) + '">' + escapeHtml(term.term) + '</a></dt>';
            html += '<dd>';
            if (term.summary) {
                html += '<p>' + escapeHtml(term.summary) + '</p>';
            }
            if (term.aliases && term.aliases.length) {
                html += '<p class="glossary-aliases"><span>' + escapeHtml(isItalianManual() ? 'Alias' : 'Also called') + ':</span> ';
                html += term.aliases.map(function(alias) {
                    return '<code>' + escapeHtml(alias) + '</code>';
                }).join(' ');
                html += '</p>';
            }
            if (term.also && term.also.length) {
                html += '<p class="glossary-also">' + term.also.map(function(href) {
                    return '<a href="' + escapeAttr(href) + '">' + escapeHtml(href.replace('.html', '').replace('#', ' § ')) + '</a>';
                }).join(' · ') + '</p>';
            }
            html += '</dd>';
        });
        html += '</dl></section>';
    });

    container.innerHTML = html || '<p>' + escapeHtml(strings.searchNoResults) + '</p>';
}

function initGlossaryPage() {
    const container = document.getElementById('glossary-list');
    if (!container) {
        return;
    }

    const fallback = getFallbackGlossary();
    renderGlossaryPage(container, fallback);

    fetchJsonFile('glossary.json').then(function(data) {
        if (data && Array.isArray(data.terms) && data.terms.length) {
            renderGlossaryPage(container, data.terms);
        }
    });
}

