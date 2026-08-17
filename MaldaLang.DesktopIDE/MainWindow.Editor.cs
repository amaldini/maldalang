// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.UserControls;
using MaldaLang.IDE;
using MaldaLang.IDE.Services;
using MaldaLang.IDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Compiler;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using System.Xml;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Markup;
using Markdig;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.ComponentModel;
using Microsoft.Web.WebView2.Core;
using MaldaLang.BuiltIns;
using MaldaLang.TraceViewer;
using MaldaLang.UIHost;
using MaldaLang.Testing;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{

    private void SetupSyntaxHighlighting()
    {
        // Create a simple syntax highlighting definition
        UpdateSyntaxHighlighting();
    }

    private IHighlightingDefinition CreateHighlightingDefinition()
    {
        // Use C# highlighting as a base (similar syntax)
        // We'll create a simple custom one
        var highlighting = HighlightingManager.Instance.GetDefinition("C#");
        if (highlighting == null)
        {
            // Fallback: create a basic highlighting
            return HighlightingManager.Instance.GetDefinitionByExtension(".cs") ?? 
                   HighlightingManager.Instance.GetDefinition("C++");
        }
        return highlighting;
    }

    
    private void UpdateSyntaxHighlighting()
    {
        var highlightingDefinition = CreateHighlightingDefinition();
        if (highlightingDefinition == null) return;
        
        var theme = _themeService.CurrentTheme;
        var isDarkTheme = theme.Name == "Dark" || theme.Name == "HighContrast";
        
        if (isDarkTheme)
        {
            // For dark themes, modify colors to be light/bright for visibility
            // Set specific colors for common syntax elements in dark theme
            SetHighlightingColor(highlightingDefinition, "Keyword", Colors.LightBlue);
            SetHighlightingColor(highlightingDefinition, "String", Colors.LightGreen);
            SetHighlightingColor(highlightingDefinition, "Comment", Colors.LightGray);
            SetHighlightingColor(highlightingDefinition, "Number", Colors.LightYellow);
            SetHighlightingColor(highlightingDefinition, "Type", Colors.LightCyan);
            SetHighlightingColor(highlightingDefinition, "Method", Colors.LightPink);
            SetHighlightingColor(highlightingDefinition, "Property", Colors.LightSalmon);
            SetHighlightingColor(highlightingDefinition, "Class name", Colors.LightCoral);
            SetHighlightingColor(highlightingDefinition, "Interface name", Colors.LightSteelBlue);
            SetHighlightingColor(highlightingDefinition, "Preprocessor", Colors.LightGoldenrodYellow);
            
            // Also update any other colors that might be dark
            foreach (var namedColor in highlightingDefinition.NamedHighlightingColors)
            {
                var color = namedColor.Foreground?.GetColor(null);
                if (color.HasValue)
                {
                    var originalColor = color.Value;
                    var brightness = (originalColor.R + originalColor.G + originalColor.B) / 3.0;
                    
                    // If the color is dark (low brightness), make it light
                    if (brightness < 100)
                    {
                        var lightColor = Color.FromRgb(
                            (byte)Math.Min(255, originalColor.R + 180),
                            (byte)Math.Min(255, originalColor.G + 180),
                            (byte)Math.Min(255, originalColor.B + 180)
                        );
                        namedColor.Foreground = new SimpleHighlightingBrush(lightColor);
                    }
                }
            }
        }
        else
        {
            // For light themes, use standard colors (darker for contrast)
            SetHighlightingColor(highlightingDefinition, "Keyword", Colors.Blue);
            SetHighlightingColor(highlightingDefinition, "String", Colors.DarkGreen);
            SetHighlightingColor(highlightingDefinition, "Comment", Colors.Green);
            SetHighlightingColor(highlightingDefinition, "Number", Colors.DarkRed);
            SetHighlightingColor(highlightingDefinition, "Type", Colors.Purple);
            SetHighlightingColor(highlightingDefinition, "Method", Colors.DarkBlue);
            SetHighlightingColor(highlightingDefinition, "Property", Colors.DarkCyan);
            SetHighlightingColor(highlightingDefinition, "Class name", Colors.DarkMagenta);
            SetHighlightingColor(highlightingDefinition, "Interface name", Colors.DarkBlue);
            SetHighlightingColor(highlightingDefinition, "Preprocessor", Colors.DarkGray);
        }
        
        CodeEditor.SyntaxHighlighting = highlightingDefinition;
    }

    
    private void SetHighlightingColor(IHighlightingDefinition definition, string name, Color color)
    {
        var namedColor = definition.NamedHighlightingColors.FirstOrDefault(nc => nc.Name == name);
        if (namedColor != null)
        {
            namedColor.Foreground = new SimpleHighlightingBrush(color);
        }
    }

    private void SetupEditor()
    {
        CodeEditor.TextChanged += (s, e) =>
        {
            SaveEditorIntoActiveDocument();
            _diagnosticsTimer?.Stop();
            _diagnosticsTimer?.Start();
            
            // Update AI chat panel context (always update, not just when visible)
            if (_aiChatPanel != null)
            {
                _aiChatPanel.CurrentCode = GetCurrentCode();
            }
        };
        
        // Handle document changes to adjust breakpoints
        CodeEditor.Document.Changed += (s, e) =>
        {
            var activeDocument = GetActiveDocument();
            var fileName = GetPhysicalPath(activeDocument) ?? "main.malda";
            
            // In AvalonEdit, DocumentChangeEventArgs provides properties directly, not a Changes collection
            // Calculate line numbers (0-based)
            // Note: The document has already been modified when this event fires.
            // The e.Offset is the offset in the ORIGINAL document where the change occurred.
            // To get the correct line number, we need to account for the fact that if text was
            // inserted before this offset, the line numbers have shifted. However, since the
            // change starts at this offset, getting the line at this offset in the modified
            // document should give us the correct line number for where the change occurred.
            // This works because: if we insert at offset X, line at X in modified doc is where we inserted.
            // If we delete starting at offset X, line at X in modified doc is where deletion started.
            var line = CodeEditor.Document.GetLineByOffset(e.Offset);
            int startLine = line.LineNumber; // AvalonEdit is 1-based; DebuggerService stores 1-based
            if (IsVirtualDocument(activeDocument))
            {
                startLine = VirtualDocumentCoordinateMapper.ToPhysicalLine(startLine, activeDocument.VirtualStartLine);
            }
            
            // Count lines in removed and inserted text by counting newlines
            // Each newline character marks the end of a line, so:
            // - N newlines in removed text = N lines removed
            // - N newlines in inserted text = N lines added
            // However, if removed text has newlines but doesn't end with one, we're also
            // removing part of the next line, so we add 1
            int oldLineCount = 0;
            if (e.RemovedText != null && e.RemovalLength > 0)
            {
                var removedText = e.RemovedText.Text ?? "";
                int newlineCount = removedText.Count(c => c == '\n');
                // If we have newlines but the text doesn't end with a newline,
                // we're removing newlineCount lines plus part of the next line
                if (newlineCount > 0 && !removedText.EndsWith("\n"))
                {
                    oldLineCount = newlineCount + 1;
                }
                else
                {
                    oldLineCount = newlineCount;
                }
            }
            
            int newLineCount = 0;
            if (e.InsertedText != null && e.InsertionLength > 0)
            {
                var insertedText = e.InsertedText.Text ?? "";
                int newlineCount = insertedText.Count(c => c == '\n');
                // Similar logic: if we insert text with newlines that doesn't end with newline,
                // we're adding newlineCount lines plus part of a line
                if (newlineCount > 0 && !insertedText.EndsWith("\n"))
                {
                    newLineCount = newlineCount + 1;
                }
                else
                {
                    newLineCount = newlineCount;
                }
            }
            
            int delta = newLineCount - oldLineCount;
            
            if (delta != 0)
            {
                _debuggerService.AdjustBreakpointsForLineChange(fileName, startLine, delta);
            }
        };
        
        CodeEditor.Options.EnableHyperlinks = false;
        CodeEditor.Options.EnableEmailHyperlinks = false;
        CodeEditor.Options.ShowSpaces = false;
        CodeEditor.Options.ShowTabs = false;
        CodeEditor.Options.ConvertTabsToSpaces = true;
        CodeEditor.Options.IndentationSize = 4;
        
        // Current-line debug highlight
        _currentLineRenderer = new CurrentLineBackgroundRenderer(CodeEditor);
        CodeEditor.TextArea.TextView.BackgroundRenderers.Add(_currentLineRenderer);

        // Search results highlight
        _searchResultsRenderer = new SearchResultsBackgroundRenderer(CodeEditor);
        CodeEditor.TextArea.TextView.BackgroundRenderers.Add(_searchResultsRenderer);
        _diagnosticRenderer = new DiagnosticSquiggleRenderer(CodeEditor);
        CodeEditor.TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);
        _documentHighlightRenderer = new SearchResultsBackgroundRenderer(
            CodeEditor,
            Color.FromArgb(56, 120, 170, 230),
            Color.FromArgb(90, 90, 150, 220),
            Color.FromArgb(90, 70, 120, 190),
            Color.FromArgb(140, 50, 100, 180));
        CodeEditor.TextArea.TextView.BackgroundRenderers.Add(_documentHighlightRenderer);
        _documentHighlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _documentHighlightTimer.Tick += (_, _) =>
        {
            _documentHighlightTimer.Stop();
            UpdateDocumentHighlights();
        };
        
        // Enable glyph margin for breakpoint indicators
        CodeEditor.TextArea.LeftMargins.Insert(0, new BreakpointMargin(CodeEditor.TextArea, this));
        
        // Add autocomplete support
        CodeEditor.TextArea.TextEntered += TextArea_TextEntered;
        CodeEditor.TextArea.KeyDown += TextArea_KeyDown;
        CodeEditor.TextArea.TextView.MouseHover += TextArea_MouseHover;
        CodeEditor.TextArea.TextView.MouseHoverStopped += TextArea_MouseHoverStopped;
        
        // Track cursor position and selection for AI chat panel
        CodeEditor.TextArea.Caret.PositionChanged += (s, e) =>
        {
            if (_activeTab == "ai" && _aiChatPanel != null)
            {
                _aiChatPanel.CursorPosition = GetCursorPosition();
                _aiChatPanel.SelectedCode = GetSelectedText();
            }

            UpdateSignatureHelp();
            ScheduleDocumentHighlightRefresh();
        };
        
        CodeEditor.TextArea.SelectionChanged += (s, e) =>
        {
            if (_activeTab == "ai" && _aiChatPanel != null)
            {
                _aiChatPanel.SelectedCode = GetSelectedText();
            }
        };
    }

    
    private void TextArea_TextEntered(object? sender, TextCompositionEventArgs e)
    {
        // Trigger autocomplete on typing letters, numbers, or certain characters
        // Include @ for decorator support
        if (e.Text != null && e.Text.Length > 0 && 
            (char.IsLetterOrDigit(e.Text[0]) || e.Text == "." || e.Text == "(" || e.Text == "@"))
        {
            ShowCompletion();
        }

        UpdateSignatureHelp();
    }

    
    private void TextArea_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Space to manually trigger autocomplete
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ShowCompletion();
        }
        // Close completion and signature help on Escape
        else if (e.Key == Key.Escape)
        {
            if (_completionWindow != null)
            {
                _completionWindow.Close();
                _completionWindow = null;
            }

            CloseSignatureHelp();
        }
    }

    
    private void ShowCompletion()
    {
        // Close existing completion window if open
        if (_completionWindow != null)
        {
            _completionWindow.Close();
            _completionWindow = null;
        }
        
        var textArea = CodeEditor.TextArea;
        var caret = textArea.Caret;
        var document = CodeEditor.Document;
        var line = document.GetLineByNumber(caret.Line);
        
        // Find the start of the current word by looking backwards from the caret
        // Include @ character for decorator support
        int wordStart = caret.Offset;
        while (wordStart > line.Offset)
        {
            char c = document.GetCharAt(wordStart - 1);
            if (char.IsLetterOrDigit(c) || c == '_' || c == '@')
            {
                wordStart--;
            }
            else
            {
                break;
            }
        }
        
        // Extract the current prefix
        string prefix = document.GetText(wordStart, caret.Offset - wordStart);
        
        // Get completions from language service
        var source = CodeEditor.Text;
        var completions = _languageService.GetCompletions(source, caret.Line - 1, caret.Column - 1);
        
        // Check if we're in decorator context (prefix starts with @)
        bool isDecoratorContext = prefix.StartsWith("@");
        
        // For decorator context, language service already filtered the completions
        // So we should use all returned completions without additional filtering
        // For other contexts, filter with the full prefix
        List<CompletionItem> filteredCompletions;
        if (isDecoratorContext)
        {
            // Language service already filtered decorators, use all returned completions
            filteredCompletions = completions;
        }
        else
        {
            // Filter completions based on the current prefix (case-insensitive)
            filteredCompletions = completions
                .Where(c => prefix == "" || c.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        
        if (filteredCompletions.Count == 0)
            return;
        
        // Create completion window with the correct start offset
        _completionWindow = new CompletionWindow(textArea);
        var data = _completionWindow.CompletionList.CompletionData;
        
        foreach (var item in filteredCompletions)
        {
            var insertText = item.InsertText ?? item.Label;
            var description = item.Detail ?? item.Label;
            // Use insertText for both Text and Content so the correct text is inserted
            data.Add(new SimpleCompletionData(insertText, insertText, description));
        }
        
        // Set the start offset so the completion window knows what to replace
        _completionWindow.StartOffset = wordStart;
        
        if (data.Count > 0)
        {
            _completionWindow.CompletionList.SelectedItem = data[0];
            _completionWindow.Show();
            _completionWindow.Closed += (s, e) => _completionWindow = null;
        }
        else
        {
            _completionWindow.Close();
            _completionWindow = null;
        }
    }

    private void UpdateSignatureHelp()
    {
        var caret = CodeEditor.TextArea.Caret;
        var source = CodeEditor.Text;
        var help = _languageService.GetSignatureHelp(source, caret.Line - 1, caret.Column - 1);
        if (help == null)
        {
            CloseSignatureHelp();
            return;
        }

        if (_signatureHelpWindow == null || _signatureHelpProvider == null)
        {
            _signatureHelpProvider = new SignatureHelpOverloadProvider();
            _signatureHelpProvider.SetSignature(help);

            _signatureHelpWindow = new OverloadInsightWindow(CodeEditor.TextArea)
            {
                Provider = _signatureHelpProvider
            };
            _signatureHelpWindow.Closed += (s, e) =>
            {
                _signatureHelpWindow = null;
                _signatureHelpProvider = null;
            };
            _signatureHelpWindow.Show();
            return;
        }

        _signatureHelpProvider.SetSignature(help);
    }

    private void CloseSignatureHelp()
    {
        if (_signatureHelpWindow != null)
        {
            _signatureHelpWindow.Close();
            _signatureHelpWindow = null;
            _signatureHelpProvider = null;
        }
    }

    private void TextArea_MouseHover(object? sender, MouseEventArgs e)
    {
        try
        {
            var position = CodeEditor.GetPositionFromPoint(e.GetPosition(CodeEditor));
            if (position == null)
            {
                CloseHoverToolTip();
                return;
            }

            var activeDocument = GetActiveDocument();
            var sourceFileName = string.IsNullOrWhiteSpace(activeDocument.PhysicalFilePath)
                ? activeDocument.FilePath
                : activeDocument.PhysicalFilePath;
            var hover = _languageService.GetHoverInformation(
                CodeEditor.Text,
                position.Value.Line - 1,
                position.Value.Column - 1,
                sourceFileName,
                CancellationToken.None);

            int? offset = null;
            try
            {
                offset = CodeEditor.Document.GetOffset(position.Value.Line, position.Value.Column);
            }
            catch
            {
                // Caret can sit past the end of a short line.
            }

            var diagnosticHit = offset is int hitOffset ? _diagnosticRenderer?.HitTest(hitOffset) : null;
            if (diagnosticHit != null)
            {
                hover = string.IsNullOrWhiteSpace(hover)
                    ? diagnosticHit.Message
                    : diagnosticHit.Message + System.Environment.NewLine + hover;
            }

            if (string.IsNullOrWhiteSpace(hover))
            {
                CloseHoverToolTip();
                return;
            }

            CloseHoverToolTip();
            _hoverToolTip = new ToolTip
            {
                Content = new TextBlock
                {
                    Text = NormalizeHoverText(hover),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420,
                    Margin = new Thickness(4)
                },
                Placement = PlacementMode.Mouse,
                StaysOpen = true,
                IsOpen = true
            };
        }
        catch
        {
            CloseHoverToolTip();
        }
    }

    private void TextArea_MouseHoverStopped(object? sender, MouseEventArgs e)
    {
        CloseHoverToolTip();
    }

    private void CloseHoverToolTip()
    {
        if (_hoverToolTip != null)
        {
            _hoverToolTip.IsOpen = false;
            _hoverToolTip = null;
        }
    }

    private static string NormalizeHoverText(string hover)
    {
        return hover
            .Replace("```malda", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private void InitializeSyntaxPanel()
    {
        _syntaxSnippets.Clear();
        _syntaxSnippets.AddRange(CreateDefaultSyntaxSnippets());

        SyntaxCategoryComboBox.Items.Clear();
        SyntaxCategoryComboBox.Items.Add("All");
        foreach (var category in _syntaxSnippets.Select(s => s.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c))
        {
            SyntaxCategoryComboBox.Items.Add(category);
        }

        SyntaxCategoryComboBox.SelectedIndex = 0;
        RefreshSyntaxSnippetList();
        RefreshOutline();
        UpdateSyntaxPanelVisibility();
    }

    private void RefreshOutline()
    {
        if (OutlineTreeView == null)
        {
            return;
        }

        var sourceKey = GetCurrentSourceKey();
        _outlineSymbols = _symbolNavigationService.GetDocumentSymbols(CodeEditor.Text ?? string.Empty, sourceKey);
        PopulateOutlineTree();
    }

    private void PopulateOutlineTree()
    {
        if (OutlineTreeView == null)
        {
            return;
        }

        OutlineTreeView.Items.Clear();
        var query = (OutlineSearchTextBox.Text ?? string.Empty).Trim();
        var nodes = BuildOutlineNodes(_outlineSymbols, query);
        foreach (var node in nodes)
        {
            OutlineTreeView.Items.Add(CreateOutlineTreeNode(node));
        }
    }

    private List<OutlineNodeItem> BuildOutlineNodes(IEnumerable<DocumentSymbolInfo> symbols, string query)
    {
        var nodes = new List<OutlineNodeItem>();
        foreach (var symbol in symbols)
        {
            var children = BuildOutlineNodes(symbol.Children, query);
            var matches = string.IsNullOrWhiteSpace(query) ||
                symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(symbol.Detail) && symbol.Detail.Contains(query, StringComparison.OrdinalIgnoreCase));

            if (!matches && children.Count == 0)
            {
                continue;
            }

            nodes.Add(new OutlineNodeItem
            {
                DisplayText = BuildOutlineDisplayText(symbol),
                Symbol = symbol,
                Children = children
            });
        }

        return nodes;
    }

    private static string BuildOutlineDisplayText(DocumentSymbolInfo symbol)
    {
        return string.IsNullOrWhiteSpace(symbol.Detail)
            ? symbol.Name
            : $"{symbol.Name} - {symbol.Detail}";
    }

    private TreeViewItem CreateOutlineTreeNode(OutlineNodeItem node)
    {
        var treeNode = new TreeViewItem
        {
            Header = node.DisplayText,
            Tag = node.Symbol,
            IsExpanded = true
        };

        foreach (var child in node.Children)
        {
            treeNode.Items.Add(CreateOutlineTreeNode(child));
        }

        return treeNode;
    }

    private static List<SyntaxSnippet> CreateDefaultSyntaxSnippets()
    {
        return new List<SyntaxSnippet>
        {
            CreateSnippet(
                id: "class",
                category: "OOP",
                label: "Class",
                description: "Define a class with constructor and method.",
                templateText: $"class ClassName {{\n\tvar value;\n\n\tfunction ClassName(value) {{\n\t\tthis.value = {SnippetCaretMarker}value;\n\t}}\n\n\tfunction methodName() {{\n\t\t\n\t}}\n}}"),
            CreateSnippet(
                id: "function",
                category: "Declarations",
                label: "Function",
                description: "Define a reusable function.",
                templateText: $"function functionName(param1, param2) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "prompt",
                category: "Declarations",
                label: "Prompt",
                description: "Define an AI prompt function.",
                templateText: $"prompt promptName(input) -> OutputType {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "if",
                category: "Statements",
                label: "If",
                description: "Conditional block.",
                templateText: $"if (condition) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "if-else",
                category: "Statements",
                label: "If / Else",
                description: "Two-way conditional block.",
                templateText: $"if (condition) {{\n\t{SnippetCaretMarker}\n}} else {{\n\t\n}}"),
            CreateSnippet(
                id: "while",
                category: "Loops",
                label: "While Loop",
                description: "Repeat while condition is true.",
                templateText: $"while (condition) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "for-in",
                category: "Loops",
                label: "For-In Loop",
                description: "Iterate items in a collection.",
                templateText: $"for (var item in collection) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "foreach",
                category: "Loops",
                label: "Foreach Loop",
                description: "Alternative foreach syntax over a collection.",
                templateText: $"foreach (var item in collection) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "lambda-expression",
                category: "Functional",
                label: "Lambda Expression",
                description: "Inline function with expression body.",
                templateText: $"var transform = (x) => {SnippetCaretMarker}x;"),
            CreateSnippet(
                id: "lambda-block",
                category: "Functional",
                label: "Lambda Block",
                description: "Inline function with block body.",
                templateText: $"var transform = (x) => {{\n\t{SnippetCaretMarker}\n}};"),
            CreateSnippet(
                id: "var-declaration",
                category: "Basics",
                label: "Variable Declaration",
                description: "Declare and initialize a variable.",
                templateText: $"var variableName = {SnippetCaretMarker}value;"),
            CreateSnippet(
                id: "new-instance",
                category: "OOP",
                label: "Create Object",
                description: "Instantiate a class with constructor args.",
                templateText: $"var instance = new ClassName({SnippetCaretMarker}value);"),
            CreateSnippet(
                id: "new-instance-no-args",
                category: "OOP",
                label: "Create Object (No Args)",
                description: "Instantiate a class with a parameterless constructor.",
                templateText: $"var instance = new {SnippetCaretMarker}ClassName();"),
            CreateSnippet(
                id: "method-call",
                category: "OOP",
                label: "Method Call",
                description: "Call an instance method.",
                templateText: $"var result = instance.{SnippetCaretMarker}methodName();"),
            CreateSnippet(
                id: "field-access",
                category: "OOP",
                label: "Field Access",
                description: "Read or write an instance field.",
                templateText: $"instance.{SnippetCaretMarker}value = 42;"),
            CreateSnippet(
                id: "try-catch",
                category: "Statements",
                label: "Try / Catch",
                description: "Handle runtime errors.",
                templateText: $"try {{\n\t{SnippetCaretMarker}\n}} catch (err) {{\n\tprint(err);\n}}"),
            CreateSnippet(
                id: "for-classic",
                category: "Loops",
                label: "For Loop (Classic)",
                description: "Traditional counter-based loop.",
                templateText: $"for (var i = 0; i < count; i = i + 1) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "break",
                category: "Statements",
                label: "Break",
                description: "Exit current loop.",
                templateText: $"{SnippetCaretMarker}break;"),
            CreateSnippet(
                id: "continue",
                category: "Statements",
                label: "Continue",
                description: "Skip to next loop iteration.",
                templateText: $"{SnippetCaretMarker}continue;"),
            CreateSnippet(
                id: "return",
                category: "Statements",
                label: "Return",
                description: "Return from function.",
                templateText: $"return {SnippetCaretMarker}value;"),
            CreateSnippet(
                id: "array-literal",
                category: "Basics",
                label: "Array",
                description: "Create an array literal.",
                templateText: $"var items = [{SnippetCaretMarker}1, 2, 3];"),
            CreateSnippet(
                id: "object-literal",
                category: "Basics",
                label: "Object",
                description: "Create an object literal.",
                templateText: $"var obj = {{\n\t\"name\": {SnippetCaretMarker}\"value\"\n}};"),
            CreateSnippet(
                id: "string-interpolation",
                category: "Strings",
                label: "String Interpolation",
                description: "Interpolate variables with $\"...{expr}...\".",
                templateText: "var name = \"Alice\";\nvar total = 42;\nvar message = $\"Hello {name}, total={total}\";\nprint(" + SnippetCaretMarker + "message);"),
            CreateSnippet(
                id: "multiline-string",
                category: "Strings",
                label: "Multiline String",
                description: "Triple-quoted multiline string literal.",
                templateText: "var text = \"\"\"\n" + SnippetCaretMarker + "Line 1\nLine 2\n\"\"\";\nprint(text);"),
            CreateSnippet(
                id: "multiline-interpolated-string",
                category: "Strings",
                label: "Multiline Interpolated String",
                description: "Interpolated triple-quoted string: $\"\"\"...\"\"\".",
                templateText: "var name = \"MALDA\";\nvar greeting = $\"\"\"\nHello {name}!\n" + SnippetCaretMarker + "Welcome to multiline interpolation.\n\"\"\";\nprint(greeting);"),
            CreateSnippet(
                id: "match-array-rest-pattern",
                category: "Pattern Matching",
                label: "Match Array + Rest Pattern",
                description: "Advanced match with nested object pattern and ...rest.",
                templateText: "var data = [{ type: \"A\", value: 1 }, { type: \"B\", value: 2 }];\nvar result = match data {\n\tcase [{ type: \"A\", value: v }, ...rest]: \"first=\" + v;\n\tdefault: \"none\";\n};\nprint(" + SnippetCaretMarker + "result);"),
            CreateSnippet(
                id: "match-object-pattern",
                category: "Pattern Matching",
                label: "Match Object Pattern",
                description: "Advanced match with object shorthand and wildcard.",
                templateText: "var profile = { name: \"Alice\", age: 30, city: \"Rome\" };\nvar result = match profile {\n\tcase { name, age }: name + \" is \" + age;\n\tcase { name }: name;\n\tcase _: \"unknown\";\n};\nprint(" + SnippetCaretMarker + "result);"),
            CreateSnippet(
                id: "match-variant-pattern",
                category: "Pattern Matching",
                label: "Match Variant Pattern",
                description: "Sum-type variant matching with constructor patterns.",
                templateText: "type Result = Ok(value) | Err(message);\nvar r = Ok(42);\nvar result = match r {\n\tcase Ok(v): \"ok: \" + v;\n\tcase Err(msg): \"error: \" + msg;\n};\nprint(" + SnippetCaretMarker + "result);"),
            CreateSnippet(
                id: "actor",
                category: "Actors",
                label: "Actor",
                description: "Define an actor with a reply handler.",
                templateText: $"actor Worker {{\n\tfunction start() {{\n\t\tprint(\"Worker started\");\n\t}}\n\n\tfunction compute(value) {{\n\t\t{SnippetCaretMarker}reply(value * 2);\n\t}}\n}}"),
            CreateSnippet(
                id: "spawn",
                category: "Actors",
                label: "Spawn Actor",
                description: "Create a new actor instance.",
                templateText: $"var worker = spawn {SnippetCaretMarker}Worker();"),
            CreateSnippet(
                id: "send",
                category: "Actors",
                label: "Send Message",
                description: "Send async message to actor.",
                templateText: $"send {SnippetCaretMarker}worker.start();"),
            CreateSnippet(
                id: "send-then-timeout",
                category: "Actors",
                label: "Send With Callback + Timeout",
                description: "Handle actor response with timeout fallback.",
                templateText: $"send worker.compute(42) then (result) {{\n\t{SnippetCaretMarker}print(result);\n}} timeout 500 catch (error) {{\n\tprint(error);\n}}"),
            CreateSnippet(
                id: "using",
                category: "Declarations",
                label: "Using",
                description: "Import a package or namespace.",
                templateText: $"using {SnippetCaretMarker}Package.Name;"),
            CreateSnippet(
                id: "include",
                category: "Declarations",
                label: "Include",
                description: "Include another MALDA file.",
                templateText: $"include {SnippetCaretMarker}\"shared.malda\";"),
            CreateSnippet(
                id: "comment-block",
                category: "Utilities",
                label: "Comment Block",
                description: "Insert a multi-line comment block.",
                templateText: $"/*\n\t{SnippetCaretMarker}Notes\n*/"),
            CreateSnippet(
                id: "print",
                category: "Utilities",
                label: "Print",
                description: "Print a value to output.",
                templateText: $"print({SnippetCaretMarker}\"text\");"),
            CreateSnippet(
                id: "sleep",
                category: "Utilities",
                label: "Sleep",
                description: "Pause execution for milliseconds.",
                templateText: $"sleep({SnippetCaretMarker}100);")
        };
    }

    private static SyntaxSnippet CreateSnippet(string id, string category, string label, string description, string templateText)
    {
        return new SyntaxSnippet
        {
            Id = id,
            Category = category,
            Label = label,
            Description = description,
            TemplateText = templateText,
            Preview = templateText.Replace(SnippetCaretMarker, "")
        };
    }

    private void RefreshSyntaxSnippetList()
    {
        var query = (SyntaxSearchTextBox.Text ?? string.Empty).Trim();
        var selectedCategory = SyntaxCategoryComboBox.SelectedItem as string ?? "All";

        var filtered = _syntaxSnippets
            .Where(s => selectedCategory == "All" || string.Equals(s.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
            .Where(s =>
                string.IsNullOrWhiteSpace(query) ||
                s.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.TemplateText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Label)
            .ToList();

        SyntaxSnippetsListBox.ItemsSource = filtered;
        if (filtered.Count > 0)
        {
            SyntaxSnippetsListBox.SelectedIndex = 0;
        }
        else
        {
            InsertSyntaxButton.IsEnabled = false;
        }
    }

    private void InsertSelectedSyntaxSnippet()
    {
        if (SyntaxSnippetsListBox.SelectedItem is not SyntaxSnippet snippet || CodeEditor.Document == null)
        {
            return;
        }

        int insertStartOffset;
        var selection = CodeEditor.TextArea.Selection;
        if (selection != null && !selection.IsEmpty)
        {
            insertStartOffset = selection.SurroundingSegment.Offset;
        }
        else
        {
            insertStartOffset = CodeEditor.TextArea.Caret.Offset;
        }

        var (snippetText, markerOffset) = FormatSnippetForInsertion(snippet.TemplateText, insertStartOffset);
        if (selection != null && !selection.IsEmpty)
        {
            CodeEditor.Document.Replace(selection.SurroundingSegment, snippetText);
        }
        else
        {
            CodeEditor.Document.Insert(insertStartOffset, snippetText);
        }

        var caretOffset = markerOffset >= 0 ? insertStartOffset + markerOffset : insertStartOffset + snippetText.Length;
        caretOffset = Math.Max(0, Math.Min(caretOffset, CodeEditor.Document.TextLength));
        CodeEditor.TextArea.Caret.Offset = caretOffset;
        CodeEditor.Focus();
    }

    private (string Text, int CaretOffset) FormatSnippetForInsertion(string templateText, int insertOffset)
    {
        var normalized = templateText.Replace("\r\n", "\n").Replace("\r", "\n");
        var linePrefix = GetIndentationPrefixAtOffset(insertOffset);
        var lines = normalized.Split('\n');

        if (!string.IsNullOrEmpty(linePrefix) && lines.Length > 1)
        {
            for (int i = 1; i < lines.Length; i++)
            {
                lines[i] = linePrefix + lines[i];
            }
            normalized = string.Join("\n", lines);
        }

        var markerOffset = normalized.IndexOf(SnippetCaretMarker, StringComparison.Ordinal);
        if (markerOffset >= 0)
        {
            normalized = normalized.Remove(markerOffset, SnippetCaretMarker.Length);
        }

        return (normalized, markerOffset);
    }

    private string GetIndentationPrefixAtOffset(int offset)
    {
        if (CodeEditor.Document == null || offset < 0 || offset > CodeEditor.Document.TextLength)
        {
            return string.Empty;
        }

        var line = CodeEditor.Document.GetLineByOffset(offset);
        var beforeOffsetLength = Math.Max(0, offset - line.Offset);
        var beforeOffset = CodeEditor.Document.GetText(line.Offset, beforeOffsetLength);
        if (string.IsNullOrEmpty(beforeOffset) || beforeOffset.Any(c => !char.IsWhiteSpace(c)))
        {
            return string.Empty;
        }

        return beforeOffset;
    }

    private void UpdateSyntaxPanelVisibility()
    {
        if (_isSyntaxPanelVisible)
        {
            SyntaxPanel.Visibility = Visibility.Visible;
            SyntaxPanelSplitter.Visibility = Visibility.Visible;
            SyntaxPanelColumn.Width = _syntaxPanelPreviousWidth.Value > 0 ? _syntaxPanelPreviousWidth : new GridLength(280, GridUnitType.Pixel);
        }
        else
        {
            if (SyntaxPanelColumn.Width.Value > 0)
            {
                _syntaxPanelPreviousWidth = SyntaxPanelColumn.Width;
            }
            SyntaxPanel.Visibility = Visibility.Collapsed;
            SyntaxPanelSplitter.Visibility = Visibility.Collapsed;
            SyntaxPanelColumn.Width = new GridLength(0);
        }
    }

    private void SyntaxSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshSyntaxSnippetList();
    }

    private void SyntaxCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSyntaxSnippetList();
    }

    private void OutlineSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PopulateOutlineTree();
    }

    private void SyntaxSnippetsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        InsertSyntaxButton.IsEnabled = SyntaxSnippetsListBox.SelectedItem is SyntaxSnippet;
    }

    private void OutlineTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (OutlineTreeView.SelectedItem is TreeViewItem { Tag: DocumentSymbolInfo symbol })
        {
            NavigateToLocation(_activeDocumentKey, symbol.Span.Line, symbol.Span.Column, Math.Max(1, symbol.Span.Length));
        }
    }

    private void SyntaxSnippetsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        InsertSelectedSyntaxSnippet();
    }

    private void InsertSyntaxButton_Click(object sender, RoutedEventArgs e)
    {
        InsertSelectedSyntaxSnippet();
    }

    private void SetupDiagnostics()
    {
        _diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _diagnosticsTimer.Tick += (s, e) =>
        {
            _diagnosticsTimer.Stop();
            UpdateDiagnostics();
        };
    }

    private void UpdateDiagnostics()
    {
        var activeDocument = GetActiveDocument();
        var (source, sourceKey) = GetSourceForAnalysis(activeDocument);
        var diagnostics = _languageService.GetDiagnostics(
            source,
            sourceKey,
            strictTypesOptions: _typeAnalysisSettingsService.ToOptions());
        if (IsVirtualDocument(activeDocument))
        {
            diagnostics = _editorDiagnosticsService.FilterForVirtualSection(
                diagnostics,
                activeDocument.VirtualStartLine,
                activeDocument.VirtualEndLine);
        }

        UpdateErrorsPanel(diagnostics);
        UpdateDiagnosticSquiggles(diagnostics);
        RefreshOutline();
    }

    private void UpdateDiagnosticSquiggles(List<Diagnostic> diagnostics)
    {
        if (_diagnosticRenderer == null)
        {
            return;
        }

        var spans = _editorDiagnosticsService.ToSpans(
            diagnostics,
            (int line, int column, out int offset) =>
                TryGetOffsetFromLocation(CodeEditor.Document, line, column, out offset));
        _diagnosticRenderer.SetDiagnostics(spans);
        CodeEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void UpdateErrorsPanel(List<Diagnostic> diagnostics)
    {
        ErrorsListBox.Items.Clear();
        foreach (var diagnostic in diagnostics)
        {
            // Add Diagnostic objects directly - the DataTemplate will handle the display
            ErrorsListBox.Items.Add(diagnostic);
        }
    }

    private void NavigateGoToDefinition_Click(object sender, RoutedEventArgs e)
    {
        GoToDefinitionAtCaret();
    }

    private void NavigateFindReferences_Click(object sender, RoutedEventArgs e)
    {
        FindReferencesAtCaret();
    }

    private void NavigateRenameSymbol_Click(object sender, RoutedEventArgs e)
    {
        RenameSymbolAtCaret();
    }

    private void GoToDefinitionAtCaret()
    {
        SaveEditorIntoActiveDocument();
        var (line, column) = GetCursorPosition();
        var sourceKey = GetCurrentSourceKey();
        var workspaceDocuments = CollectWorkspaceDocuments();
        var definition = workspaceDocuments.Count > 1
            ? _symbolNavigationService.GetWorkspaceDefinition(workspaceDocuments, CodeEditor.Text, line, column, sourceKey)
            : _symbolNavigationService.GetDefinition(CodeEditor.Text, line, column, sourceKey);
        if (definition == null)
        {
            MessageBox.Show("No definition found at the current cursor position.", "Go to Definition", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        NavigateToWorkspaceLocation(definition);
    }

    private void FindReferencesAtCaret()
    {
        SaveEditorIntoActiveDocument();
        var (line, column) = GetCursorPosition();
        var sourceKey = GetCurrentSourceKey();
        var target = _symbolNavigationService.PrepareRename(CodeEditor.Text, line, column, sourceKey);
        if (target == null)
        {
            MessageBox.Show("Place the cursor on a symbol to find its references.", "Find References", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var workspaceDocuments = CollectWorkspaceDocuments();
        var references = workspaceDocuments.Count > 1
            ? _symbolNavigationService.GetWorkspaceReferences(workspaceDocuments, CodeEditor.Text, line, column, sourceKey)
            : _symbolNavigationService.GetReferences(CodeEditor.Text, line, column, sourceKey);
        _searchResults.Clear();
        _searchResultNodes.Clear();
        _currentSearchResultIndex = -1;

        var fileCount = 0;
        foreach (var group in references.GroupBy(reference => reference.SourceKey ?? sourceKey, StringComparer.OrdinalIgnoreCase))
        {
            var text = TryGetWorkspaceDocumentText(group.Key, workspaceDocuments);
            if (text == null)
            {
                continue;
            }

            fileCount++;
            var document = new TextDocument(text);
            foreach (var reference in group)
            {
                if (!TryGetOffsetFromLocation(document, reference.Span.Line, reference.Span.Column, out var offset))
                {
                    continue;
                }

                var location = document.GetLocation(offset);
                _searchResults.Add(new SearchResultItem
                {
                    DocumentKey = group.Key,
                    Offset = offset,
                    Length = Math.Max(1, reference.Span.Length),
                    Line = location.Line,
                    Column = location.Column,
                    Preview = BuildSearchPreview(document, location.Line)
                });
            }
        }

        PopulateSearchTree();
        SwitchToTab("search");
        SearchSummaryTextBlock.Text = _searchResults.Count == 0
            ? $"No references found for '{target.Name}'."
            : $"Found {_searchResults.Count} reference{(_searchResults.Count == 1 ? string.Empty : "s")} for '{target.Name}' in {fileCount} file{(fileCount == 1 ? string.Empty : "s")}.";

        if (_searchResults.Count > 0)
        {
            _currentSearchResultIndex = 0;
            SelectSearchResultNode(_searchResults[0], navigateWhenMissingNode: true);
        }
        else
        {
            UpdateSearchHighlightsForActiveDocument();
        }
    }

    private void RenameSymbolAtCaret()
    {
        SaveEditorIntoActiveDocument();
        var (line, column) = GetCursorPosition();
        var sourceKey = GetCurrentSourceKey();
        var target = _symbolNavigationService.PrepareRename(CodeEditor.Text, line, column, sourceKey);
        if (target == null)
        {
            MessageBox.Show("Place the cursor on a symbol to rename it.", "Rename Symbol", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newName = PromptForSymbolName(target.Name);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, target.Name, StringComparison.Ordinal))
        {
            return;
        }

        var workspaceDocuments = CollectWorkspaceDocuments();
        var workspaceEdits = workspaceDocuments.Count > 1
            ? _symbolNavigationService.RenameWorkspaceSymbol(workspaceDocuments, CodeEditor.Text, line, column, newName, sourceKey)
            : null;
        if (workspaceEdits != null && workspaceEdits.Count > 0)
        {
            var fileCount = ApplyWorkspaceTextEdits(workspaceEdits, workspaceDocuments);
            UpdateDiagnostics();
            RefreshOutline();
            MessageBox.Show(
                $"Renamed '{target.Name}' to '{newName}' in {fileCount} file{(fileCount == 1 ? string.Empty : "s")}.",
                "Rename Symbol",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var edits = _symbolNavigationService.Rename(CodeEditor.Text, line, column, newName, sourceKey);
        if (edits == null || edits.Count == 0)
        {
            MessageBox.Show("Rename could not be applied at the current cursor position.", "Rename Symbol", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplyTextEdits(edits);
        SaveEditorIntoActiveDocument();
        UpdateDiagnostics();
        RefreshOutline();
        MessageBox.Show($"Renamed '{target.Name}' to '{newName}' in the current document.", "Rename Symbol", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ApplyTextEdits(List<TextEditInfo> edits)
    {
        if (CodeEditor.Document == null)
        {
            return;
        }

        var orderedEdits = edits
            .OrderByDescending(edit => edit.Span.Line)
            .ThenByDescending(edit => edit.Span.Column)
            .ToList();

        foreach (var edit in orderedEdits)
        {
            if (!TryGetOffsetFromLocation(CodeEditor.Document, edit.Span.Line, edit.Span.Column, out var startOffset))
            {
                continue;
            }

            var maxLength = Math.Max(0, CodeEditor.Document.TextLength - startOffset);
            var replaceLength = Math.Min(edit.Span.Length, maxLength);
            CodeEditor.Document.Replace(startOffset, replaceLength, edit.NewText);
        }
    }

    private List<WorkspaceDocumentInfo> CollectWorkspaceDocuments()
    {
        var seenPhysical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in _openDocuments.Values)
        {
            var path = GetPhysicalPath(document);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                continue;
            }

            if (!seenPhysical.Add(path))
            {
                continue;
            }

            var text = HasVirtualFamily(path)
                ? RecomposeVirtualFamily(document)
                : document.Content;
            _workspaceFiles.SetOpenDocument(path, text);
        }

        var seed = GetPhysicalPath(GetActiveDocument());
        if (!string.IsNullOrWhiteSpace(seed) && File.Exists(seed))
        {
            return _workspaceFiles.GetDocumentsFor(seed).ToList();
        }

        return _workspaceFiles.GetDocuments().ToList();
    }

    private bool HasVirtualFamily(string physicalPath)
    {
        return _openDocuments.Values.Any(document =>
            IsVirtualDocument(document) &&
            string.Equals(GetPhysicalPath(document), physicalPath, StringComparison.OrdinalIgnoreCase));
    }

    private string? TryGetWorkspaceDocumentText(string sourceKey, IReadOnlyList<WorkspaceDocumentInfo> documents)
    {
        var match = documents.FirstOrDefault(document =>
            string.Equals(document.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            return match.Text;
        }

        if (File.Exists(sourceKey))
        {
            try
            {
                return File.ReadAllText(sourceKey);
            }
            catch
            {
                return null;
            }
        }

        if (string.Equals(sourceKey, _activeDocumentKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceKey, GetCurrentSourceKey(), StringComparison.OrdinalIgnoreCase))
        {
            return CodeEditor.Text;
        }

        return null;
    }

    private void NavigateToWorkspaceLocation(SymbolLocation location)
    {
        var sourceKey = location.SourceKey;
        if (string.IsNullOrWhiteSpace(sourceKey) ||
            string.Equals(sourceKey, _activeDocumentKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceKey, GetCurrentSourceKey(), StringComparison.OrdinalIgnoreCase))
        {
            NavigateToLocation(_activeDocumentKey, location.Span.Line, location.Span.Column, Math.Max(1, location.Span.Length));
            return;
        }

        var documentKey = ResolveDocumentKeyForLocation(sourceKey, location.Span.Line);
        if (documentKey == null)
        {
            MessageBox.Show($"Could not open '{sourceKey}'.", "Go to Definition", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var line = location.Span.Line;
        if (_openDocuments.TryGetValue(documentKey, out var document) && IsVirtualDocument(document))
        {
            line = Math.Max(0, location.Span.Line - document.VirtualStartLine);
        }

        NavigateToLocation(documentKey, line, location.Span.Column, Math.Max(1, location.Span.Length));
    }

    private string? ResolveDocumentKeyForLocation(string sourceKey, int zeroBasedLine)
    {
        if (_openDocuments.ContainsKey(sourceKey))
        {
            return sourceKey;
        }

        string fullPath;
        try
        {
            if (!File.Exists(sourceKey))
            {
                return null;
            }

            fullPath = Path.GetFullPath(sourceKey);
        }
        catch
        {
            return null;
        }

        var virtualMatch = _documentOrder
            .Select(key => (Key: key, Document: _openDocuments[key]))
            .Where(item => IsVirtualDocument(item.Document) &&
                           string.Equals(GetPhysicalPath(item.Document), fullPath, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(item =>
                zeroBasedLine >= item.Document.VirtualStartLine &&
                zeroBasedLine <= item.Document.VirtualEndLine);
        if (virtualMatch.Key != null)
        {
            return virtualMatch.Key;
        }

        var physicalOpen = _documentOrder.FirstOrDefault(key =>
            _openDocuments.TryGetValue(key, out var document) &&
            !IsVirtualDocument(document) &&
            string.Equals(GetPhysicalPath(document), fullPath, StringComparison.OrdinalIgnoreCase));
        if (physicalOpen != null)
        {
            return physicalOpen;
        }

        OpenFileAndIncludedDocuments(fullPath);
        return ResolveDocumentKeyForLocation(fullPath, zeroBasedLine) ?? GetDocumentKey(fullPath);
    }

    private int ApplyWorkspaceTextEdits(List<WorkspaceTextEditInfo> edits, IReadOnlyList<WorkspaceDocumentInfo> documents)
    {
        var fileCount = 0;
        foreach (var group in edits.GroupBy(edit => edit.SourceKey, StringComparer.OrdinalIgnoreCase))
        {
            var path = group.Key;
            var fileEdits = group
                .Select(edit => new TextEditInfo { Span = edit.Span, NewText = edit.NewText })
                .ToList();

            var original = TryGetWorkspaceDocumentText(path, documents) ?? string.Empty;
            var updated = MaldaIndentFormatter.ApplyEdits(original, fileEdits);
            if (string.Equals(original, updated, StringComparison.Ordinal))
            {
                continue;
            }

            fileCount++;
            if (HasVirtualFamily(path) || _openDocuments.ContainsKey(path))
            {
                ApplyUpdatedPhysicalDocument(path, updated, markDirty: true);
            }
            else if (_documentOrder.Any(key =>
                         _openDocuments.TryGetValue(key, out var document) &&
                         string.Equals(GetPhysicalPath(document), path, StringComparison.OrdinalIgnoreCase)))
            {
                ApplyUpdatedPhysicalDocument(path, updated, markDirty: true);
            }
            else
            {
                try
                {
                    File.WriteAllText(path, updated);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not write '{path}': {ex.Message}", "Rename Symbol", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        if (_openDocuments.ContainsKey(_activeDocumentKey))
        {
            SyncEditorFromActiveDocument();
        }

        RefreshDocumentTabs();
        return fileCount;
    }

    private void ApplyUpdatedPhysicalDocument(string physicalPath, string updatedText, bool markDirty)
    {
        if (HasVirtualFamily(physicalPath))
        {
            RebuildVirtualTabsForPhysicalFile(physicalPath, updatedText);
            if (!markDirty)
            {
                return;
            }

            foreach (var document in _openDocuments.Values.Where(doc =>
                         string.Equals(GetPhysicalPath(doc), physicalPath, StringComparison.OrdinalIgnoreCase)))
            {
                document.IsDirty = true;
            }

            return;
        }

        var key = _documentOrder.FirstOrDefault(candidate =>
            _openDocuments.TryGetValue(candidate, out var document) &&
            string.Equals(GetPhysicalPath(document), physicalPath, StringComparison.OrdinalIgnoreCase))
            ?? GetDocumentKey(physicalPath);

        if (!_openDocuments.TryGetValue(key, out var openDocument))
        {
            openDocument = CreateDocument(physicalPath, updatedText);
            _openDocuments[key] = openDocument;
            if (!_documentOrder.Contains(key))
            {
                _documentOrder.Add(key);
            }
        }
        else
        {
            openDocument.Content = updatedText;
        }

        openDocument.IsDirty = markDirty || !string.Equals(openDocument.Content, openDocument.LastSavedContent, StringComparison.Ordinal);
        if (string.Equals(key, _activeDocumentKey, StringComparison.OrdinalIgnoreCase) && CodeEditor.Document != null)
        {
            _isSwitchingDocument = true;
            try
            {
                CodeEditor.Text = updatedText;
            }
            finally
            {
                _isSwitchingDocument = false;
            }
        }
    }

    private void FormatDocumentAtCaret()
    {
        SaveEditorIntoActiveDocument();
        var formatted = MaldaIndentFormatter.FormatDocument(CodeEditor.Text);
        if (string.Equals(formatted, CodeEditor.Text, StringComparison.Ordinal))
        {
            return;
        }

        CodeEditor.Text = formatted;
        SaveEditorIntoActiveDocument();
        UpdateDiagnostics();
        RefreshOutline();
        ScheduleDocumentHighlightRefresh();
    }

    private void GoToWorkspaceSymbol()
    {
        SaveEditorIntoActiveDocument();
        var documents = CollectWorkspaceDocuments();
        var symbols = _symbolNavigationService.GetWorkspaceSymbols(documents, null);
        if (symbols.Count == 0)
        {
            MessageBox.Show("No workspace symbols found.", "Go to Symbol", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Window
        {
            Title = "Go to Symbol",
            Width = 520,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        var filter = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        var list = new ListBox { DisplayMemberPath = "Display" };
        DockPanel.SetDock(filter, Dock.Top);
        root.Children.Add(filter);
        root.Children.Add(list);

        var items = symbols
            .Select(symbol => new WorkspaceSymbolPickItem
            {
                Display = string.IsNullOrWhiteSpace(symbol.ContainerName)
                    ? $"{symbol.Name}  —  {Path.GetFileName(symbol.Location.SourceKey)}"
                    : $"{symbol.ContainerName}.{symbol.Name}  —  {Path.GetFileName(symbol.Location.SourceKey)}",
                Symbol = symbol
            })
            .ToList();
        list.ItemsSource = items;

        filter.TextChanged += (_, _) =>
        {
            var query = filter.Text?.Trim() ?? string.Empty;
            list.ItemsSource = string.IsNullOrEmpty(query)
                ? items
                : items.Where(item => item.Display.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        };

        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem != null)
            {
                dialog.DialogResult = true;
            }
        };

        var choose = new Button { Content = "Go", MinWidth = 80, Margin = new Thickness(0, 8, 0, 0), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        choose.Click += (_, _) => dialog.DialogResult = true;
        DockPanel.SetDock(choose, Dock.Bottom);
        root.Children.Add(choose);
        dialog.Content = root;
        filter.Focus();

        if (dialog.ShowDialog() != true || list.SelectedItem == null)
        {
            return;
        }

        if (list.SelectedItem is WorkspaceSymbolPickItem selected)
        {
            NavigateToWorkspaceLocation(selected.Symbol.Location);
        }
    }

    private void ScheduleDocumentHighlightRefresh()
    {
        if (_documentHighlightTimer == null)
        {
            return;
        }

        _documentHighlightTimer.Stop();
        _documentHighlightTimer.Start();
    }

    private void UpdateDocumentHighlights()
    {
        if (_documentHighlightRenderer == null || CodeEditor.Document == null)
        {
            return;
        }

        try
        {
            var (line, column) = GetCursorPosition();
            var highlights = _symbolNavigationService.GetDocumentHighlights(
                CodeEditor.Text, line, column, GetCurrentSourceKey());
            var segments = new List<SearchMatchSegment>();
            foreach (var span in highlights)
            {
                if (!TryGetOffsetFromLocation(CodeEditor.Document, span.Line, span.Column, out var offset))
                {
                    continue;
                }

                segments.Add(new SearchMatchSegment { Offset = offset, Length = Math.Max(1, span.Length) });
            }

            _documentHighlightRenderer.SetMatches(segments, null);
            CodeEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        }
        catch
        {
            _documentHighlightRenderer.SetMatches(Array.Empty<SearchMatchSegment>(), null);
        }
    }

    private static bool TryGetOffsetFromLocation(TextDocument document, int zeroBasedLine, int zeroBasedColumn, out int offset)
    {
        offset = 0;
        var lineNumber = zeroBasedLine + 1;
        if (lineNumber <= 0 || lineNumber > document.LineCount)
        {
            return false;
        }

        try
        {
            var line = document.GetLineByNumber(lineNumber);
            offset = Math.Min(line.Offset + Math.Max(0, zeroBasedColumn), line.EndOffset);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string? PromptForSymbolName(string currentName)
    {
        var dialog = new Window
        {
            Title = "Rename Symbol",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = Brushes.White
        };

        var root = new Grid
        {
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = $"Rename '{currentName}' to:",
            Margin = new Thickness(0, 0, 0, 8)
        };

        var input = new TextBox
        {
            MinWidth = 280,
            Text = currentName,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okButton = new Button
        {
            Content = "Rename",
            MinWidth = 80,
            IsDefault = true
        };
        okButton.Click += (_, _) => dialog.DialogResult = true;

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80
        };
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        Grid.SetRow(label, 0);
        Grid.SetRow(input, 1);
        Grid.SetRow(buttons, 2);
        root.Children.Add(label);
        root.Children.Add(input);
        root.Children.Add(buttons);
        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void ErrorsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ErrorsListBox.SelectedItem is Diagnostic diagnostic)
        {
            NavigateToLocation(_activeDocumentKey, diagnostic.Line, diagnostic.Column, Math.Max(1, diagnostic.Length));
        }
    }

    // Autofix Methods
    private void AutofixErrorsButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyAllAutofixes();
    }

    private void ErrorFixButton_Click(object sender, RoutedEventArgs e)
    {
        // Find the Diagnostic associated with this button
        if (sender is Button button && button.Tag is Diagnostic diagnostic)
        {
            ApplySingleAutofix(diagnostic);
        }
    }

    private void ApplyAllAutofixes()
    {
        try
        {
            // Get all diagnostics with autofix suggestions
            var activeDocument = GetActiveDocument();
            var (source, sourceKey) = GetSourceForAnalysis(activeDocument);
            var diagnostics = _languageService.GetDiagnostics(
                source,
                sourceKey,
                strictTypesOptions: _typeAnalysisSettingsService.ToOptions());
            var autofixableDiagnostics = diagnostics
                .Where(d => d.AutoFix != null && d.AutoFix.IsSimpleCharacterFix)
                .ToList();

            if (autofixableDiagnostics.Count == 0)
            {
                MessageBox.Show("No autofixable errors found.", "Autofix", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Sort by position (reverse order to avoid offset issues when inserting)
            autofixableDiagnostics.Sort((a, b) =>
            {
                var lineCompare = b.Line.CompareTo(a.Line);
                if (lineCompare != 0) return lineCompare;
                return b.Column.CompareTo(a.Column);
            });

            // Apply all autofixes
            var fixedCount = 0;
            foreach (var diagnostic in autofixableDiagnostics)
            {
                var autofix = diagnostic.AutoFix;
                if (autofix == null) continue;

                try
                {
                    if (ApplyAutofixInternal(autofix))
                    {
                        fixedCount++;
                    }
                }
                catch
                {
                    // Skip if autofix fails (invalid position, etc.)
                    continue;
                }
            }

            // Update diagnostics
            UpdateDiagnostics();

            // Show results
            if (fixedCount > 0)
            {
                MessageBox.Show($"Successfully fixed {fixedCount} error{(fixedCount > 1 ? "s" : string.Empty)}.",
                    "Autofix Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error applying autofixes: {ex.Message}", "Autofix Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplySingleAutofix(Diagnostic diagnostic)
    {
        try
        {
            var autofix = diagnostic.AutoFix;
            if (autofix == null) return;

            if (ApplyAutofixInternal(autofix))
            {
                // Update diagnostics after successful fix
                UpdateDiagnostics();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error applying autofix: {ex.Message}", "Autofix Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ApplyAutofixInternal(AutoFixInfo autofix)
    {
        if (CodeEditor?.Document == null) return false;

        try
        {
            // Convert 0-based line to 1-based (AvalonEdit uses 1-based)
            var lineNumber = autofix.Line + 1;

            // Get the document line
            if (lineNumber <= 0 || lineNumber > CodeEditor.Document.LineCount)
            {
                return false;
            }

            var documentLine = CodeEditor.Document.GetLineByNumber(lineNumber);
            if (documentLine == null) return false;

            // Calculate start and end offsets
            // Column is 0-based, add to line offset
            var startOffset = documentLine.Offset + autofix.Column;
            var endOffset = startOffset + autofix.LengthToReplace;

            // Validate offsets
            if (startOffset < documentLine.Offset || endOffset > documentLine.EndOffset)
            {
                return false;
            }

            // Apply the fix
            CodeEditor.Document.Replace(startOffset, endOffset - startOffset, autofix.TextToInsert);
            return true;
        }
        catch
        {
            return false;
        }
    }

    
    private string GetCurrentCode() => CodeEditor.Text;

    
    private (int Line, int Column) GetCursorPosition()
    {
        var line = CodeEditor.TextArea.Caret.Line; // 1-based
        var column = CodeEditor.TextArea.Caret.Column; // 1-based
        return (line - 1, column - 1); // Convert to 0-based
    }

    
    private string GetSelectedText()
    {
        var selection = CodeEditor.SelectedText;
        return selection ?? "";
    }

    
    // Edit Menu
    private void EditUndo_Click(object sender, RoutedEventArgs e)
    {
        if (CodeEditor.CanUndo)
        {
            CodeEditor.Undo();
        }
    }

    
    private void EditRedo_Click(object sender, RoutedEventArgs e)
    {
        if (CodeEditor.CanRedo)
        {
            CodeEditor.Redo();
        }
    }

    
    private void EditCut_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CodeEditor.SelectedText))
        {
            Clipboard.SetText(CodeEditor.SelectedText);
            var selection = CodeEditor.TextArea.Selection;
            if (selection != null && !selection.IsEmpty)
            {
                var startOffset = selection.SurroundingSegment.Offset;
                var length = selection.SurroundingSegment.Length;
                CodeEditor.Document.Replace(startOffset, length, "");
            }
        }
    }

    
    private void EditCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CodeEditor.SelectedText))
        {
            Clipboard.SetText(CodeEditor.SelectedText);
        }
    }

    
    private void EditPaste_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            var selection = CodeEditor.TextArea.Selection;
            if (selection != null && !selection.IsEmpty)
            {
                var startOffset = selection.SurroundingSegment.Offset;
                var length = selection.SurroundingSegment.Length;
                CodeEditor.Document.Replace(startOffset, length, text);
            }
            else
            {
                // If nothing is selected, insert at cursor position
                var offset = CodeEditor.TextArea.Caret.Offset;
                CodeEditor.Document.Insert(offset, text);
            }
        }
    }

    
    private void EditSelectAll_Click(object sender, RoutedEventArgs e)
    {
        CodeEditor.SelectAll();
    }

    
    private void EditFormatDocument_Click(object sender, RoutedEventArgs e)
    {
        FormatDocumentAtCaret();
    }

    private void NavigateGoToSymbol_Click(object sender, RoutedEventArgs e)
    {
        GoToWorkspaceSymbol();
    }

    private void ToggleSyntaxPanelVisibility()
    {
        _isSyntaxPanelVisible = !_isSyntaxPanelVisible;
        UpdateSyntaxPanelVisibility();
        UpdateViewMenuStates();

        if (_isSyntaxPanelVisible)
        {
            SyntaxSearchTextBox.Focus();
            SyntaxSearchTextBox.SelectAll();
        }
        else
        {
            CodeEditor.Focus();
        }
    }

    private void ViewToggleSyntaxPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        _isSyntaxPanelVisible = menuItem.IsChecked;
        UpdateSyntaxPanelVisibility();
        UpdateViewMenuStates();

        if (_isSyntaxPanelVisible)
        {
            SyntaxSearchTextBox.Focus();
            SyntaxSearchTextBox.SelectAll();
        }
        else
        {
            CodeEditor.Focus();
        }
    }
}

// Simple completion data implementation for AvalonEdit
public class SimpleCompletionData : ICompletionData
{
    public SimpleCompletionData(string text, string content, string description)
    {
        Text = text;
        Content = content;
        Description = description;
    }
    
    public string Text { get; }
    public object Content { get; }
    public object Description { get; }
    public double Priority => 0;
    public ImageSource? Image => null;
    
    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}

public class SignatureHelpOverloadProvider : IOverloadProvider
{
    private SignatureHelpInfo? _signature;
    private int _selectedIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value)
            {
                return;
            }

            _selectedIndex = value;
            NotifyChanged();
        }
    }

    public int Count => _signature == null ? 0 : 1;

    public object CurrentHeader => _signature?.SignatureLabel ?? string.Empty;

    public object CurrentContent
    {
        get
        {
            if (_signature == null || _signature.Parameters.Count == 0)
            {
                return string.Empty;
            }

            var active = Math.Clamp(_signature.ActiveParameter, 0, _signature.Parameters.Count - 1);
            return $"Parameter {active + 1}/{_signature.Parameters.Count}: {_signature.Parameters[active]}";
        }
    }

    public string CurrentIndexText
    {
        get
        {
            if (_signature == null || _signature.Parameters.Count == 0)
            {
                return string.Empty;
            }

            var active = Math.Clamp(_signature.ActiveParameter, 0, _signature.Parameters.Count - 1);
            return $"{active + 1} of {_signature.Parameters.Count}";
        }
    }

    public void SetSignature(SignatureHelpInfo signature)
    {
        _signature = signature;
        _selectedIndex = 0;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentHeader)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentContent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentIndexText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
    }
}
