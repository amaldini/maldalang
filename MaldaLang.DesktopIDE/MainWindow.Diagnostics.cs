// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows.Threading;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
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
        var typeOptions = _typeAnalysisSettingsService.ToOptions();
        var isVirtual = IsVirtualDocument(activeDocument);
        var virtualStart = activeDocument.VirtualStartLine;
        var virtualEnd = activeDocument.VirtualEndLine;
        var documentKey = _activeDocumentKey;
        var requestId = ++_diagnosticsRequestId;

        _diagnosticsCts?.Cancel();
        _diagnosticsCts = new CancellationTokenSource();
        var token = _diagnosticsCts.Token;

        _ = QueryDiagnosticsAsync(
            source,
            sourceKey,
            typeOptions,
            isVirtual,
            virtualStart,
            virtualEnd,
            documentKey,
            requestId,
            token);
    }

    private async Task QueryDiagnosticsAsync(
        string source,
        string? sourceKey,
        StrictTypesOptions typeOptions,
        bool isVirtual,
        int virtualStart,
        int virtualEnd,
        string documentKey,
        int requestId,
        CancellationToken token)
    {
        List<Diagnostic> diagnostics;
        List<DocumentSymbolInfo> outlineSymbols;
        try
        {
            (diagnostics, outlineSymbols) = await Task.Run(() =>
            {
                // Diagnostics from an included file carry that file's lines; keep this document's only.
                var found = _languageService.GetDiagnostics(
                    source,
                    sourceKey,
                    token,
                    typeOptions)
                    .Where(MaldaLang.IDE.DiagnosticSourceFiles.IsInDocument)
                    .ToList();
                if (isVirtual)
                {
                    found = _editorDiagnosticsService.FilterForVirtualSection(
                        found,
                        virtualStart,
                        virtualEnd);
                }

                var symbols = _symbolNavigationService.GetDocumentSymbols(source, sourceKey, token);
                return (found, symbols);
            }, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }

        if (token.IsCancellationRequested || requestId != _diagnosticsRequestId || _activeDocumentKey != documentKey)
        {
            return;
        }

        UpdateErrorsPanel(diagnostics);
        UpdateDiagnosticSquiggles(diagnostics);
        _outlineSymbols = outlineSymbols;
        PopulateOutlineTree();
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
        _quickFixMargin?.InvalidateVisual();
    }

    private void UpdateErrorsPanel(List<Diagnostic> diagnostics)
    {
        ErrorsListBox.Items.Clear();
        foreach (var diagnostic in diagnostics)
        {
            ErrorsListBox.Items.Add(diagnostic);
        }

        var errors = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var warnings = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
        UpdateProblemsBadge(errors, warnings);
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
        if (sender is Button button && button.Tag is Diagnostic diagnostic)
        {
            ApplySingleAutofix(diagnostic);
        }
    }

    private void EditQuickFix_Click(object sender, RoutedEventArgs e)
    {
        ShowQuickFixesAtCaret();
    }

    public bool LineHasQuickFix(int editorOneBasedLine)
    {
        var spans = _diagnosticRenderer?.Spans ?? Array.Empty<EditorDiagnosticSpan>();
        return _editorQuickFixService.LineHasFix(spans, editorOneBasedLine - 1);
    }

    public void ShowQuickFixesForEditorLine(int editorOneBasedLine)
    {
        UpdateDiagnostics();
        var spans = _diagnosticRenderer?.Spans ?? Array.Empty<EditorDiagnosticSpan>();
        var fixes = _editorQuickFixService.GetFixesOnLine(spans, editorOneBasedLine - 1);
        ShowQuickFixMenu(fixes);
    }

    private void ShowQuickFixesAtCaret()
    {
        UpdateDiagnostics();
        var spans = _diagnosticRenderer?.Spans ?? Array.Empty<EditorDiagnosticSpan>();
        var caretOffset = CodeEditor.CaretOffset;
        var (line, _) = GetCursorPosition();
        var fixes = _editorQuickFixService.GetFixesAtCaret(spans, caretOffset, line);
        ShowQuickFixMenu(fixes);
    }

    private void ShowQuickFixMenu(IReadOnlyList<EditorDiagnosticSpan> fixes)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = CodeEditor.TextArea,
            Placement = PlacementMode.Relative
        };

        try
        {
            CodeEditor.TextArea.TextView.EnsureVisualLines();
            var caretVisual = CodeEditor.TextArea.TextView.GetVisualPosition(
                CodeEditor.TextArea.Caret.Position,
                VisualYPosition.TextBottom);
            menu.PlacementRectangle = new Rect(
                caretVisual.X,
                caretVisual.Y - CodeEditor.TextArea.TextView.VerticalOffset,
                0,
                0);
        }
        catch
        {
            menu.Placement = PlacementMode.MousePoint;
        }

        if (fixes.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "No quick fixes available",
                IsEnabled = false
            });
        }
        else
        {
            foreach (var span in fixes)
            {
                var autofix = span.AutoFix;
                if (autofix == null)
                {
                    continue;
                }

                var item = new MenuItem
                {
                    Header = autofix.Description,
                    Tag = autofix
                };
                item.Click += (_, _) => ApplyAutofix(autofix);
                menu.Items.Add(item);
            }
        }

        EditorPopupTheming.Apply(menu, _themeService.CurrentTheme);
        menu.IsOpen = true;
    }

    private void ApplyAllAutofixes()
    {
        try
        {
            var activeDocument = GetActiveDocument();
            var (source, sourceKey) = GetSourceForAnalysis(activeDocument);
            var diagnostics = _languageService.GetDiagnostics(
                    source,
                    sourceKey,
                    strictTypesOptions: _typeAnalysisSettingsService.ToOptions())
                .Where(MaldaLang.IDE.DiagnosticSourceFiles.IsInDocument)
                .ToList();
            if (IsVirtualDocument(activeDocument))
            {
                diagnostics = _editorDiagnosticsService.FilterForVirtualSection(
                    diagnostics,
                    activeDocument.VirtualStartLine,
                    activeDocument.VirtualEndLine);
            }

            var edits = _editorQuickFixService.ToBatchEdits(diagnostics);
            if (edits.Count == 0)
            {
                IdePromptWindow.Show("No autofixable errors found.", "Quick Fix", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fixedCount = EditorUndoableText.ApplyEdits(CodeEditor.Document, edits);
            UpdateDiagnostics();
            if (fixedCount > 0)
            {
                IdePromptWindow.Show(
                    $"Successfully fixed {fixedCount} error{(fixedCount > 1 ? "s" : string.Empty)}.",
                    "Quick Fix Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            IdePromptWindow.Show($"Error applying autofixes: {ex.Message}", "Quick Fix Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplySingleAutofix(Diagnostic diagnostic)
    {
        if (diagnostic.AutoFix == null)
        {
            return;
        }

        ApplyAutofix(diagnostic.AutoFix);
    }

    private void ApplyAutofix(AutoFixInfo autofix)
    {
        try
        {
            if (ApplyAutofixInternal(autofix))
            {
                UpdateDiagnostics();
            }
        }
        catch (Exception ex)
        {
            IdePromptWindow.Show($"Error applying autofix: {ex.Message}", "Quick Fix Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ApplyAutofixInternal(AutoFixInfo autofix)
    {
        if (CodeEditor?.Document == null)
        {
            return false;
        }

        return EditorUndoableText.ApplyEdits(CodeEditor.Document, new[] { EditorQuickFixService.ToEdit(autofix) }) > 0;
    }
}
