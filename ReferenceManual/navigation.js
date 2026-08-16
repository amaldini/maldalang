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
            printTitle: 'Stampa questo capitolo (A4, codice adattato alla pagina)',
            copy: 'Copia',
            copied: 'Copiato!',
            copyAria: 'Copia il codice negli appunti',
            langSwitch: 'English',
            langSwitchTitle: 'English version of this page'
        };
    }
    return {
        openContents: 'Open contents',
        closeContents: 'Close contents',
        print: 'Print / PDF',
        printTitle: 'Print this chapter (A4, code wrapped to the page)',
        copy: 'Copy',
        copied: 'Copied!',
        copyAria: 'Copy code to clipboard',
        langSwitch: 'Italiano',
        langSwitchTitle: 'Versione italiana di questa pagina'
    };
}

document.addEventListener('DOMContentLoaded', function() {
    if (document.body.style.zoom !== undefined) {
        document.body.style.zoom = '1';
    }

    injectHreflang();

    injectNavbar().then(function() {
        initNavigation();
        initCollapsibleNav();
        initNavDrawer();
    });

    initHeaderActions();
    initCodeCopy();
    initSmoothScroll();
    highlightActiveSection();
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
        { href: "10-classes-objects.html", text: "10. Classes & Objects", category: "Language Fundamentals" },
        { href: "11-input-output.html", text: "11. Input/Output", category: "Standard Library" },
        { href: "12-built-in-functions.html", text: "12. Built-in Functions", category: "Standard Library" },
        { href: "13-graphs.html", text: "13. Graphs", category: "Standard Library" },
        { href: "14-vectordb.html", text: "14. VectorDB", category: "Standard Library" },
        { href: "15-database.html", text: "15. Database Support", category: "Standard Library" },
        { href: "16-actors.html", text: "16. Actors", category: "AI & Agents" },
        { href: "17-agent-orchestration.html", text: "17. Agent Orchestration", category: "AI & Agents" },
        { href: "18-graph-memory.html", text: "18. GraphMemory", category: "AI & Agents" },
        { href: "19-mcp-server.html", text: "19. MCP Server", category: "AI & Agents" },
        { href: "20-acp.html", text: "20. ACP (Agent Communication Protocol)", category: "AI & Agents" },
        { href: "21-durable-workflows.html", text: "21. Durable Workflows", category: "AI & Agents" },
        { href: "22-web-ui-hub.html", text: "22. Web UI Overview", category: "Web" },
        { href: "23-web-ui.html", text: "23. Web UI Server Components", category: "Web" },
        { href: "24-http-server-html-ui.html", text: "24. HttpServer & HTML UI Generation", category: "Web" },
        { href: "25-browser-javascript-backend.html", text: "25. Browser JavaScript UI Backend", category: "Web" },
        { href: "26-rest-api.html", text: "26. REST API Server", category: "Web" },
        { href: "27-rest-web-client.html", text: "27. REST Web Client", category: "Web" },
        { href: "28-full-stack-development.html", text: "28. Full-Stack Development with MALDA", category: "Web" },
        { href: "29-dotnet-interop.html", text: "29. .NET Interop", category: "Platform" },
        { href: "30-device-integration.html", text: "30. Device Integration", category: "Platform" },
        { href: "31-personal-assistant.html", text: "31. Personal Assistant and CLI", category: "Platform" },
        { href: "32-examples.html", text: "32. Examples", category: "Reference" },
        { href: "33-property-testing.html", text: "33. Property Testing", category: "Reference" },
        { href: "34-grammar.html", text: "34. Grammar", category: "Reference" },
        { href: "35-appendix.html", text: "35. Appendix", category: "Reference" },
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
        { href: "10-classes-objects.html", text: "10. Classi e oggetti", category: "Language Fundamentals" },
        { href: "11-input-output.html", text: "11. Input/Output", category: "Standard Library" },
        { href: "12-built-in-functions.html", text: "12. Funzioni built-in", category: "Standard Library" },
        { href: "13-graphs.html", text: "13. Grafi", category: "Standard Library" },
        { href: "14-vectordb.html", text: "14. VectorDB", category: "Standard Library" },
        { href: "15-database.html", text: "15. Supporto database", category: "Standard Library" },
        { href: "16-actors.html", text: "16. Actor", category: "AI & Agents" },
        { href: "17-agent-orchestration.html", text: "17. Orchestrazione di agenti", category: "AI & Agents" },
        { href: "18-graph-memory.html", text: "18. GraphMemory", category: "AI & Agents" },
        { href: "19-mcp-server.html", text: "19. Server MCP", category: "AI & Agents" },
        { href: "20-acp.html", text: "20. ACP (Agent Communication Protocol)", category: "AI & Agents" },
        { href: "21-durable-workflows.html", text: "21. Workflow durevoli", category: "AI & Agents" },
        { href: "22-web-ui-hub.html", text: "22. Panoramica Web UI", category: "Web" },
        { href: "23-web-ui.html", text: "23. Componenti server Web UI", category: "Web" },
        { href: "24-http-server-html-ui.html", text: "24. HttpServer e generazione UI HTML", category: "Web" },
        { href: "25-browser-javascript-backend.html", text: "25. Backend UI JavaScript nel browser", category: "Web" },
        { href: "26-rest-api.html", text: "26. Server REST API", category: "Web" },
        { href: "27-rest-web-client.html", text: "27. Client REST Web", category: "Web" },
        { href: "28-full-stack-development.html", text: "28. Sviluppo full-stack con MALDA", category: "Web" },
        { href: "29-dotnet-interop.html", text: "29. Interop .NET", category: "Platform" },
        { href: "30-device-integration.html", text: "30. Integrazione dispositivi", category: "Platform" },
        { href: "31-personal-assistant.html", text: "31. Assistente personale e CLI", category: "Platform" },
        { href: "32-examples.html", text: "32. Esempi", category: "Reference" },
        { href: "33-property-testing.html", text: "33. Property testing", category: "Reference" },
        { href: "34-grammar.html", text: "34. Grammatica", category: "Reference" },
        { href: "35-appendix.html", text: "35. Appendice", category: "Reference" },
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

function initHeaderActions() {
    const header = document.querySelector('header');
    if (!header) {
        return;
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
        langSwitch.textContent = strings.langSwitch;
        langSwitch.title = strings.langSwitchTitle;
        langSwitch.setAttribute('hreflang', isItalianManual() ? 'en' : 'it');
        langSwitch.setAttribute('lang', isItalianManual() ? 'en' : 'it');

        const printButton = document.createElement('button');
        printButton.type = 'button';
        printButton.className = 'manual-action';
        printButton.textContent = strings.print;
        printButton.title = strings.printTitle;
        printButton.addEventListener('click', function() {
            window.print();
        });

        actions.appendChild(langSwitch);
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
