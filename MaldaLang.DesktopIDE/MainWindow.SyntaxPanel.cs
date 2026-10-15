// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.IDE.Models;
using System.Windows.Input;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private void InitializeSyntaxPanel()
    {
        _syntaxSnippets.Clear();
        _syntaxSnippets.AddRange(SyntaxSnippetCatalog.CreateDefault());

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
        if (IsSidebarMaximized)
        {
            return;
        }

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
