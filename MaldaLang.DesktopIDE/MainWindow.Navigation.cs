// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.Formatting;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
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
            IdePromptWindow.Show("No definition found at the current cursor position.", "Go to Definition", MessageBoxButton.OK, MessageBoxImage.Information);
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
            IdePromptWindow.Show("Place the cursor on a symbol to find its references.", "Find References", MessageBoxButton.OK, MessageBoxImage.Information);
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
            IdePromptWindow.Show("Place the cursor on a symbol to rename it.", "Rename Symbol", MessageBoxButton.OK, MessageBoxImage.Information);
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
            IdePromptWindow.Show(
                $"Renamed '{target.Name}' to '{newName}' in {fileCount} file{(fileCount == 1 ? string.Empty : "s")}.",
                "Rename Symbol",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var edits = _symbolNavigationService.Rename(CodeEditor.Text, line, column, newName, sourceKey);
        if (edits == null || edits.Count == 0)
        {
            IdePromptWindow.Show("Rename could not be applied at the current cursor position.", "Rename Symbol", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplyTextEdits(edits);
        SaveEditorIntoActiveDocument();
        UpdateDiagnostics();
        RefreshOutline();
        IdePromptWindow.Show($"Renamed '{target.Name}' to '{newName}' in the current document.", "Rename Symbol", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ApplyTextEdits(List<TextEditInfo> edits)
    {
        if (CodeEditor.Document == null)
        {
            return;
        }

        EditorUndoableText.ApplyEdits(CodeEditor.Document, edits);
    }

    /// <summary>
    /// Replaces the current editor buffer without clearing AvalonEdit's undo stack.
    /// Assigning <c>TextEditor.Text</c> calls <c>UndoStack.ClearAll()</c> in AvalonEdit 6.3.
    /// </summary>
    private void SetEditorTextUndoable(string newText)
    {
        if (CodeEditor.Document == null)
        {
            return;
        }

        var caret = CodeEditor.CaretOffset;
        EditorUndoableText.ReplaceAll(CodeEditor.Document, newText);
        CodeEditor.CaretOffset = Math.Min(Math.Max(0, caret), CodeEditor.Document.TextLength);
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
            IdePromptWindow.Show($"Could not open '{sourceKey}'.", "Go to Definition", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    IdePromptWindow.Show($"Could not write '{path}': {ex.Message}", "Rename Symbol", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        if (_openDocuments.ContainsKey(_activeDocumentKey))
        {
            SyncEditorFromActiveDocument(resetUndoStack: false);
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
                SetEditorTextUndoable(updatedText);
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
        // Same formatter as `malda fmt` and malda-lsp. A source the lexer rejects comes back unchanged.
        var result = MaldaFormatter.Format(CodeEditor.Text);
        var formatted = result.Text;
        if (!result.Success || string.Equals(formatted, CodeEditor.Text, StringComparison.Ordinal))
        {
            return;
        }

        SetEditorTextUndoable(formatted);
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
            IdePromptWindow.Show("No workspace symbols found.", "Go to Symbol", MessageBoxButton.OK, MessageBoxImage.Information);
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
        DialogTheming.Apply(dialog, _themeService.CurrentTheme);

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

        var (line, column) = GetCursorPosition();
        var source = CodeEditor.Text;
        var sourceKey = GetCurrentSourceKey();
        var documentKey = _activeDocumentKey;
        var requestId = ++_highlightsRequestId;

        _highlightsCts?.Cancel();
        _highlightsCts = new CancellationTokenSource();
        var token = _highlightsCts.Token;

        _ = QueryDocumentHighlightsAsync(source, line, column, sourceKey, documentKey, requestId, token);
    }

    private async Task QueryDocumentHighlightsAsync(
        string source,
        int line,
        int column,
        string? sourceKey,
        string documentKey,
        int requestId,
        CancellationToken token)
    {
        List<TextSpanInfo> highlights;
        try
        {
            highlights = await Task.Run(
                () => _symbolNavigationService.GetDocumentHighlights(source, line, column, sourceKey, token),
                token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            highlights = new List<TextSpanInfo>();
        }

        if (token.IsCancellationRequested ||
            requestId != _highlightsRequestId ||
            _activeDocumentKey != documentKey ||
            _documentHighlightRenderer == null ||
            CodeEditor.Document == null)
        {
            return;
        }

        try
        {
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
        => EditorUndoableText.TryGetOffsetFromLocation(document, zeroBasedLine, zeroBasedColumn, out offset);

    private string? PromptForSymbolName(string currentName)
    {
        var theme = _themeService.CurrentTheme;
        var dialog = new Window
        {
            Title = "Rename Symbol",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight
        };
        DialogTheming.Apply(dialog, theme);

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
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = new SolidColorBrush(theme.TextForeground)
        };

        var input = new TextBox
        {
            MinWidth = 280,
            Text = currentName,
            Margin = new Thickness(0, 0, 0, 12),
            Background = new SolidColorBrush(theme.InputBackground),
            Foreground = new SolidColorBrush(theme.InputForeground),
            BorderBrush = new SolidColorBrush(theme.InputBorder),
            CaretBrush = new SolidColorBrush(theme.InputForeground)
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
            IsDefault = true,
            Background = new SolidColorBrush(theme.PrimaryButtonBackground),
            Foreground = new SolidColorBrush(theme.PrimaryButtonForeground),
            BorderBrush = new SolidColorBrush(theme.PrimaryButtonBorder)
        };
        okButton.Click += (_, _) => dialog.DialogResult = true;

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(theme.ButtonBackground),
            Foreground = new SolidColorBrush(theme.ButtonForeground),
            BorderBrush = new SolidColorBrush(theme.ButtonBorder)
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
}
