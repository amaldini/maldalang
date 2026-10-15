// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.IDE.Models;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using System.Windows.Input;
using System.ComponentModel;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private void TextArea_TextEntered(object? sender, TextCompositionEventArgs e)
    {
        var trigger = e.Text;
        if (EditorIntelliSensePolicy.ShouldCloseExistingCompletion(trigger, manual: false))
        {
            CloseCompletionWindow();
        }

        if (EditorIntelliSensePolicy.ShouldQueryCompletions(trigger, _completionWindow != null, manual: false))
        {
            ScheduleCompletion(trigger, manual: false);
        }

        if (EditorIntelliSensePolicy.ShouldScheduleSignatureHelp(trigger, caretMoved: false))
        {
            ScheduleSignatureHelp();
        }
    }

    
    private void TextArea_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Space to manually trigger autocomplete
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ScheduleCompletion(triggerText: null, manual: true);
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

    
    private void ScheduleCompletion(string? triggerText, bool manual)
    {
        _pendingCompletionTrigger = triggerText;
        _pendingCompletionManual = manual;
        if (_completionTimer == null || EditorIntelliSensePolicy.IsImmediateCompletionQuery(manual, triggerText))
        {
            _completionTimer?.Stop();
            RunCompletionQuery(triggerText, manual);
            return;
        }

        _completionTimer.Stop();
        _completionTimer.Start();
    }

    private void RunCompletionQuery(string? triggerText, bool manual)
    {
        if (EditorIntelliSensePolicy.ShouldCloseExistingCompletion(triggerText, manual))
        {
            CloseCompletionWindow();
        }

        var textArea = CodeEditor.TextArea;
        var caret = textArea.Caret;
        var document = CodeEditor.Document;
        var line = document.GetLineByNumber(caret.Line);
        var wordStart = caret.Offset;
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

        var source = CodeEditor.Text;
        var caretLine = caret.Line;
        var caretColumn = caret.Column;
        var documentKey = _activeDocumentKey;
        var sourceFileName = GetCurrentSourceKey();

        _completionCts?.Cancel();
        _completionCts = new CancellationTokenSource();
        var token = _completionCts.Token;

        _ = QueryCompletionsAsync(source, caretLine, caretColumn, wordStart, documentKey, sourceFileName, token);
    }

    private async Task QueryCompletionsAsync(
        string source,
        int caretLine,
        int caretColumn,
        int wordStart,
        string documentKey,
        string? sourceFileName,
        CancellationToken token)
    {
        List<CompletionItem> completions;
        try
        {
            completions = await Task.Run(
                () => _languageService.GetCompletions(source, caretLine - 1, caretColumn - 1, sourceFileName, token),
                token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }

        if (token.IsCancellationRequested || _activeDocumentKey != documentKey || CodeEditor.Document == null)
        {
            return;
        }

        PresentCompletionWindow(completions, wordStart);
    }

    private void PresentCompletionWindow(List<CompletionItem> completions, int requestedWordStart)
    {
        var textArea = CodeEditor.TextArea;
        var caret = textArea.Caret;
        var document = CodeEditor.Document;
        var line = document.GetLineByNumber(caret.Line);
        var wordStart = caret.Offset;
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

        if (Math.Abs(wordStart - requestedWordStart) > 16)
        {
            return;
        }

        var prefix = document.GetText(wordStart, Math.Max(0, caret.Offset - wordStart));
        var isDecoratorContext = prefix.StartsWith('@');
        var filteredCompletions = isDecoratorContext
            ? completions
            : completions
                .Where(c => prefix.Length == 0 || c.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (filteredCompletions.Count == 0)
        {
            return;
        }

        CloseCompletionWindow();
        _completionWindow = new CompletionWindow(textArea);
        EditorPopupTheming.Apply(_completionWindow, _themeService.CurrentTheme);
        var data = _completionWindow.CompletionList.CompletionData;

        foreach (var item in filteredCompletions)
        {
            var insertText = item.InsertText ?? item.Label;
            var description = item.Detail ?? item.Label;
            data.Add(new SimpleCompletionData(insertText, insertText, description));
        }

        _completionWindow.StartOffset = wordStart;
        if (data.Count == 0)
        {
            CloseCompletionWindow();
            return;
        }

        _completionWindow.CompletionList.SelectedItem = data[0];
        _completionWindow.Show();
        _completionWindow.Closed += (_, _) => _completionWindow = null;
    }

    private void CloseCompletionWindow()
    {
        if (_completionWindow == null)
        {
            return;
        }

        _completionWindow.Close();
        _completionWindow = null;
    }

    private void ScheduleSignatureHelp()
    {
        if (_signatureHelpTimer == null)
        {
            RunSignatureHelpQuery();
            return;
        }

        _signatureHelpTimer.Stop();
        _signatureHelpTimer.Start();
    }

    private void RunSignatureHelpQuery()
    {
        var caret = CodeEditor.TextArea.Caret;
        var source = CodeEditor.Text;
        var caretLine = caret.Line;
        var caretColumn = caret.Column;
        var documentKey = _activeDocumentKey;
        var requestId = ++_signatureHelpRequestId;

        _signatureHelpCts?.Cancel();
        _signatureHelpCts = new CancellationTokenSource();
        var token = _signatureHelpCts.Token;

        _ = QuerySignatureHelpAsync(source, caretLine, caretColumn, documentKey, requestId, token);
    }

    private async Task QuerySignatureHelpAsync(
        string source,
        int caretLine,
        int caretColumn,
        string documentKey,
        int requestId,
        CancellationToken token)
    {
        SignatureHelpInfo? help;
        try
        {
            help = await Task.Run(
                () => _languageService.GetSignatureHelp(source, caretLine - 1, caretColumn - 1, token),
                token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            help = null;
        }

        if (token.IsCancellationRequested || requestId != _signatureHelpRequestId || _activeDocumentKey != documentKey)
        {
            return;
        }

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
            EditorPopupTheming.Apply(_signatureHelpWindow, _themeService.CurrentTheme);
            _signatureHelpWindow.Closed += (_, _) =>
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
            var requestId = ++_hoverRequestId;
            _hoverCts?.Cancel();
            _hoverCts = new CancellationTokenSource();
            var token = _hoverCts.Token;
            _ = QueryHoverAsync(
                CodeEditor.Text,
                position.Value.Line - 1,
                position.Value.Column - 1,
                sourceFileName,
                diagnosticHit,
                requestId,
                token);
        }
        catch
        {
            CloseHoverToolTip();
        }
    }

    private async Task QueryHoverAsync(
        string source,
        int line,
        int column,
        string? sourceFileName,
        EditorDiagnosticSpan? diagnosticHit,
        int requestId,
        CancellationToken token)
    {
        string? hover;
        try
        {
            hover = await Task.Run(
                () => _languageService.GetHoverInformation(source, line, column, sourceFileName, token),
                token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            hover = null;
        }

        if (token.IsCancellationRequested || requestId != _hoverRequestId)
        {
            return;
        }

        if (diagnosticHit != null)
        {
            hover = string.IsNullOrWhiteSpace(hover)
                ? diagnosticHit.Message
                : diagnosticHit.Message + System.Environment.NewLine + hover;
            if (diagnosticHit.AutoFix != null)
            {
                hover += System.Environment.NewLine + "Press Ctrl+. to apply: " + diagnosticHit.AutoFix.Description;
            }
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
        EditorPopupTheming.Apply(_hoverToolTip, _themeService.CurrentTheme);
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
