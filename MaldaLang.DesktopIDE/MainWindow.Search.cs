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

    private void EditFind_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("search");

        var selectedText = CodeEditor.SelectedText;
        if (!string.IsNullOrWhiteSpace(selectedText) && !selectedText.Contains('\n'))
        {
            SearchTextBox.Text = selectedText;
        }

        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
        RunSearch();
    }

    private void EditReplace_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("search");
        var selectedText = CodeEditor.SelectedText;
        if (!string.IsNullOrWhiteSpace(selectedText) && !selectedText.Contains('\n'))
        {
            SearchTextBox.Text = selectedText;
        }

        ReplaceTextBox.Focus();
        ReplaceTextBox.SelectAll();
        RunSearch();
    }

    private void SearchFindAllButton_Click(object sender, RoutedEventArgs e)
    {
        RunSearch();
    }

    private void SearchPrevButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateSearchResults(-1);
    }

    private void SearchNextButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateSearchResults(1);
    }

    private void SearchReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        ReplaceCurrentSearchMatch();
    }

    private void SearchReplaceAllButton_Click(object sender, RoutedEventArgs e)
    {
        ReplaceAllSearchMatches();
    }

    private void ReplaceTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ReplaceCurrentSearchMatch();
        }
    }

    private void ReplaceCurrentSearchMatch()
    {
        SaveEditorIntoActiveDocument();
        if (_searchResults.Count == 0)
        {
            RunSearch();
        }

        if (_searchResults.Count == 0)
        {
            return;
        }

        if (_currentSearchResultIndex < 0)
        {
            _currentSearchResultIndex = 0;
        }

        var match = _searchResults[_currentSearchResultIndex];
        var replacement = ReplaceTextBox.Text ?? "";
        ApplyReplacementToDocument(match.DocumentKey, match.Offset, match.Length, replacement);
        RunSearch();
    }

    private void ReplaceAllSearchMatches()
    {
        SaveEditorIntoActiveDocument();
        if (_searchResults.Count == 0)
        {
            RunSearch();
        }

        if (_searchResults.Count == 0)
        {
            return;
        }

        var replacement = ReplaceTextBox.Text ?? "";
        foreach (var group in _searchResults.GroupBy(result => result.DocumentKey, StringComparer.OrdinalIgnoreCase))
        {
            if (!_openDocuments.TryGetValue(group.Key, out var document))
            {
                continue;
            }

            var matches = group
                .Select(result => new SearchMatch(result.Offset, result.Length))
                .ToList();
            document.Content = SearchReplaceService.ReplaceAll(document.Content, matches, replacement);
            document.IsDirty = !string.Equals(document.Content, document.LastSavedContent, StringComparison.Ordinal);
            if (string.Equals(group.Key, _activeDocumentKey, StringComparison.OrdinalIgnoreCase))
            {
                SetEditorTextUndoable(document.Content);
            }
        }

        UpdateDiagnostics();
        RefreshDocumentTabs();
        RunSearch();
    }

    private void ApplyReplacementToDocument(string documentKey, int offset, int length, string replacement)
    {
        if (!_openDocuments.TryGetValue(documentKey, out var document))
        {
            return;
        }

        document.Content = SearchReplaceService.ReplaceAt(document.Content, new SearchMatch(offset, length), replacement);
        document.IsDirty = !string.Equals(document.Content, document.LastSavedContent, StringComparison.Ordinal);
        if (string.Equals(documentKey, _activeDocumentKey, StringComparison.OrdinalIgnoreCase))
        {
            if (offset >= 0 && length >= 0 && offset + length <= CodeEditor.Document.TextLength)
            {
                CodeEditor.Document.Replace(offset, length, replacement);
            }
            else
            {
                SetEditorTextUndoable(document.Content);
            }
        }

        UpdateDiagnostics();
        RefreshDocumentTabs();
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            RunSearch();
        }
    }

    private void SearchResultsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (SearchResultsTreeView.SelectedItem is TreeViewItem { Tag: SearchResultItem selected })
        {
            NavigateToSearchResult(selected);
        }
    }

    private void RunSearch()
    {
        SaveEditorIntoActiveDocument();
        _searchResults.Clear();
        _searchResultNodes.Clear();
        _currentSearchResultIndex = -1;
        SearchResultsTreeView.Items.Clear();

        var query = SearchTextBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchSummaryTextBlock.Text = "Enter text to search.";
            UpdateSearchHighlightsForActiveDocument();
            return;
        }

        var useRegex = SearchRegexCheckBox.IsChecked == true;
        var matchCase = SearchMatchCaseCheckBox.IsChecked == true;
        var currentDocumentOnly = SearchCurrentDocumentOnlyCheckBox.IsChecked == true;
        Regex? regex = null;
        if (useRegex)
        {
            try
            {
                var regexOptions = RegexOptions.Compiled | RegexOptions.Multiline;
                if (!matchCase)
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                regex = new Regex(query, regexOptions);
            }
            catch (ArgumentException ex)
            {
                SearchSummaryTextBlock.Text = $"Invalid regex: {ex.Message}";
                UpdateSearchHighlightsForActiveDocument();
                return;
            }
        }

        IEnumerable<string> documentKeys = currentDocumentOnly
            ? new[] { _activeDocumentKey }
            : _documentOrder;

        foreach (var documentKey in documentKeys)
        {
            if (!_openDocuments.TryGetValue(documentKey, out var document))
            {
                continue;
            }

            var content = document.Content ?? string.Empty;
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            var matches = useRegex
                ? FindRegexMatches(documentKey, content, regex!)
                : FindTextMatches(documentKey, content, query, matchCase);

            _searchResults.AddRange(matches);
        }

        PopulateSearchTree();

        if (_searchResults.Count == 0)
        {
            SearchSummaryTextBlock.Text = "No matches found.";
            UpdateSearchHighlightsForActiveDocument();
            return;
        }

        var matchedDocumentCount = _searchResults.Select(r => r.DocumentKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var scopeText = currentDocumentOnly ? " (current document scope)" : string.Empty;
        SearchSummaryTextBlock.Text = $"Found {_searchResults.Count} match{(_searchResults.Count == 1 ? string.Empty : "es")} in {matchedDocumentCount} document{(matchedDocumentCount == 1 ? string.Empty : "s")}{scopeText}.";
        _currentSearchResultIndex = 0;
        SelectSearchResultNode(_searchResults[0], navigateWhenMissingNode: true);
        UpdateSearchHighlightsForActiveDocument();
    }

    private IEnumerable<SearchResultItem> FindTextMatches(string documentKey, string content, string query, bool matchCase)
    {
        var comparer = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var doc = new TextDocument(content);
        var index = 0;
        while (index < content.Length)
        {
            var foundIndex = content.IndexOf(query, index, comparer);
            if (foundIndex < 0)
            {
                yield break;
            }

            var location = doc.GetLocation(foundIndex);
            yield return new SearchResultItem
            {
                DocumentKey = documentKey,
                Offset = foundIndex,
                Length = query.Length,
                Line = location.Line,
                Column = location.Column,
                Preview = BuildSearchPreview(doc, location.Line)
            };

            index = foundIndex + Math.Max(1, query.Length);
        }
    }

    private static IEnumerable<SearchResultItem> FindRegexMatches(string documentKey, string content, Regex regex)
    {
        var doc = new TextDocument(content);
        foreach (Match match in regex.Matches(content))
        {
            if (!match.Success || match.Length <= 0)
            {
                continue;
            }

            var location = doc.GetLocation(match.Index);
            yield return new SearchResultItem
            {
                DocumentKey = documentKey,
                Offset = match.Index,
                Length = match.Length,
                Line = location.Line,
                Column = location.Column,
                Preview = BuildSearchPreview(doc, location.Line)
            };
        }
    }

    private static string BuildSearchPreview(TextDocument document, int lineNumber)
    {
        if (lineNumber <= 0 || lineNumber > document.LineCount)
        {
            return string.Empty;
        }

        var line = document.GetLineByNumber(lineNumber);
        var text = document.GetText(line).Trim();
        if (text.Length <= 120)
        {
            return text;
        }

        return text.Substring(0, 117) + "...";
    }

    private void PopulateSearchTree()
    {
        SearchResultsTreeView.Items.Clear();
        _searchResultNodes.Clear();

        foreach (var key in _documentOrder)
        {
            var resultsInDocument = _searchResults.Where(r => string.Equals(r.DocumentKey, key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (resultsInDocument.Count == 0)
            {
                continue;
            }

            if (!_openDocuments.TryGetValue(key, out var document))
            {
                continue;
            }

            var documentNode = new TreeViewItem
            {
                Header = $"{GetDocumentDisplayName(document)} ({resultsInDocument.Count})",
                IsExpanded = true
            };

            foreach (var match in resultsInDocument)
            {
                var matchNode = new TreeViewItem
                {
                    Header = $"Ln {match.Line}, Col {match.Column}: {match.Preview}",
                    Tag = match
                };
                _searchResultNodes[match] = matchNode;
                documentNode.Items.Add(matchNode);
            }

            SearchResultsTreeView.Items.Add(documentNode);
        }
    }

    private void NavigateSearchResults(int direction)
    {
        if (_searchResults.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
            {
                RunSearch();
            }
            return;
        }

        if (_currentSearchResultIndex < 0)
        {
            _currentSearchResultIndex = 0;
        }
        else
        {
            _currentSearchResultIndex = (_currentSearchResultIndex + direction + _searchResults.Count) % _searchResults.Count;
        }

        var selected = _searchResults[_currentSearchResultIndex];
        SelectSearchResultNode(selected, navigateWhenMissingNode: true);
    }

    private void SelectSearchResultNode(SearchResultItem result, bool navigateWhenMissingNode)
    {
        if (_searchResultNodes.TryGetValue(result, out var node))
        {
            if (node.Parent is TreeViewItem parentNode)
            {
                parentNode.IsExpanded = true;
            }

            node.IsSelected = true;
            node.BringIntoView();
        }
        else if (navigateWhenMissingNode)
        {
            NavigateToSearchResult(result);
        }
    }

    private void NavigateToSearchResult(SearchResultItem result)
    {
        var index = _searchResults.IndexOf(result);
        if (index >= 0)
        {
            _currentSearchResultIndex = index;
        }

        if (_openDocuments.ContainsKey(result.DocumentKey))
        {
            NavigateToOffset(result.DocumentKey, result.Offset, result.Length);
            return;
        }

        NavigateToWorkspaceLocation(new SymbolLocation
        {
            SourceKey = result.DocumentKey,
            Span = new TextSpanInfo
            {
                Line = Math.Max(0, result.Line - 1),
                Column = Math.Max(0, result.Column - 1),
                Length = result.Length
            }
        });
    }

    private void UpdateSearchHighlightsForActiveDocument()
    {
        if (_searchResultsRenderer == null)
        {
            return;
        }

        var segments = _searchResults
            .Where(r => string.Equals(r.DocumentKey, _activeDocumentKey, StringComparison.OrdinalIgnoreCase))
            .Select(r => new SearchMatchSegment { Offset = r.Offset, Length = r.Length })
            .ToList();

        int? activeOffset = null;
        if (_currentSearchResultIndex >= 0 && _currentSearchResultIndex < _searchResults.Count)
        {
            var current = _searchResults[_currentSearchResultIndex];
            if (string.Equals(current.DocumentKey, _activeDocumentKey, StringComparison.OrdinalIgnoreCase))
            {
                activeOffset = current.Offset;
            }
        }

        _searchResultsRenderer.SetMatches(segments, activeOffset);
        CodeEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }
}
