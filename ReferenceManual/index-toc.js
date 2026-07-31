// Generate Table of Contents on the home page (grouped by category)

const TOC_CATEGORY_ORDER = [
    'Language Fundamentals',
    'Built-in Features',
    'AI & Advanced Features',
    'Reference'
];

const staticChapters = [
    { file: "01-introduction.html", title: "Introduction", num: "1", category: "Language Fundamentals", description: "Language overview and features" },
    { file: "25-tools.html", title: "Tools & Tooling", num: "2", category: "Reference", description: "Interpreter, compiler, and package manager" },
    { file: "02-lexical-structure.html", title: "Lexical Structure", num: "3", category: "Language Fundamentals", description: "Comments, identifiers, keywords, operators" },
    { file: "03-data-types.html", title: "Data Types", num: "4", category: "Language Fundamentals", description: "Primitive types, type system, objects" },
    { file: "04-variables.html", title: "Variables", num: "5", category: "Language Fundamentals", description: "Declaration, assignment, scope" },
    { file: "07-expressions.html", title: "Expressions", num: "6", category: "Language Fundamentals", description: "Arithmetic, comparison, logical operations" },
    { file: "08-control-structures.html", title: "Control Structures", num: "7", category: "Language Fundamentals", description: "Conditionals and loops" },
    { file: "09-functions.html", title: "Functions", num: "8", category: "Language Fundamentals", description: "Function declaration, calls, decorators" },
    { file: "05-arrays.html", title: "Arrays", num: "9", category: "Language Fundamentals", description: "Array operations and methods" },
    { file: "10-classes-objects.html", title: "Classes & Objects", num: "10", category: "Language Fundamentals", description: "OOP features, inheritance" },
    { file: "12-input-output.html", title: "Input/Output", num: "11", category: "Built-in Features", description: "print, input, sleep functions" },
    { file: "11-built-in-functions.html", title: "Built-in Functions", num: "12", category: "Built-in Features", description: "Type conversion, math, string, file operations" },
    { file: "06-graphs.html", title: "Graphs", num: "13", category: "Built-in Features", description: "Graph data structures and algorithms" },
    { file: "06-vectordb.html", title: "VectorDB", num: "14", category: "Built-in Features", description: "Vector database for similarity search" },
    { file: "13-actors.html", title: "Actors", num: "15", category: "AI & Advanced Features", description: "Actor model and message passing" },
    { file: "14-agent-orchestration.html", title: "Agent Orchestration", num: "16", category: "AI & Advanced Features", description: "LLM clients, agents, tools" },
    { file: "21-graph-memory.html", title: "GraphMemory", num: "17", category: "AI & Advanced Features", description: "Semantic memory system for AI agents" },
    { file: "15-database.html", title: "Database Support", num: "18", category: "Built-in Features", description: "SQL Server and PostgreSQL clients" },
    { file: "16-web-ui-hub.html", title: "Web UI Overview", num: "19", category: "Built-in Features", description: "Choose server components, HttpServer pages, or browser JS" },
    { file: "16-web-ui.html", title: "Web UI Server Components", num: "20", category: "Built-in Features", description: "Server components, fragments, live updates, and ui.*" },
    { file: "16-http-server-html-ui.html", title: "HttpServer & HTML UI Generation", num: "21", category: "Built-in Features", description: "Route-first hosting, @PAGE, @AIPAGE, and HTML generation" },
    { file: "16-browser-javascript-backend.html", title: "Browser JavaScript UI Backend", num: "22", category: "Built-in Features", description: "Browser-hosted MALDA compiled to JavaScript" },
    { file: "17-rest-api.html", title: "REST API Server", num: "23", category: "Built-in Features", description: "Creating REST APIs" },
    { file: "18-rest-web-client.html", title: "REST Web Client", num: "24", category: "Built-in Features", description: "Making HTTP requests to REST APIs" },
    { file: "19-mcp-server.html", title: "MCP Server", num: "25", category: "Built-in Features", description: "MCP protocol support" },
    { file: "20-acp.html", title: "ACP (Agent Communication Protocol)", num: "26", category: "AI & Advanced Features", description: "Agent communication and collaboration protocol" },
    { file: "21-dotnet-interop.html", title: ".NET Interop", num: "27", category: "Built-in Features", description: "Loading and using external .NET libraries" },
    { file: "24-device-integration.html", title: "Device Integration", num: "28", category: "Built-in Features", description: "Control physical devices (Arduino, ESP32, IoT, etc.)" },
    { file: "26-personal-assistant.html", title: "Personal Assistant and CLI", num: "29", category: "Reference", description: "Assistant commands, scheduling, and channels" },
    { file: "20-examples.html", title: "Examples", num: "30", category: "Reference", description: "Complete code examples" },
    { file: "30-full-stack-development.html", title: "Full-Stack Development with MALDA", num: "31", category: "Reference", description: "End-to-end CRM/ticketing architecture and implementation flow" },
    { file: "31-durable-workflows.html", title: "Durable Workflows", num: "32", category: "Reference", description: "Durable workflow syntax, runtime model, CLI, DLQ, and operations" },
    { file: "27-property-testing.html", title: "Property Testing", num: "33", category: "Reference", description: "Deterministic property tests, shrinking, and regression workflows" },
    { file: "22-grammar.html", title: "Grammar", num: "34", category: "Reference", description: "BNF-like grammar specification" },
    { file: "23-appendix.html", title: "Appendix", num: "35", category: "Reference", description: "Reserved words, operator precedence" }
];

function generateTableOfContents() {
    const tocContainer = document.getElementById('table-of-contents');
    if (!tocContainer) return;

    let tocHTML = '';

    TOC_CATEGORY_ORDER.forEach(function(category) {
        const chapters = staticChapters
            .filter(function(ch) { return ch.category === category; })
            .sort(function(a, b) { return parseInt(a.num, 10) - parseInt(b.num, 10); });

        if (chapters.length === 0) {
            return;
        }

        tocHTML += '<details class="toc-category" open>\n';
        tocHTML += '  <summary>' + category + '</summary>\n';
        tocHTML += '  <ul>\n';

        chapters.forEach(function(chapter) {
            const numPrefix = chapter.num ? chapter.num + '. ' : '';
            tocHTML += '    <li><a href="' + chapter.file + '">' + numPrefix + chapter.title + '</a>';
            if (chapter.description) {
                tocHTML += ' - ' + chapter.description;
            }
            tocHTML += '</li>\n';
        });

        tocHTML += '  </ul>\n';
        tocHTML += '</details>\n';
    });

    tocHTML += '\n<div class="info-box" style="margin-top: 20px;">\n';
    tocHTML += '    <strong>Web UI:</strong> Not sure which UI chapter to read? Start with <a href="16-web-ui-hub.html">19. Web UI Overview</a>.\n';
    tocHTML += '</div>\n';

    tocContainer.innerHTML = tocHTML;

    const navFooter = document.querySelector('.nav-footer');
    if (navFooter) {
        const nextLink = navFooter.querySelector('a[href*="01-introduction"], a:last-child');
        if (nextLink && staticChapters.length > 0) {
            const firstChapter = staticChapters[0];
            nextLink.textContent = 'Next: ' + firstChapter.num + '. ' + firstChapter.title + '→';
            nextLink.setAttribute('href', firstChapter.file);
        }
    }
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', generateTableOfContents);
} else {
    generateTableOfContents();
}
