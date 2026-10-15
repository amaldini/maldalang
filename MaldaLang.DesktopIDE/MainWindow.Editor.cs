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
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using System.Xml;
using System.Windows.Threading;
using System.Windows.Input;
using System.Threading;
using System.Threading.Tasks;
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
    private DispatcherTimer? _completionTimer;
    private DispatcherTimer? _signatureHelpTimer;
    private CancellationTokenSource? _completionCts;
    private CancellationTokenSource? _signatureHelpCts;
    private CancellationTokenSource? _diagnosticsCts;
    private CancellationTokenSource? _highlightsCts;
    private CancellationTokenSource? _hoverCts;
    private int _diagnosticsRequestId;
    private int _highlightsRequestId;
    private int _signatureHelpRequestId;
    private int _hoverRequestId;
    private string? _pendingCompletionTrigger;
    private bool _pendingCompletionManual;

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
        var isDarkTheme = theme.IsDark;
        
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
        _completionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _completionTimer.Tick += (_, _) =>
        {
            _completionTimer.Stop();
            RunCompletionQuery(_pendingCompletionTrigger, _pendingCompletionManual);
        };
        _signatureHelpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _signatureHelpTimer.Tick += (_, _) =>
        {
            _signatureHelpTimer.Stop();
            RunSignatureHelpQuery();
        };
        
        // Enable glyph margin for breakpoint indicators and quick-fix lightbulbs
        CodeEditor.TextArea.LeftMargins.Insert(0, new BreakpointMargin(CodeEditor.TextArea, this));
        _quickFixMargin = new QuickFixMargin(CodeEditor.TextArea, this);
        CodeEditor.TextArea.LeftMargins.Insert(1, _quickFixMargin);
        
        // Add autocomplete support
        CodeEditor.TextArea.TextEntered += TextArea_TextEntered;
        CodeEditor.TextArea.KeyDown += TextArea_KeyDown;
        CodeEditor.TextArea.TextView.MouseHover += TextArea_MouseHover;
        CodeEditor.TextArea.TextView.MouseHoverStopped += TextArea_MouseHoverStopped;
        
        // Track cursor position and selection for AI chat panel
        CodeEditor.TextArea.Caret.PositionChanged += (s, e) =>
        {
            if (AIChatPanel != null && AIChatPanel.Visibility == Visibility.Visible && _aiChatPanel != null)
            {
                _aiChatPanel.CursorPosition = GetCursorPosition();
                _aiChatPanel.SelectedCode = GetSelectedText();
            }

            UpdateWindowChrome();

            ScheduleSignatureHelp();
            ScheduleDocumentHighlightRefresh();
        };
        
        CodeEditor.TextArea.SelectionChanged += (s, e) =>
        {
            if (AIChatPanel != null && AIChatPanel.Visibility == Visibility.Visible && _aiChatPanel != null)
            {
                _aiChatPanel.SelectedCode = GetSelectedText();
            }
        };

        // AvalonEdit's TextArea is the hit-test target, so the TextEditor ContextMenu
        // only appears after it is assigned here.
        if (CodeEditor.ContextMenu != null)
        {
            CodeEditor.TextArea.ContextMenu = CodeEditor.ContextMenu;
            CodeEditor.TextArea.PreviewMouseRightButtonDown += TextArea_PreviewMouseRightButtonDown;
            ApplyEditorContextMenuTheme(_themeService.CurrentTheme);
        }
    }

    private void TextArea_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = CodeEditor.GetPositionFromPoint(e.GetPosition(CodeEditor));
        if (position == null)
        {
            return;
        }

        var clickOffset = TryGetDocumentOffset(position.Value);
        var selection = CodeEditor.TextArea.Selection;
        var selectionStart = 0;
        var selectionLength = 0;
        if (selection != null && !selection.IsEmpty)
        {
            var segment = selection.SurroundingSegment;
            if (segment != null)
            {
                selectionStart = segment.Offset;
                selectionLength = segment.Length;
            }
        }

        if (clickOffset is int offset &&
            !EditorContextMenuPolicy.ShouldMoveCaretToClick(offset, selectionStart, selectionLength))
        {
            return;
        }

        CodeEditor.TextArea.Caret.Position = position.Value;
        CodeEditor.TextArea.ClearSelection();
    }

    private int? TryGetDocumentOffset(TextViewPosition position)
    {
        try
        {
            return CodeEditor.Document.GetOffset(position.Location);
        }
        catch
        {
            return null;
        }
    }

    private void EditorContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        var hasSelection = !string.IsNullOrEmpty(CodeEditor.SelectedText);
        var clipboardHasText = false;
        try
        {
            clipboardHasText = Clipboard.ContainsText();
        }
        catch
        {
            // Another process can lock the clipboard on Windows.
        }

        var (line, column) = GetCursorPosition();
        var sourceKey = GetCurrentSourceKey();
        var source = CodeEditor.Text;
        var hasRenameTarget = _symbolNavigationService.PrepareRename(source, line, column, sourceKey) != null;
        var hasDefinition = _symbolNavigationService.GetDefinition(source, line, column, sourceKey) != null;
        var spans = _diagnosticRenderer?.Spans ?? Array.Empty<EditorDiagnosticSpan>();
        var hasQuickFix = _editorQuickFixService.GetFixesAtCaret(spans, CodeEditor.CaretOffset, line).Count > 0;
        var state = EditorContextMenuPolicy.Resolve(new EditorContextMenuContext(
            HasSelection: hasSelection,
            ClipboardHasText: clipboardHasText,
            HasDefinition: hasDefinition,
            HasRenameTarget: hasRenameTarget,
            HasQuickFix: hasQuickFix));

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is EditorContextMenuCommand command)
            {
                item.IsEnabled = EditorContextMenuPolicy.IsCommandEnabled(command, state);
            }
        }
    }

    private void ApplyEditorContextMenuTheme(Theme theme)
    {
        var menu = CodeEditor.TextArea.ContextMenu ?? CodeEditor.ContextMenu;
        if (menu == null)
        {
            return;
        }

        EditorPopupTheming.Apply(menu, theme);
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
}
