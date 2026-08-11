// MALDA Reference Manual - syntax highlighter
//
// Dependency-free tokenizer for MALDA plus the shell / REPL / JSON snippets that
// appear in the manual. Keyword list mirrors MaldaLang/Lexer.cs.
//
// Every source line is wrapped in <span class="ln">. Print stylesheets rely on
// those per-line boxes to give wrapped code a hanging indent, which is what
// keeps long lines readable on paper.

(function (global) {
    'use strict';

    var KEYWORDS = [
        'if', 'else', 'while', 'for', 'foreach', 'function', 'fn', 'def',
        'component', 'return', 'var', 'const', 'print', 'input', 'and', 'or',
        'not', 'break', 'continue', 'try', 'catch', 'finally', 'throw', 'defer',
        'class', 'new', 'this', 'super', 'extends', 'public', 'private',
        'static', 'actor', 'message', 'spawn', 'send', 'receive', 'self', 'on',
        'then', 'timeout', 'dict', 'graph', 'directed', 'undirected', 'in',
        'using', 'import', 'export', 'include', 'prompt', 'property',
        'match', 'case', 'default', 'type', 'schema', 'api', 'await', 'async',
        'workflow', 'step', 'approval', 'wait', 'retry', 'backoff', 'delay',
        'maxDelay', 'compensate', 'onReject'
    ];

    var LITERALS = ['true', 'false', 'null'];

    // Longest listing that still fits comfortably on one printed page.
    var SHORT_BLOCK_LINES = 20;

    var TAB_SIZE = 4;
    // Deeply indented lines stop earning extra hang; past this the wrapped text
    // would be pushed off the measure.
    var MAX_HANG_COLUMNS = 24;

    var SHELL_COMMANDS = [
        'malda', 'dotnet', 'npm', 'npx', 'pnpm', 'yarn', 'pip', 'git', 'curl',
        'wget', 'echo', 'pwsh', 'powershell', 'bash', 'sh', 'zsh', 'cd', 'ls',
        'dir', 'mkdir', 'rm', 'cp', 'mv', 'cat', 'export', 'set', 'setx',
        'node', 'python', 'python3', 'docker', 'make', 'code'
    ];

    var keywordSet = toSet(KEYWORDS);
    var literalSet = toSet(LITERALS);
    var shellCommandSet = toSet(SHELL_COMMANDS);

    var TOKEN_PATTERN = new RegExp([
        '(\\/\\/[^\\n]*)',                                  // 1  line comment
        '(\\/\\*[\\s\\S]*?\\*\\/)',                         // 2  block comment
        '(\\$"""[\\s\\S]*?""")',                            // 3  interpolated triple string
        '("""[\\s\\S]*?""")',                               // 4  triple string
        '(\\$"(?:\\\\[\\s\\S]|[^"\\\\])*")',                // 5  interpolated string
        '("(?:\\\\[\\s\\S]|[^"\\\\])*")',                   // 6  string
        "('(?:\\\\[\\s\\S]|[^'\\\\])*')",                   // 7  single-quoted string
        '(@[A-Za-z_][A-Za-z0-9_]*)',                        // 8  decorator
        '(\\b\\d[\\d_]*(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)',    // 9  number
        '([A-Za-z_][A-Za-z0-9_]*)',                         // 10 identifier
        '(\\|>|=>|->|\\+\\+|--|[+\\-*/%]=|[=!<>]=|&&|\\|\\||[+\\-*/%=<>!?&|~^])', // 11 operator
        '([{}()\\[\\];,.:])'                                // 12 punctuation
    ].join('|'), 'g');

    function toSet(values) {
        var set = Object.create(null);
        for (var i = 0; i < values.length; i++) {
            set[values[i]] = true;
        }
        return set;
    }

    function escapeHtml(text) {
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    // Collects highlighted output as one HTML fragment per source line.
    function Emitter() {
        this.lines = [''];
    }

    Emitter.prototype.push = function (cls, text) {
        if (text === '') {
            return;
        }
        var parts = String(text).split('\n');
        for (var i = 0; i < parts.length; i++) {
            if (i > 0) {
                this.lines.push('');
            }
            if (parts[i] !== '') {
                this.pushHtml(cls
                    ? '<span class="' + cls + '">' + escapeHtml(parts[i]) + '</span>'
                    : escapeHtml(parts[i]));
            }
        }
    };

    Emitter.prototype.pushHtml = function (html) {
        this.lines[this.lines.length - 1] += html;
    };

    // `indents` carries each line's leading column count. Print stylesheets use
    // it to hang a wrapped continuation off the line's own indentation instead
    // of off the left edge of the block, so a continuation never looks like a
    // dedent.
    Emitter.prototype.toHtml = function (indents) {
        var html = '';
        for (var i = 0; i < this.lines.length; i++) {
            var indent = (indents && indents[i]) || 0;
            html += indent > 0
                ? '<span class="ln" style="--i:' + indent + '">' + this.lines[i] + '</span>'
                : '<span class="ln">' + this.lines[i] + '</span>';
        }
        return html;
    };

    function leadingColumns(line) {
        var columns = 0;
        for (var i = 0; i < line.length; i++) {
            var ch = line.charAt(i);
            if (ch === ' ') {
                columns += 1;
            } else if (ch === '\t') {
                columns += TAB_SIZE - (columns % TAB_SIZE);
            } else {
                break;
            }
        }
        return Math.min(columns, MAX_HANG_COLUMNS);
    }

    function previousMeaningfulChar(source, index) {
        for (var i = index - 1; i >= 0; i--) {
            var ch = source.charAt(i);
            if (ch !== ' ' && ch !== '\t') {
                return ch;
            }
        }
        return '';
    }

    function nextMeaningfulChar(source, index) {
        for (var i = index; i < source.length; i++) {
            var ch = source.charAt(i);
            if (ch !== ' ' && ch !== '\t') {
                return ch;
            }
        }
        return '';
    }

    function classifyIdentifier(source, name, start, end) {
        if (keywordSet[name]) {
            return 'mld-kw';
        }
        if (literalSet[name]) {
            return 'mld-lit';
        }
        if (previousMeaningfulChar(source, start) === '.') {
            return 'mld-prop';
        }
        if (nextMeaningfulChar(source, end) === '(') {
            return 'mld-fn';
        }
        if (/^[A-Z]/.test(name)) {
            return 'mld-type';
        }
        return '';
    }

    // Splits $"text {expr}" so the embedded expressions keep their own colours.
    function emitInterpolatedString(emitter, raw) {
        var pattern = /\{\{|\}\}|\{([^{}]*)\}/g;
        var last = 0;
        var match;

        while ((match = pattern.exec(raw)) !== null) {
            emitter.push('mld-str', raw.slice(last, match.index));
            if (match[1] === undefined) {
                emitter.push('mld-str', match[0]);
            } else {
                emitter.push('mld-interp-brace', '{');
                emitMalda(emitter, match[1]);
                emitter.push('mld-interp-brace', '}');
            }
            last = pattern.lastIndex;
        }

        emitter.push('mld-str', raw.slice(last));
    }

    function emitMalda(emitter, source) {
        var pattern = new RegExp(TOKEN_PATTERN.source, 'g');
        var last = 0;
        var match;

        while ((match = pattern.exec(source)) !== null) {
            if (match.index > last) {
                emitter.push('', source.slice(last, match.index));
            }
            last = pattern.lastIndex;

            if (match[1] !== undefined || match[2] !== undefined) {
                emitter.push('mld-comment', match[0]);
            } else if (match[3] !== undefined || match[5] !== undefined) {
                emitInterpolatedString(emitter, match[0]);
            } else if (match[4] !== undefined || match[6] !== undefined || match[7] !== undefined) {
                emitter.push('mld-str', match[0]);
            } else if (match[8] !== undefined) {
                emitter.push('mld-at', match[0]);
            } else if (match[9] !== undefined) {
                emitter.push('mld-num', match[0]);
            } else if (match[10] !== undefined) {
                emitter.push(
                    classifyIdentifier(source, match[10], match.index, pattern.lastIndex),
                    match[0]);
            } else if (match[11] !== undefined) {
                emitter.push('mld-op', match[0]);
            } else {
                emitter.push('mld-punct', match[0]);
            }
        }

        if (last < source.length) {
            emitter.push('', source.slice(last));
        }
    }

    function emitShellLine(emitter, line) {
        var pattern = /("(?:\\.|[^"\\])*")|('(?:\\.|[^'\\])*')|(#[^\n]*)|(--?[A-Za-z][\w-]*)|(\|)|([A-Za-z_][\w.-]*)/g;
        var last = 0;
        var isFirstWord = true;
        var match;

        while ((match = pattern.exec(line)) !== null) {
            if (match.index > last) {
                emitter.push('', line.slice(last, match.index));
            }
            last = pattern.lastIndex;

            if (match[1] !== undefined || match[2] !== undefined) {
                emitter.push('mld-str', match[0]);
            } else if (match[3] !== undefined) {
                emitter.push('mld-comment', match[0]);
            } else if (match[4] !== undefined) {
                emitter.push('mld-flag', match[0]);
            } else if (match[5] !== undefined) {
                emitter.push('mld-op', match[0]);
                isFirstWord = true;
            } else if (isFirstWord) {
                emitter.push('mld-cmd', match[0]);
                isFirstWord = false;
            } else {
                emitter.push('', match[0]);
            }
        }

        if (last < line.length) {
            emitter.push('', line.slice(last));
        }
    }

    // Terminal transcripts: prompt lines are commands, everything else is output.
    function emitShell(emitter, source) {
        var lines = source.split('\n');

        for (var i = 0; i < lines.length; i++) {
            if (i > 0) {
                emitter.push('', '\n');
            }
            var prompt = /^(\s*(?:PS[^>]*>|[$#])\s?)([\s\S]*)$/.exec(lines[i]);
            if (prompt) {
                emitter.push('mld-prompt', prompt[1]);
                emitShellLine(emitter, prompt[2]);
            } else if (looksLikeShellCommand(lines[i])) {
                emitShellLine(emitter, lines[i]);
            } else {
                emitter.push('mld-output', lines[i]);
            }
        }
    }

    // MALDA REPL transcripts: "> " lines are source, everything else is output.
    function emitRepl(emitter, source) {
        var lines = source.split('\n');

        for (var i = 0; i < lines.length; i++) {
            if (i > 0) {
                emitter.push('', '\n');
            }
            var prompt = /^(\s*>\s?)([\s\S]*)$/.exec(lines[i]);
            if (prompt) {
                emitter.push('mld-prompt', prompt[1]);
                emitMalda(emitter, prompt[2]);
            } else {
                emitter.push('mld-output', lines[i]);
            }
        }
    }

    function emitJson(emitter, source) {
        var pattern = /("(?:\\.|[^"\\])*")(\s*:)?|(\b-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)|(\btrue\b|\bfalse\b|\bnull\b)|([{}\[\],:])/g;
        var last = 0;
        var match;

        while ((match = pattern.exec(source)) !== null) {
            if (match.index > last) {
                emitter.push('', source.slice(last, match.index));
            }
            last = pattern.lastIndex;

            if (match[1] !== undefined) {
                emitter.push(match[2] ? 'mld-key' : 'mld-str', match[1]);
                if (match[2]) {
                    emitter.push('mld-punct', match[2]);
                }
            } else if (match[3] !== undefined) {
                emitter.push('mld-num', match[0]);
            } else if (match[4] !== undefined) {
                emitter.push('mld-lit', match[0]);
            } else {
                emitter.push('mld-punct', match[0]);
            }
        }

        if (last < source.length) {
            emitter.push('', source.slice(last));
        }
    }

    function looksLikeShellCommand(line) {
        var match = /^\s*([A-Za-z_][\w.-]*)/.exec(line);
        return match !== null && shellCommandSet[match[1]] === true;
    }

    function detectLanguage(source) {
        var lines = source.split('\n').filter(function (line) {
            return line.trim() !== '';
        });

        if (lines.length === 0) {
            return 'malda';
        }

        var hasShellPrompt = lines.some(function (line) {
            return /^\s*(?:PS[^>]*>|\$)\s/.test(line);
        });
        if (hasShellPrompt) {
            return 'shell';
        }

        var hasReplPrompt = lines.some(function (line) {
            return /^\s*>\s/.test(line);
        });
        if (hasReplPrompt) {
            return 'repl';
        }

        var commandLines = lines.filter(looksLikeShellCommand).length;
        if (commandLines / lines.length >= 0.5) {
            return 'shell';
        }

        var trimmed = source.trim();
        if ((trimmed.charAt(0) === '{' || trimmed.charAt(0) === '[') &&
            /"\s*:/.test(trimmed) &&
            !/\b(var|const|function|class|print|return)\b/.test(trimmed)) {
            return 'json';
        }

        return 'malda';
    }

    function highlight(source, language) {
        var text = String(source)
            .replace(/\r\n/g, '\n')
            .replace(/^\n/, '')
            .replace(/\s+$/, '');
        var lang = language || detectLanguage(text);
        var emitter = new Emitter();

        if (lang === 'shell') {
            emitShell(emitter, text);
        } else if (lang === 'repl') {
            emitRepl(emitter, text);
        } else if (lang === 'json') {
            emitJson(emitter, text);
        } else {
            lang = 'malda';
            emitMalda(emitter, text);
        }

        var indents = text.split('\n').map(leadingColumns);

        return {
            html: emitter.toHtml(indents),
            language: lang,
            lineCount: emitter.lines.length
        };
    }

    function highlightElement(code) {
        if (code.hasAttribute('data-highlighted')) {
            return;
        }
        // Some blocks (the keyword index) ship hand-written links; leave them alone.
        if (code.querySelector('*') !== null) {
            code.setAttribute('data-highlighted', 'skipped');
            return;
        }

        var declared = (code.className.match(/language-([\w-]+)/) || [])[1];
        var result = highlight(code.textContent, declared);

        code.innerHTML = result.html;
        code.setAttribute('data-highlighted', result.language);

        var pre = code.parentElement;
        if (pre && pre.tagName === 'PRE') {
            pre.setAttribute('data-language', result.language);
            pre.setAttribute('data-lines', String(result.lineCount));
            // Print stylesheets keep blocks up to this length on a single page.
            if (result.lineCount <= SHORT_BLOCK_LINES) {
                pre.setAttribute('data-short', '');
            }
        }
    }

    function highlightAll(root) {
        var scope = root || document;
        var blocks = scope.querySelectorAll('pre > code');
        for (var i = 0; i < blocks.length; i++) {
            highlightElement(blocks[i]);
        }
        return blocks.length;
    }

    global.MaldaHighlight = {
        highlight: highlight,
        highlightElement: highlightElement,
        highlightAll: highlightAll,
        detectLanguage: detectLanguage,
        keywords: KEYWORDS
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { highlightAll(); });
    } else {
        highlightAll();
    }
})(window);
