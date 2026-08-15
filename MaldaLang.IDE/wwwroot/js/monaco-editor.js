// Monaco Editor integration for SPL
window.monacoEditors = window.monacoEditors || {};

window.initMonacoEditor = function (elementId, dotNetHelper) {
    return new Promise((resolve, reject) => {
        if (window.monaco) {
            initializeEditor(elementId, dotNetHelper, resolve);
            return;
        }
        
        // Load Monaco Editor
        const script = document.createElement('script');
        script.src = 'https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min/vs/loader.js';
        script.onload = function () {
            require.config({ paths: { vs: 'https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min/vs' } });
            require(['vs/editor/editor.main'], function () {
                initializeEditor(elementId, dotNetHelper, resolve);
            });
        };
        script.onerror = reject;
        document.head.appendChild(script);
    });
};

function initializeEditor(elementId, dotNetHelper, resolve) {
    const element = document.getElementById(elementId);
    if (!element) {
        resolve(false);
        return;
    }
    
    // Register SPL language
    if (!monaco.languages.getLanguages().some(l => l.id === 'spl')) {
        monaco.languages.register({ id: 'spl' });
        
        // Define tokens for syntax highlighting
        monaco.languages.setMonarchTokensProvider('spl', {
            tokenizer: {
                root: [
                    [/\/\/.*$/, 'comment'],
                    [/\/\*[\s\S]*?\*\//, 'comment'],
                    [/"([^"\\]|\\.)*"/, 'string'],
                    [/\d+\.\d+/, 'number.float'],
                    [/\d+/, 'number'],
                    [/[a-z_][a-z0-9_]*/i, {
                        cases: {
                            '@keywords': 'keyword',
                            '@default': 'identifier'
                        }
                    }],
                    [/[+\-*/%=<>!&|]/, 'operator'],
                    [/[{}()\[\];,.]/, 'delimiter']
                ]
            },
            keywords: [
                'if', 'else', 'while', 'for', 'function', 'return', 'var',
                'print', 'input', 'true', 'false', 'and', 'or', 'not',
                'break', 'continue', 'class', 'new', 'this', 'super',
                'extends', 'public', 'private', 'static', 'null'
            ]
        });
    }
    
    // Create editor
    const editor = monaco.editor.create(element, {
        value: '',
        language: 'spl',
        theme: 'vs-dark',
        automaticLayout: true,
        minimap: { enabled: true },
        lineNumbers: 'on',
        roundedSelection: false,
        scrollBeyondLastLine: false,
        readOnly: false,
        fontSize: 14,
        glyphMargin: true,  // Enable glyph margin for breakpoints
        quickSuggestions: {
            other: true,
            comments: false,
            strings: false
        },
        suggestOnTriggerCharacters: true  // Enable suggestions on trigger characters like @
    });
    
    window.monacoEditors[elementId] = editor;
    
    // Register completion provider
    monaco.languages.registerCompletionItemProvider('spl', {
        triggerCharacters: ['@'],
        provideCompletionItems: function(model, position, context) {
            return new Promise((resolve) => {
                // Find the start of the current word by looking backwards from the caret
                const lineText = model.getLineContent(position.lineNumber);
                let wordStart = position.column - 1;
                
                // Look backwards to find the start of the identifier
                // Include @ character for decorator support
                while (wordStart > 0) {
                    const char = lineText[wordStart - 1];
                    if (char && (/\w/.test(char) || char === '_' || char === '@')) {
                        wordStart--;
                    } else {
                        break;
                    }
                }
                
                // Extract the current prefix
                const prefix = lineText.substring(wordStart, position.column - 1);
                
                // Check if this was triggered by @ character
                const isTriggeredByAt = context && context.triggerKind === monaco.languages.CompletionTriggerKind.TriggerCharacter && context.triggerCharacter === '@';
                
                // Get the full source text - this ensures decorator context detection works correctly
                // even when typing characters (the cursor position might be past the textUntilPosition range)
                const fullSource = model.getValue();
                
                // Column is 0-based for the language service
                // It should point to the position after the last typed character
                // When prefix starts with @, the column should point after the last character of the prefix
                let serviceColumn = position.column - 1; // Convert from 1-based to 0-based
                
                // If prefix starts with @, ensure column points to after the prefix
                if (prefix.startsWith('@')) {
                    // Column should point after the last character in the prefix
                    // wordStart is the position of @, prefix.length is the length including @
                    // So column should be wordStart + prefix.length
                    serviceColumn = wordStart + prefix.length;
                }
                
                dotNetHelper.invokeMethodAsync('GetCompletions', 
                    fullSource, position.lineNumber - 1, serviceColumn)
                    .then(completions => {
                        // Check if we're in decorator context
                        // This can be: prefix starts with @, or triggered by @ character, or completions are decorators
                        const isTriggeredByAt = context && context.triggerKind === monaco.languages.CompletionTriggerKind.TriggerCharacter && context.triggerCharacter === '@';
                        const prefixStartsWithAt = prefix.startsWith('@');
                        const hasDecoratorCompletions = completions.length > 0 && completions.some(c => c.kind === 'decorator');
                        const isDecoratorContext = prefixStartsWithAt || isTriggeredByAt || hasDecoratorCompletions;
                        
                        // Debug: log when we're in decorator context but got no completions
                        if (prefixStartsWithAt && completions.length === 0) {
                            console.log('Decorator context detected but no completions. Prefix:', prefix, 'Column:', adjustedColumn, 'Line:', lineText);
                        }
                        
                        // If prefix starts with @ but we got no completions, the language service might not have detected it
                        // This can happen when typing after @ (e.g., @MCP)
                        let finalCompletions = completions;
                        if (prefixStartsWithAt && completions.length === 0) {
                            // Extract the partial name after @
                            const partialName = prefix.substring(1); // Remove @
                            // Try multiple column positions to ensure we detect the decorator context
                            // The column should point to the position after the last typed character
                            // Try with the current column first
                            const retryColumn = position.column - 1;
                            return dotNetHelper.invokeMethodAsync('GetCompletions', 
                                fullSource, position.lineNumber - 1, retryColumn)
                                .then(retryCompletions => {
                                    // If still no completions, try with column pointing to after @ + partial name length
                                    if (retryCompletions.length === 0 && partialName.length > 0) {
                                        // Calculate column as: position of @ (wordStart) + 1 (after @) + partialName.length
                                        const atColumn = wordStart; // 0-based position of @
                                        const afterPartialColumn = atColumn + 1 + partialName.length;
                                        return dotNetHelper.invokeMethodAsync('GetCompletions', 
                                            fullSource, position.lineNumber - 1, afterPartialColumn)
                                            .then(retry2Completions => {
                                                finalCompletions = retry2Completions.length > 0 ? retry2Completions : retryCompletions;
                                                return processCompletions(finalCompletions, prefix, true, position, wordStart);
                                            });
                                    }
                                    finalCompletions = retryCompletions;
                                    return processCompletions(finalCompletions, prefix, true, position, wordStart);
                                });
                        }
                        
                        // If triggered by @ but no completions returned, try retry logic
                        if (isTriggeredByAt && completions.length === 0) {
                            // Check if @ is actually in the line at the expected position
                            const atPosition = position.column - 2; // Position of @ (0-based)
                            if (atPosition >= 0 && lineText[atPosition] === '@') {
                                // @ is there, try calling with column pointing right after it
                                return dotNetHelper.invokeMethodAsync('GetCompletions', 
                                    fullSource, position.lineNumber - 1, position.column - 1)
                                    .then(retryCompletions => {
                                        // Use retry completions if we got any
                                        finalCompletions = retryCompletions.length > 0 ? retryCompletions : completions;
                                        return processCompletions(finalCompletions, prefix, true, position, wordStart);
                                    });
                            } else {
                                // @ might not be in model yet, but we're triggered by it, so force decorator context
                                return processCompletions([], prefix, true, position, wordStart);
                            }
                        }
                        
                        return processCompletions(finalCompletions, prefix, isDecoratorContext, position, wordStart);
                    })
                    .then(result => {
                        if (result && result.suggestions) {
                            resolve(result);
                        } else {
                            resolve({ suggestions: [] });
                        }
                    })
                    .catch(err => {
                        console.error('Completion error:', err);
                        resolve({ suggestions: [] });
                    });
            });
        }
    });
    
    function processCompletions(completions, prefix, isDecoratorContext, position, wordStart) {
        // For decorator context, language service already filtered the completions
        // So we should use all returned completions without additional filtering
        // For other contexts, filter with the full prefix
        let filteredCompletions;
        if (isDecoratorContext) {
            // Language service already filtered decorators, use all returned completions
            filteredCompletions = completions;
        } else {
            // Filter completions based on the current prefix (case-insensitive)
            filteredCompletions = completions.filter(c => {
                if (prefix === '') {
                    return true; // Show all when prefix is empty
                }
                return c.label.toLowerCase().startsWith(prefix.toLowerCase());
            });
        }
        
        const suggestions = filteredCompletions.map(c => ({
            label: c.label,
            kind: monaco.languages.CompletionItemKind[c.kind] || monaco.languages.CompletionItemKind.Text,
            detail: c.detail,
            documentation: c.documentation,
            insertText: c.insertText || c.label,
            // Set the range to replace the current word being typed
            range: {
                startLineNumber: position.lineNumber,
                startColumn: wordStart + 1, // Convert to 1-based
                endLineNumber: position.lineNumber,
                endColumn: position.column
            }
        }));
        
        return { suggestions: suggestions };
    }
    
    // Register hover provider
    monaco.languages.registerHoverProvider('spl', {
        provideHover: function(model, position) {
            return new Promise((resolve) => {
                const text = model.getValue();
                dotNetHelper.invokeMethodAsync('GetHoverInfo', 
                    text, position.lineNumber - 1, position.column - 1)
                    .then(info => {
                        if (info) {
                            resolve({
                                range: {
                                    startLineNumber: position.lineNumber,
                                    startColumn: position.column,
                                    endLineNumber: position.lineNumber,
                                    endColumn: position.column
                                },
                                contents: [{ value: info }]
                            });
                        } else {
                            resolve(null);
                        }
                    })
                    .catch(err => {
                        console.error('Hover error:', err);
                        resolve(null);
                    });
            });
        }
    });
    
    // Handle content changes
    editor.onDidChangeModelContent(function (e) {
        const value = editor.getValue();
        dotNetHelper.invokeMethodAsync('OnContentChangedJS', value).catch(err => console.error(err));
        
        // Trigger completion when @ is typed
        if (e.changes && e.changes.length > 0) {
            e.changes.forEach(function(change) {
                // Check if @ was just typed
                if (change.text === '@') {
                    // Trigger completion after a short delay to ensure the @ is in the model
                    setTimeout(function() {
                        editor.getAction('editor.action.triggerSuggest').run();
                    }, 10);
                }
                
                const startLine = change.range.startLineNumber; // 1-based (DebuggerService / core)
                const oldLineCount = change.range.endLineNumber - change.range.startLineNumber + 1;
                const newLineCount = change.text.split('\n').length;
                const delta = newLineCount - oldLineCount;
                
                if (delta !== 0) {
                    dotNetHelper.invokeMethodAsync('OnModelContentChangedJS', startLine, delta)
                        .catch(err => console.error('Error adjusting breakpoints:', err));
                }
            });
        }
    });
    
    // Handle cursor position changes
    editor.onDidChangeCursorPosition(function (e) {
        dotNetHelper.invokeMethodAsync('OnCursorPositionChangedJS', 
            e.position.lineNumber - 1, e.position.column - 1).catch(err => console.error(err));
    });
    
    // Handle breakpoint clicks - click on glyph margin or line numbers area
    editor.onMouseDown(function (e) {
        if (e.target) {
            // Check if clicking on glyph margin (where breakpoints are shown) or line numbers
            if (e.target.type === monaco.editor.MouseTargetType.GUTTER_LINE_NUMBERS ||
                e.target.type === monaco.editor.MouseTargetType.GUTTER_GLYPH_MARGIN) {
                const line = e.target.position ? e.target.position.lineNumber : e.target.lineNumber;
                if (line) {
                    dotNetHelper.invokeMethodAsync('OnBreakpointToggleJS', line).catch(err => console.error(err));
                }
            }
        }
    });
    
    resolve(true);
}

window.initMonacoPreviewEditor = function (elementId) {
    return new Promise((resolve, reject) => {
        if (window.monaco) {
            initializePreviewEditor(elementId, resolve);
            return;
        }
        
        // Load Monaco Editor
        const script = document.createElement('script');
        script.src = 'https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min/vs/loader.js';
        script.onload = function () {
            require.config({ paths: { vs: 'https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min/vs' } });
            require(['vs/editor/editor.main'], function () {
                initializePreviewEditor(elementId, resolve);
            });
        };
        script.onerror = reject;
        document.head.appendChild(script);
    });
};

function initializePreviewEditor(elementId, resolve) {
    const element = document.getElementById(elementId);
    if (!element) {
        resolve(false);
        return;
    }
    
    // Ensure element is visible (remove display: none if present)
    if (element.style.display === 'none') {
        element.style.display = '';
    }
    
    // Register SPL language if not already registered
    if (!monaco.languages.getLanguages().some(l => l.id === 'spl')) {
        monaco.languages.register({ id: 'spl' });
        
        // Define tokens for syntax highlighting
        monaco.languages.setMonarchTokensProvider('spl', {
            tokenizer: {
                root: [
                    [/\/\/.*$/, 'comment'],
                    [/\/\*[\s\S]*?\*\//, 'comment'],
                    [/"([^"\\]|\\.)*"/, 'string'],
                    [/\d+\.\d+/, 'number.float'],
                    [/\d+/, 'number'],
                    [/[a-z_][a-z0-9_]*/i, {
                        cases: {
                            '@keywords': 'keyword',
                            '@default': 'identifier'
                        }
                    }],
                    [/[+\-*/%=<>!&|]/, 'operator'],
                    [/[{}()\[\];,.]/, 'delimiter']
                ]
            },
            keywords: [
                'if', 'else', 'while', 'for', 'function', 'return', 'var',
                'print', 'input', 'true', 'false', 'and', 'or', 'not',
                'break', 'continue', 'class', 'new', 'this', 'super',
                'extends', 'public', 'private', 'static', 'null'
            ]
        });
    }
    
    // Create read-only editor for preview
    const editor = monaco.editor.create(element, {
        value: '',
        language: 'spl',
        theme: 'vs-dark',
        automaticLayout: true,
        minimap: { enabled: false },
        lineNumbers: 'on',
        readOnly: true,
        fontSize: 12,
        scrollBeyondLastLine: false,
        wordWrap: 'on'
    });
    
    window.monacoEditors[elementId] = editor;
    
    // Force layout update to ensure proper sizing - use multiple attempts
    setTimeout(() => {
        editor.layout();
        // Additional layout update after a short delay to handle any async rendering
        setTimeout(() => {
            editor.layout();
        }, 100);
    }, 0);
    
    resolve(true);
}

window.setMonacoValue = function (elementId, value) {
    const editor = window.monacoEditors[elementId];
    if (editor) {
        editor.setValue(value || '');
        // Ensure editor is visible and properly laid out
        const element = document.getElementById(elementId);
        if (element) {
            element.style.display = '';
            // Force layout update
            setTimeout(() => {
                editor.layout();
            }, 0);
        }
    }
};

window.disposeMonacoEditor = function (elementId) {
    const editor = window.monacoEditors[elementId];
    if (editor) {
        editor.dispose();
        delete window.monacoEditors[elementId];
    }
};

window.getMonacoValue = function (elementId) {
    const editor = window.monacoEditors[elementId];
    if (editor) {
        return editor.getValue();
    }
    return '';
};

window.setMonacoDiagnostics = function (elementId, diagnostics) {
    const editor = window.monacoEditors[elementId];
    if (editor && editor.getModel()) {
        const markers = diagnostics.map(d => ({
            startLineNumber: d.line + 1,
            startColumn: d.column + 1,
            endLineNumber: d.line + 1,
            endColumn: d.column + (d.length || 1) + 1,
            message: d.message,
            severity: d.severity === 'Error' ? monaco.MarkerSeverity.Error :
                      d.severity === 'Warning' ? monaco.MarkerSeverity.Warning :
                      monaco.MarkerSeverity.Info
        }));
        monaco.editor.setModelMarkers(editor.getModel(), 'spl', markers);
    }
};

window.setMonacoBreakpoints = function (elementId, breakpoints) {
    const editor = window.monacoEditors[elementId];
    if (!editor || !editor.getModel()) return;
    
    // Store breakpoint decorations for this editor
    if (!window.monacoBreakpointDecorations) {
        window.monacoBreakpointDecorations = {};
    }
    
    const editorDecorations = window.monacoBreakpointDecorations[elementId] || [];
    
    // Remove old breakpoint decorations
    if (editorDecorations.length > 0) {
        editor.deltaDecorations(editorDecorations, []);
        window.monacoBreakpointDecorations[elementId] = [];
    }
    
    // Add new breakpoint decorations
    const newDecorations = breakpoints
        .filter(bp => bp.enabled !== false) // Only show enabled breakpoints
        .map(bp => ({
            range: {
                startLineNumber: bp.line,
                startColumn: 1,
                endLineNumber: bp.line,
                endColumn: 1
            },
            options: {
                isWholeLine: true,
                glyphMarginClassName: 'breakpoint-glyph',
                glyphMarginHoverMessage: { value: bp.condition ? `Breakpoint (condition: ${bp.condition})` : 'Breakpoint' },
                className: 'breakpoint-line',
                stickiness: monaco.editor.TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges
            }
        }));
    
    if (newDecorations.length > 0) {
        const decorationIds = editor.deltaDecorations([], newDecorations);
        window.monacoBreakpointDecorations[elementId] = decorationIds;
    }
    
    // Also add CSS for breakpoint glyph if not already added
    if (!document.getElementById('monaco-breakpoint-styles')) {
        const style = document.createElement('style');
        style.id = 'monaco-breakpoint-styles';
        style.textContent = `
            .breakpoint-glyph {
                background: #e51400;
                border-radius: 50%;
                width: 12px;
                height: 12px;
                margin-left: 2px;
                margin-top: 2px;
                cursor: pointer;
            }
            .breakpoint-glyph:hover {
                background: #ff0000;
            }
            .breakpoint-line {
                background-color: rgba(229, 20, 0, 0.1);
            }
        `;
        document.head.appendChild(style);
    }
};

window.highlightMonacoLine = function (elementId, line) {
    const editor = window.monacoEditors[elementId];
    if (editor) {
        editor.setPosition({ lineNumber: line + 1, column: 1 });
        editor.revealLineInCenter(line + 1);
    }
};

window.navigateMonacoToPosition = function (elementId, line, column, length) {
    const editor = window.monacoEditors[elementId];
    if (editor) {
        // Convert from 0-based to 1-based (Monaco uses 1-based indexing)
        const lineNumber = line + 1;
        const columnNumber = column + 1;
        const errorLength = length || 1;
        
        // Set cursor position to the start of the error
        editor.setPosition({ lineNumber: lineNumber, column: columnNumber });
        
        // Reveal the position in the center of the viewport
        editor.revealLineInCenter(lineNumber);
        
        // Highlight the error range by setting a selection
        // This makes it easier to see exactly where the error is
        editor.setSelection({
            startLineNumber: lineNumber,
            startColumn: columnNumber,
            endLineNumber: lineNumber,
            endColumn: columnNumber + errorLength - 1
        });
        
        // Focus the editor to ensure it's visible
        editor.focus();
    }
};

window.getMonacoSelection = function (elementId) {
    const editor = window.monacoEditors[elementId];
    if (!editor) return null;
    
    const selection = editor.getSelection();
    if (!selection) return null;
    
    return {
        startLine: selection.startLineNumber - 1,
        startColumn: selection.startColumn - 1,
        endLine: selection.endLineNumber - 1,
        endColumn: selection.endColumn - 1
    };
};

window.getMonacoSelectedText = function (elementId) {
    const editor = window.monacoEditors[elementId];
    if (!editor) return "";
    
    const selection = editor.getSelection();
    if (!selection || selection.isEmpty()) return "";
    
    return editor.getModel().getValueInRange(selection);
};

window.insertMonacoText = function (elementId, text, line, column) {
    const editor = window.monacoEditors[elementId];
    if (!editor) return;
    
    const position = {
        lineNumber: line + 1,
        column: column + 1
    };
    
    editor.executeEdits("insert", [{
        range: {
            startLineNumber: line + 1,
            startColumn: column + 1,
            endLineNumber: line + 1,
            endColumn: column + 1
        },
        text: text
    }]);
    
    editor.setPosition(position);
};

window.replaceMonacoRange = function (elementId, startLine, startCol, endLine, endCol, text) {
    const editor = window.monacoEditors[elementId];
    if (!editor) return;
    
    editor.executeEdits("replace", [{
        range: {
            startLineNumber: startLine + 1,
            startColumn: startCol + 1,
            endLineNumber: endLine + 1,
            endColumn: endCol + 1
        },
        text: text
    }]);
};