// Generate Table of Contents on the home page (grouped by category).
// Prefers chapters.json over HTTP; falls back to FALLBACK_TOC_CHAPTERS offline.

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
    { file: "25-tools.html", title: "Tools & Tooling", num: "2", category: "Language Fundamentals", description: "Interpreter, compiler, and package manager" },
    { file: "02-lexical-structure.html", title: "Lexical Structure", num: "3", category: "Language Fundamentals", description: "Comments, identifiers, keywords, operators" },
    { file: "03-data-types.html", title: "Data Types", num: "4", category: "Language Fundamentals", description: "Primitive types, type system, objects" },
    { file: "04-variables.html", title: "Variables", num: "5", category: "Language Fundamentals", description: "Declaration, assignment, scope" },
    { file: "05-arrays.html", title: "Arrays", num: "6", category: "Language Fundamentals", description: "Array operations and methods" },
    { file: "07-expressions.html", title: "Expressions", num: "7", category: "Language Fundamentals", description: "Arithmetic, comparison, logical operations" },
    { file: "08-control-structures.html", title: "Control Structures", num: "8", category: "Language Fundamentals", description: "Conditionals and loops" },
    { file: "09-functions.html", title: "Functions", num: "9", category: "Language Fundamentals", description: "Function declaration, calls, decorators" },
    { file: "10-classes-objects.html", title: "Classes & Objects", num: "10", category: "Language Fundamentals", description: "OOP features, inheritance" },
    { file: "12-input-output.html", title: "Input/Output", num: "11", category: "Standard Library", description: "print, input, sleep functions" },
    { file: "11-built-in-functions.html", title: "Built-in Functions", num: "12", category: "Standard Library", description: "Type conversion, math, string, file operations" },
    { file: "06-graphs.html", title: "Graphs", num: "13", category: "Standard Library", description: "Graph data structures and algorithms" },
    { file: "06-vectordb.html", title: "VectorDB", num: "14", category: "Standard Library", description: "Vector database for similarity search" },
    { file: "15-database.html", title: "Database Support", num: "15", category: "Standard Library", description: "SQL Server and PostgreSQL clients" },
    { file: "13-actors.html", title: "Actors", num: "16", category: "AI & Agents", description: "Actor model and message passing" },
    { file: "14-agent-orchestration.html", title: "Agent Orchestration", num: "17", category: "AI & Agents", description: "LLM clients, agents, tools" },
    { file: "21-graph-memory.html", title: "GraphMemory", num: "18", category: "AI & Agents", description: "Semantic memory system for AI agents" },
    { file: "19-mcp-server.html", title: "MCP Server", num: "19", category: "AI & Agents", description: "MCP protocol support" },
    { file: "20-acp.html", title: "ACP (Agent Communication Protocol)", num: "20", category: "AI & Agents", description: "Agent communication and collaboration protocol" },
    { file: "31-durable-workflows.html", title: "Durable Workflows", num: "21", category: "AI & Agents", description: "Durable workflow syntax, runtime model, CLI, DLQ, and operations" },
    { file: "16-web-ui-hub.html", title: "Web UI Overview", num: "22", category: "Web", description: "Choose server components, HttpServer pages, or browser JS" },
    { file: "16-web-ui.html", title: "Web UI Server Components", num: "23", category: "Web", description: "Server components, fragments, live updates, and ui.*" },
    { file: "16-http-server-html-ui.html", title: "HttpServer & HTML UI Generation", num: "24", category: "Web", description: "Route-first hosting, @PAGE, @AIPAGE, and HTML generation" },
    { file: "16-browser-javascript-backend.html", title: "Browser JavaScript UI Backend", num: "25", category: "Web", description: "Browser-hosted MALDA compiled to JavaScript" },
    { file: "17-rest-api.html", title: "REST API Server", num: "26", category: "Web", description: "Creating REST APIs" },
    { file: "18-rest-web-client.html", title: "REST Web Client", num: "27", category: "Web", description: "Making HTTP requests to REST APIs" },
    { file: "30-full-stack-development.html", title: "Full-Stack Development with MALDA", num: "28", category: "Web", description: "End-to-end full-stack architecture and implementation flow" },
    { file: "21-dotnet-interop.html", title: ".NET Interop", num: "29", category: "Platform", description: "Loading and using external .NET libraries" },
    { file: "24-device-integration.html", title: "Device Integration", num: "30", category: "Platform", description: "Control physical devices (Arduino, ESP32, IoT, etc.)" },
    { file: "26-personal-assistant.html", title: "Personal Assistant and CLI", num: "31", category: "Platform", description: "Assistant commands, scheduling, and channels" },
    { file: "20-examples.html", title: "Examples", num: "32", category: "Reference", description: "Complete code examples" },
    { file: "27-property-testing.html", title: "Property Testing", num: "33", category: "Reference", description: "Deterministic property tests, shrinking, and regression workflows" },
    { file: "22-grammar.html", title: "Grammar", num: "34", category: "Reference", description: "BNF-like grammar specification" },
    { file: "23-appendix.html", title: "Appendix", num: "35", category: "Reference", description: "Reserved words, operator precedence" }
];

function chaptersToTocItems(chapters) {
    const items = [];
    let num = 0;
    chapters.forEach(function(chapter) {
        if (chapter.isHome) {
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
        tocHTML += '  <summary>' + category + '</summary>\n';
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

    const hub = chapters.find(function(ch) { return ch.file === '16-web-ui-hub.html'; });
    if (hub) {
        tocHTML += '\n<div class="info-box" style="margin-top: 20px;">\n';
        tocHTML += '    <strong>Web UI:</strong> Not sure which UI chapter to read? Start with <a href="' + hub.file + '">' + chapterLabel(hub) + '</a>.\n';
        tocHTML += '</div>\n';
    }

    tocContainer.innerHTML = tocHTML;

    const navFooter = document.querySelector('.nav-footer');
    if (navFooter && chapters.length > 0) {
        const nextLink = navFooter.querySelector('a[href*="01-introduction"], a:last-child');
        if (nextLink) {
            const firstChapter = chapters[0];
            nextLink.textContent = 'Next: ' + chapterLabel(firstChapter) + '→';
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

    renderTableOfContents(tocContainer, FALLBACK_TOC_CHAPTERS);
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', generateTableOfContents);
} else {
    generateTableOfContents();
}
