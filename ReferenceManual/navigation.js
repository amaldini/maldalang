// Navigation and Interactive Features

const NAV_CATEGORY_ORDER = [
    'Language Fundamentals',
    'Built-in Features',
    'AI & Advanced Features',
    'Reference'
];

document.addEventListener('DOMContentLoaded', function() {
    if (document.body.style.zoom !== undefined) {
        document.body.style.zoom = '1';
    }

    injectNavbar().then(function() {
        initNavigation();
        initCollapsibleNav();
    });

    initHeaderActions();
    initCodeCopy();
    initSmoothScroll();
    highlightActiveSection();
});

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
    return FALLBACK_NAV_ITEMS;
}

const FALLBACK_NAV_ITEMS = [
        { href: "index.html", text: "Home", category: null },
        { href: "01-introduction.html", text: "1. Introduction", category: "Language Fundamentals" },
        { href: "25-tools.html", text: "2. Tools & Tooling", category: "Reference" },
        { href: "02-lexical-structure.html", text: "3. Lexical Structure", category: "Language Fundamentals" },
        { href: "03-data-types.html", text: "4. Data Types", category: "Language Fundamentals" },
        { href: "04-variables.html", text: "5. Variables", category: "Language Fundamentals" },
        { href: "07-expressions.html", text: "6. Expressions", category: "Language Fundamentals" },
        { href: "08-control-structures.html", text: "7. Control Structures", category: "Language Fundamentals" },
        { href: "09-functions.html", text: "8. Functions", category: "Language Fundamentals" },
        { href: "05-arrays.html", text: "9. Arrays", category: "Language Fundamentals" },
        { href: "10-classes-objects.html", text: "10. Classes & Objects", category: "Language Fundamentals" },
        { href: "12-input-output.html", text: "11. Input/Output", category: "Built-in Features" },
        { href: "11-built-in-functions.html", text: "12. Built-in Functions", category: "Built-in Features" },
        { href: "06-graphs.html", text: "13. Graphs", category: "Built-in Features" },
        { href: "06-vectordb.html", text: "14. VectorDB", category: "Built-in Features" },
        { href: "13-actors.html", text: "15. Actors", category: "AI & Advanced Features" },
        { href: "14-agent-orchestration.html", text: "16. Agent Orchestration", category: "AI & Advanced Features" },
        { href: "21-graph-memory.html", text: "17. GraphMemory", category: "AI & Advanced Features" },
        { href: "15-database.html", text: "18. Database Support", category: "Built-in Features" },
        { href: "16-web-ui-hub.html", text: "19. Web UI Overview", category: "Built-in Features" },
        { href: "16-web-ui.html", text: "20. Web UI Server Components", category: "Built-in Features" },
        { href: "16-http-server-html-ui.html", text: "21. HttpServer & HTML UI Generation", category: "Built-in Features" },
        { href: "16-browser-javascript-backend.html", text: "22. Browser JavaScript UI Backend", category: "Built-in Features" },
        { href: "17-rest-api.html", text: "23. REST API Server", category: "Built-in Features" },
        { href: "18-rest-web-client.html", text: "24. REST Web Client", category: "Built-in Features" },
        { href: "19-mcp-server.html", text: "25. MCP Server", category: "Built-in Features" },
        { href: "20-acp.html", text: "26. ACP (Agent Communication Protocol)", category: "AI & Advanced Features" },
        { href: "21-dotnet-interop.html", text: "27. .NET Interop", category: "Built-in Features" },
        { href: "24-device-integration.html", text: "28. Device Integration", category: "Built-in Features" },
        { href: "26-personal-assistant.html", text: "29. Personal Assistant and CLI", category: "Reference" },
        { href: "20-examples.html", text: "30. Examples", category: "Reference" },
        { href: "30-full-stack-development.html", text: "31. Full-Stack Development with MALDA", category: "Reference" },
        { href: "31-durable-workflows.html", text: "32. Durable Workflows", category: "Reference" },
        { href: "27-property-testing.html", text: "33. Property Testing", category: "Reference" },
        { href: "22-grammar.html", text: "34. Grammar", category: "Reference" },
        { href: "23-appendix.html", text: "35. Appendix", category: "Reference" },
];

function renderCollapsibleNav(nav, items) {
    const homeItem = items.find(function(item) { return item.href === 'index.html'; });
    const chapterItems = items.filter(function(item) { return item.href !== 'index.html'; });

    let navHTML = '<ul class="nav-root">';

    if (homeItem) {
        navHTML += '<li class="nav-home"><a href="index.html">' + homeItem.text + '</a></li>';
    }

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
        navHTML += '<span class="nav-category-label">' + escapeHtml(category) + '</span>';
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

function initHeaderActions() {
    const header = document.querySelector('header');
    if (!header || header.querySelector('.manual-actions')) {
        return;
    }

    const actions = document.createElement('div');
    actions.className = 'manual-actions';

    const printButton = document.createElement('button');
    printButton.type = 'button';
    printButton.className = 'manual-action';
    printButton.textContent = 'Print / PDF';
    printButton.title = 'Print this chapter (A4, code wrapped to the page)';
    printButton.addEventListener('click', function() {
        window.print();
    });

    actions.appendChild(printButton);
    header.appendChild(actions);
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
            button.className = 'copy-btn';
            button.textContent = 'Copy';
            button.setAttribute('aria-label', 'Copy code to clipboard');

            button.addEventListener('click', function() {
                copyToClipboard(codeBlockText(block));
                button.textContent = 'Copied!';
                setTimeout(function() {
                    button.textContent = 'Copy';
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
    const nav = document.querySelector('nav');
    nav.classList.toggle('mobile-open');
}
