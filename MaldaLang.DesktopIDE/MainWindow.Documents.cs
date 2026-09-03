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

    private void InitializeDocumentSystem()
    {
        _openDocuments.Clear();
        _documentOrder.Clear();
        _openDocuments[UntitledDocumentKey] = new OpenDocument
        {
            FilePath = null,
            PhysicalFilePath = null,
            Content = "",
            LastSavedContent = "",
            IsDirty = false
        };
        _documentOrder.Add(UntitledDocumentKey);
        _activeDocumentKey = UntitledDocumentKey;
        SyncEditorFromActiveDocument();
        RefreshDocumentTabs();
    }

    private static string GetDocumentKey(string? filePath, string? virtualTabId = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return UntitledDocumentKey;
        }

        var fullPath = Path.GetFullPath(filePath);
        if (string.IsNullOrWhiteSpace(virtualTabId))
        {
            return fullPath;
        }

        return $"{fullPath}{VirtualDocumentPrefix}{virtualTabId}";
    }

    private static string GetDocumentDisplayName(OpenDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.VirtualDisplayName))
        {
            var baseName = string.IsNullOrWhiteSpace(document.FilePath)
                ? "Untitled.malda"
                : Path.GetFileName(document.FilePath);
            return $"{baseName} :: {document.VirtualDisplayName}";
        }

        return string.IsNullOrWhiteSpace(document.FilePath) ? "Untitled.malda" : Path.GetFileName(document.FilePath);
    }

    private static bool IsVirtualDocument(OpenDocument document)
    {
        return !string.IsNullOrWhiteSpace(document.VirtualTabId) && !string.IsNullOrWhiteSpace(document.PhysicalFilePath);
    }

    private static string? GetPhysicalPath(OpenDocument document)
    {
        return string.IsNullOrWhiteSpace(document.PhysicalFilePath) ? document.FilePath : document.PhysicalFilePath;
    }

    private OpenDocument CreateDocument(string? filePath, string content)
    {
        return new OpenDocument
        {
            FilePath = filePath,
            PhysicalFilePath = filePath,
            Content = content,
            LastSavedContent = content,
            IsDirty = false
        };
    }

    private OpenDocument GetActiveDocument()
    {
        if (!_openDocuments.TryGetValue(_activeDocumentKey, out var document))
        {
            document = new OpenDocument
            {
                FilePath = null,
                PhysicalFilePath = null,
                Content = "",
                LastSavedContent = "",
                IsDirty = false
            };
            _openDocuments[_activeDocumentKey] = document;
            if (!_documentOrder.Contains(_activeDocumentKey))
            {
                _documentOrder.Add(_activeDocumentKey);
            }
        }

        return document;
    }

    private void SaveEditorIntoActiveDocument()
    {
        if (_isSwitchingDocument)
        {
            return;
        }

        var document = GetActiveDocument();
        document.Content = CodeEditor.Text;
        document.IsDirty = !string.Equals(document.Content, document.LastSavedContent, StringComparison.Ordinal);
        _fileService.SetFilePath(GetPhysicalPath(document));
        _fileService.SetContent(document.Content);
    }

    private void SyncEditorFromActiveDocument(bool resetUndoStack = true)
    {
        var document = GetActiveDocument();

        _isSwitchingDocument = true;
        try
        {
            if (CodeEditor.Text != document.Content)
            {
                if (resetUndoStack)
                {
                    // TextEditor.Text clears UndoStack — correct when loading a different buffer.
                    CodeEditor.Text = document.Content;
                }
                else
                {
                    SetEditorTextUndoable(document.Content);
                }
            }
        }
        finally
        {
            _isSwitchingDocument = false;
        }

        _fileService.SetFilePath(GetPhysicalPath(document));
        _fileService.SetContent(document.Content);
        UpdateBreakpointVisuals();
        UpdateDiagnostics();
        RefreshOutline();
        UpdateAIChatPanelContext();
        UpdateSearchHighlightsForActiveDocument();
    }

    private void RefreshDocumentTabs()
    {
        if (DocumentTabsPanel == null)
        {
            return;
        }

        DocumentTabsPanel.Children.Clear();
        var activeBrush = FindResource("TabActiveBackgroundBrush") as Brush ?? Brushes.White;
        var inactiveBrush = FindResource("TabBackgroundBrush") as Brush ?? Brushes.LightGray;
        var tabStyle = TryFindResource("DocumentTabButton") as Style;
        var closeStyle = TryFindResource("DocumentTabCloseButton") as Style;

        foreach (var key in _documentOrder)
        {
            if (!_openDocuments.TryGetValue(key, out var document))
            {
                continue;
            }

            var isActive = key == _activeDocumentKey;
            var displayName = GetDocumentDisplayName(document);
            var button = new Button
            {
                Content = document.IsDirty ? $"{displayName} •" : displayName,
                Style = tabStyle,
                ToolTip = GetPhysicalPath(document) ?? "Unsaved file",
                Background = isActive ? activeBrush : Brushes.Transparent,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
            };

            var documentKey = key;
            button.Click += (_, _) => ActivateDocument(documentKey);

            var canClose = _documentOrder.Count > 1;
            var tabContainer = new DockPanel
            {
                LastChildFill = true
            };

            if (canClose)
            {
                var closeButton = new Button
                {
                    Content = "✕",
                    Style = closeStyle,
                    ToolTip = $"Close {displayName}",
                    Background = Brushes.Transparent
                };
                closeButton.Click += (_, _) => CloseDocument(documentKey);
                DockPanel.SetDock(closeButton, Dock.Right);
                tabContainer.Children.Add(closeButton);
            }

            tabContainer.Children.Add(button);

            var chrome = new Border
            {
                Child = tabContainer,
                Background = isActive ? activeBrush : Brushes.Transparent,
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                Margin = new Thickness(4, 4, 0, 0),
                BorderBrush = isActive ? FindResource("BorderBrush") as Brush : Brushes.Transparent,
                BorderThickness = new Thickness(1, 1, 1, 0)
            };

            DocumentTabsPanel.Children.Add(chrome);
        }
    }

    private void ActivateDocument(string documentKey)
    {
        if (!_openDocuments.ContainsKey(documentKey))
        {
            return;
        }

        if (_activeDocumentKey == documentKey)
        {
            return;
        }

        SaveEditorIntoActiveDocument();
        _activeDocumentKey = documentKey;
        SyncEditorFromActiveDocument();
        RefreshDocumentTabs();
    }

    private bool TryCloseDocument(string documentKey)
    {
        if (!_openDocuments.TryGetValue(documentKey, out var document))
        {
            return false;
        }

        if (document.IsDirty)
        {
            var saveChoice = MessageBox.Show(
                $"Save changes to {GetDocumentDisplayName(document)} before closing?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (saveChoice == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (saveChoice == MessageBoxResult.Yes)
            {
                var previouslyActiveKey = _activeDocumentKey;
                if (_activeDocumentKey != documentKey)
                {
                    _activeDocumentKey = documentKey;
                    SyncEditorFromActiveDocument();
                }

                if (!SaveActiveDocument(showSuccessMessage: false))
                {
                    if (_activeDocumentKey != previouslyActiveKey && _openDocuments.ContainsKey(previouslyActiveKey))
                    {
                        _activeDocumentKey = previouslyActiveKey;
                        SyncEditorFromActiveDocument();
                    }
                    return false;
                }
            }
        }

        var closedFilePath = GetPhysicalPath(document);
        var closedIndex = _documentOrder.IndexOf(documentKey);
        _openDocuments.Remove(documentKey);
        _documentOrder.Remove(documentKey);

        if (!string.IsNullOrWhiteSpace(closedFilePath))
        {
            var hasRemainingSiblings = _openDocuments.Values.Any(doc =>
                string.Equals(GetPhysicalPath(doc), closedFilePath, StringComparison.OrdinalIgnoreCase));
            if (!hasRemainingSiblings)
            {
                _debuggerService.ClearBreakpointsForFile(closedFilePath);
            }
        }

        if (_documentOrder.Count == 0)
        {
            ResetToSingleDocument("", null);
            return true;
        }

        if (_activeDocumentKey == documentKey)
        {
            var nextIndex = Math.Max(0, closedIndex - 1);
            if (nextIndex >= _documentOrder.Count)
            {
                nextIndex = _documentOrder.Count - 1;
            }

            _activeDocumentKey = _documentOrder[nextIndex];
            SyncEditorFromActiveDocument();
        }

        RefreshDocumentTabs();
        return true;
    }

    private void CloseDocument(string documentKey)
    {
        _ = TryCloseDocument(documentKey);
    }

    private void CloseOtherDocuments()
    {
        var otherDocumentKeys = _documentOrder
            .Where(key => !string.Equals(key, _activeDocumentKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in otherDocumentKeys)
        {
            if (!TryCloseDocument(key))
            {
                return;
            }
        }
    }

    private void CloseAllDocuments()
    {
        var allKeys = _documentOrder.ToList();
        foreach (var key in allKeys)
        {
            if (!TryCloseDocument(key))
            {
                return;
            }
        }
    }

    private bool SaveActiveDocument(bool showSuccessMessage)
    {
        SaveEditorIntoActiveDocument();
        var activeDocument = GetActiveDocument();
        var content = activeDocument.Content;

        if (IsVirtualDocument(activeDocument))
        {
            if (!PersistVirtualFamilyToPhysicalFile(activeDocument))
            {
                return false;
            }

            RefreshDocumentTabs();
            if (showSuccessMessage)
            {
                MessageBox.Show("File saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return true;
        }

        string fileName;
        if (string.IsNullOrEmpty(activeDocument.FilePath))
        {
            var dialog = new SaveFileDialog
            {
                Filter = "MALDA Files (*.malda)|*.malda|All Files (*.*)|*.*",
                DefaultExt = "malda",
                FileName = "program.malda"
            };

            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            fileName = dialog.FileName;
            var targetKey = GetDocumentKey(fileName);
            activeDocument.FilePath = fileName;
            activeDocument.PhysicalFilePath = fileName;

            if (_activeDocumentKey != targetKey)
            {
                var previousKey = _activeDocumentKey;
                _openDocuments.Remove(previousKey);

                if (_openDocuments.TryGetValue(targetKey, out var existingDocument))
                {
                    existingDocument.Content = activeDocument.Content;
                    existingDocument.LastSavedContent = activeDocument.Content;
                    existingDocument.IsDirty = false;
                    activeDocument = existingDocument;
                }
                else
                {
                    _openDocuments[targetKey] = activeDocument;
                }

                _activeDocumentKey = targetKey;
                var oldIndex = _documentOrder.FindIndex(k => string.Equals(k, previousKey, StringComparison.OrdinalIgnoreCase));
                if (oldIndex >= 0)
                {
                    _documentOrder[oldIndex] = _activeDocumentKey;
                }
                else if (!_documentOrder.Contains(_activeDocumentKey))
                {
                    _documentOrder.Add(_activeDocumentKey);
                }
            }

            _fileService.SetFilePath(fileName);
        }
        else
        {
            fileName = activeDocument.FilePath;
        }

        File.WriteAllText(fileName, content);
        activeDocument.LastSavedContent = content;
        activeDocument.IsDirty = false;
        activeDocument.PhysicalFilePath = fileName;
        _fileService.SetContent(content);
        RefreshDocumentTabs();

        if (showSuccessMessage)
        {
            MessageBox.Show("File saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return true;
    }

    private void ResetToSingleDocument(string content, string? filePath)
    {
        var key = GetDocumentKey(filePath);
        _openDocuments.Clear();
        _documentOrder.Clear();
        _openDocuments[key] = new OpenDocument
        {
            FilePath = filePath,
            PhysicalFilePath = filePath,
            Content = content,
            LastSavedContent = content,
            IsDirty = false
        };
        _documentOrder.Add(key);
        _activeDocumentKey = key;
        SyncEditorFromActiveDocument();
        RefreshDocumentTabs();
        SetCurrentExample(null);
    }

    private List<OpenDocument> GetVirtualFamily(OpenDocument document)
    {
        var physicalPath = GetPhysicalPath(document);
        if (string.IsNullOrWhiteSpace(physicalPath))
        {
            return new List<OpenDocument>();
        }

        return _documentOrder
            .Select(key => _openDocuments.TryGetValue(key, out var doc) ? doc : null)
            .Where(doc => doc != null && IsVirtualDocument(doc) && string.Equals(GetPhysicalPath(doc), physicalPath, StringComparison.OrdinalIgnoreCase))
            .Cast<OpenDocument>()
            .OrderBy(doc => doc.VirtualOrder)
            .ToList();
    }

    private string RecomposeVirtualFamily(OpenDocument document)
    {
        var family = GetVirtualFamily(document);
        var physicalPath = GetPhysicalPath(document);
        var sections = family.Select(doc => new VirtualDocumentSection
        {
            SectionId = doc.VirtualTabId ?? $"sec_{doc.VirtualOrder + 1:D3}",
            Title = doc.VirtualDisplayName ?? $"section {doc.VirtualOrder + 1}",
            Order = doc.VirtualOrder,
            StartLine = doc.VirtualStartLine,
            EndLine = doc.VirtualEndLine,
            Content = doc.Content
        }).ToList();

        var fullSource = !string.IsNullOrWhiteSpace(physicalPath) && File.Exists(physicalPath)
            ? _virtualDocumentSegmentationService.RecomposePreservingClosedSections(sections, File.ReadAllText(physicalPath))
            : _virtualDocumentSegmentationService.Recompose(sections);

        var byId = _virtualDocumentSegmentationService
            .Segment(fullSource)
            .ToDictionary(section => section.SectionId, StringComparer.OrdinalIgnoreCase);
        foreach (var member in family)
        {
            if (member.VirtualTabId == null || !byId.TryGetValue(member.VirtualTabId, out var updatedSection))
            {
                continue;
            }

            member.VirtualStartLine = updatedSection.StartLine;
            member.VirtualEndLine = updatedSection.EndLine;
        }

        return fullSource;
    }

    private bool PersistVirtualFamilyToPhysicalFile(OpenDocument document)
    {
        var physicalPath = GetPhysicalPath(document);
        if (string.IsNullOrWhiteSpace(physicalPath))
        {
            return false;
        }

        var fullSource = RecomposeVirtualFamily(document);
        File.WriteAllText(physicalPath, fullSource);

        foreach (var member in GetVirtualFamily(document))
        {
            member.LastSavedContent = member.Content;
            member.IsDirty = false;
            member.FilePath = physicalPath;
            member.PhysicalFilePath = physicalPath;
        }

        if (_openDocuments.TryGetValue(_activeDocumentKey, out var active) && IsVirtualDocument(active))
        {
            _fileService.SetFilePath(physicalPath);
            _fileService.SetContent(active.Content);
        }

        return true;
    }

    private (string Source, string SourceKey) GetSourceForAnalysis(OpenDocument document)
    {
        if (!IsVirtualDocument(document))
        {
            return (document.Content, document.FilePath ?? "main.malda");
        }

        return (RecomposeVirtualFamily(document), GetPhysicalPath(document) ?? "main.malda");
    }

    private (string Source, string SourceFilePath, bool UsesPhysicalFileOnDisk) GetSourceForExecution(OpenDocument document)
    {
        if (IsVirtualDocument(document))
        {
            var physicalPath = GetPhysicalPath(document) ?? "main.malda";
            var source = RecomposeVirtualFamily(document);
            File.WriteAllText(physicalPath, source);
            return (source, physicalPath, true);
        }

        return (document.Content, document.FilePath ?? "main.malda", !string.IsNullOrWhiteSpace(document.FilePath) && File.Exists(document.FilePath));
    }

    private void RebuildVirtualTabsForPhysicalFile(string physicalPath, string fileContent)
    {
        var fullPath = Path.GetFullPath(physicalPath);
        var keysToRemove = _documentOrder
            .Where(key => _openDocuments.TryGetValue(key, out var doc) &&
                          string.Equals(GetPhysicalPath(doc), fullPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var insertIndex = keysToRemove.Count > 0
            ? _documentOrder.IndexOf(keysToRemove[0])
            : _documentOrder.Count;
        foreach (var key in keysToRemove)
        {
            _openDocuments.Remove(key);
            _documentOrder.Remove(key);
        }

        var sections = _virtualDocumentSegmentationService.Segment(fileContent);
        if (sections.Count <= 1)
        {
            var key = GetDocumentKey(fullPath);
            _openDocuments[key] = CreateDocument(fullPath, fileContent);
            if (!_documentOrder.Contains(key))
            {
                _documentOrder.Insert(Math.Min(insertIndex, _documentOrder.Count), key);
            }

            return;
        }
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var key = GetDocumentKey(fullPath, section.SectionId);
            _openDocuments[key] = new OpenDocument
            {
                FilePath = fullPath,
                PhysicalFilePath = fullPath,
                VirtualTabId = section.SectionId,
                VirtualDisplayName = section.Title,
                VirtualOrder = section.Order,
                VirtualStartLine = section.StartLine,
                VirtualEndLine = section.EndLine,
                Content = section.Content,
                LastSavedContent = section.Content,
                IsDirty = false
            };
            _documentOrder.Insert(Math.Min(insertIndex + i, _documentOrder.Count), key);
        }
    }

    private static string? TryResolveExampleAbsolutePath(string? relativeExamplePath)
    {
        if (string.IsNullOrWhiteSpace(relativeExamplePath))
        {
            return null;
        }

        if (Path.IsPathRooted(relativeExamplePath) && File.Exists(relativeExamplePath))
        {
            return Path.GetFullPath(relativeExamplePath);
        }

        var candidateRoots = new List<string>();
        var cwd = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (cwd != null)
        {
            candidateRoots.Add(cwd.FullName);
            cwd = cwd.Parent;
        }

        var exeDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (exeDir != null)
        {
            candidateRoots.Add(exeDir.FullName);
            exeDir = exeDir.Parent;
        }

        foreach (var root in candidateRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidatePath = Path.Combine(root, "Examples", relativeExamplePath);
            if (File.Exists(candidatePath))
            {
                return Path.GetFullPath(candidatePath);
            }
        }

        return null;
    }

    private static IEnumerable<string> GetIncludePaths(string source, string sourceFilePath)
    {
        var baseDirectory = Path.GetDirectoryName(sourceFilePath) ?? Directory.GetCurrentDirectory();

        foreach (Match match in IncludeStatementRegex.Matches(source))
        {
            var includePath = match.Groups["path"].Value.Trim();
            if (string.IsNullOrEmpty(includePath))
            {
                continue;
            }

            var resolvedPath = Path.IsPathRooted(includePath)
                ? includePath
                : Path.Combine(baseDirectory, includePath);

            yield return Path.GetFullPath(resolvedPath);
        }
    }

    private static List<string> ResolveFileAndIncludes(string entryFilePath)
    {
        var orderedFiles = new List<string>();
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue(Path.GetFullPath(entryFilePath));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current) || !File.Exists(current))
            {
                continue;
            }

            orderedFiles.Add(current);
            var source = File.ReadAllText(current);
            foreach (var includePath in GetIncludePaths(source, current))
            {
                queue.Enqueue(includePath);
            }
        }

        return orderedFiles;
    }

    private void OpenFileAndIncludedDocuments(string filePath)
    {
        SetCurrentExample(null);

        var orderedFiles = ResolveFileAndIncludes(filePath);
        if (orderedFiles.Count == 0)
        {
            return;
        }

        SaveEditorIntoActiveDocument();

        for (int i = 0; i < orderedFiles.Count; i++)
        {
            var currentPath = orderedFiles[i];
            var fileContent = File.ReadAllText(currentPath);
            if (i == 0 && currentPath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase))
            {
                RebuildVirtualTabsForPhysicalFile(currentPath, fileContent);
                continue;
            }

            var key = GetDocumentKey(currentPath);
            if (!_openDocuments.ContainsKey(key))
            {
                _openDocuments[key] = new OpenDocument
                {
                    FilePath = currentPath,
                    PhysicalFilePath = currentPath,
                    Content = fileContent,
                    LastSavedContent = fileContent,
                    IsDirty = false
                };
                _documentOrder.Add(key);
            }
        }

        var primaryPath = Path.GetFullPath(filePath);
        _activeDocumentKey = _documentOrder
            .FirstOrDefault(key => _openDocuments.TryGetValue(key, out var doc) &&
                                   string.Equals(GetPhysicalPath(doc), primaryPath, StringComparison.OrdinalIgnoreCase))
            ?? GetDocumentKey(filePath);
        SyncEditorFromActiveDocument();
        RefreshDocumentTabs();
    }

    private void ReloadOpenFilesButton_Click(object sender, RoutedEventArgs e)
    {
        StopActiveExecution();
        SaveEditorIntoActiveDocument();

        var dirtyDocuments = _documentOrder
            .Where(key =>
                _openDocuments.TryGetValue(key, out var doc) &&
                doc.IsDirty &&
                !string.IsNullOrWhiteSpace(doc.FilePath))
            .Select(key => _openDocuments[key])
            .ToList();

        if (dirtyDocuments.Count > 0)
        {
            var dirtyNames = string.Join(", ", dirtyDocuments.Select(GetDocumentDisplayName));
            var message = $"Reloading will discard unsaved changes in: {dirtyNames}\n\nContinue?";
            var choice = MessageBox.Show(
                message,
                "Reload Open Files",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (choice != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var reloadedCount = 0;
        var skippedCount = 0;
        var missingCount = 0;

        var reloadedPhysicalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _documentOrder.ToList())
        {
            if (!_openDocuments.TryGetValue(key, out var document))
            {
                continue;
            }

            var physicalPath = GetPhysicalPath(document);
            if (string.IsNullOrWhiteSpace(physicalPath))
            {
                skippedCount++;
                continue;
            }

            if (!File.Exists(physicalPath))
            {
                missingCount++;
                continue;
            }

            if (IsVirtualDocument(document))
            {
                if (reloadedPhysicalPaths.Contains(physicalPath))
                {
                    continue;
                }

                var fileContent = File.ReadAllText(physicalPath);
                RebuildVirtualTabsForPhysicalFile(physicalPath, fileContent);
                reloadedPhysicalPaths.Add(physicalPath);
            }
            else
            {
                var fileContent = File.ReadAllText(physicalPath);
                document.Content = fileContent;
                document.LastSavedContent = fileContent;
                document.IsDirty = false;
            }

            reloadedCount++;
        }

        if (!_openDocuments.ContainsKey(_activeDocumentKey) && _documentOrder.Count > 0)
        {
            _activeDocumentKey = _documentOrder[0];
        }

        SyncEditorFromActiveDocument();
        RefreshDocumentTabs();
        UpdateDiagnostics();

        SetOutputText($"Reload complete. Reloaded: {reloadedCount}, Unsaved tabs skipped: {skippedCount}, Missing files: {missingCount}");
        SwitchToTab("output");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveActiveDocument(showSuccessMessage: true);
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MALDA Files (*.malda)|*.malda|All Files (*.*)|*.*",
            DefaultExt = "malda"
        };
        
        if (dialog.ShowDialog() == true)
        {
            // Stop debugger if running
            if (_debuggerService.State.IsRunning)
            {
                _debuggerHook?.Stop();
                _debugCancellation?.Cancel();
                _debuggerService.Stop();
                _debuggerHook = null;
                _debugTask = null;
                _debugCancellation?.Dispose();
                _debugCancellation = null;
                ResetDualDebugState();
                _ = StopJsDebuggerAsync();
                ClearCurrentLineHighlight();
                UpdateButtonStates();
            }
            
            // Stop regular run if running
            if (_runTask != null && !_runTask.IsCompleted)
            {
                _runCancellation?.Cancel();
                _runTask = null;
                _runCancellation?.Dispose();
                _runCancellation = null;
                UpdateButtonStates();
            }
            
            // Clear breakpoints for the old file before loading new file
            var oldFilePath = GetCurrentPhysicalFilePath();
            _debuggerService.ClearBreakpointsForFile(oldFilePath);
            
            OpenFileAndIncludedDocuments(dialog.FileName);
        }
    }

    private string GetCurrentSourceKey()
    {
        var activeDocument = GetActiveDocument();
        if (IsVirtualDocument(activeDocument))
        {
            return _activeDocumentKey;
        }

        return GetPhysicalPath(activeDocument) ?? "main.malda";
    }

    private string GetCurrentPhysicalFilePath()
    {
        return GetPhysicalPath(GetActiveDocument()) ?? "main.malda";
    }

    private bool NavigateToLocation(string documentKey, int zeroBasedLine, int zeroBasedColumn, int length)
    {
        if (!string.Equals(_activeDocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
        {
            ActivateDocument(documentKey);
        }

        if (CodeEditor?.Document == null)
        {
            return false;
        }

        var lineNumber = zeroBasedLine + 1;
        if (lineNumber <= 0 || lineNumber > CodeEditor.Document.LineCount)
        {
            return false;
        }

        try
        {
            var documentLine = CodeEditor.Document.GetLineByNumber(lineNumber);
            var safeColumn = Math.Max(0, zeroBasedColumn);
            var startOffset = Math.Min(documentLine.Offset + safeColumn, documentLine.EndOffset);
            var maxLength = Math.Max(0, CodeEditor.Document.TextLength - startOffset);
            var selectionLength = Math.Max(1, Math.Min(length, maxLength));

            CodeEditor.TextArea.Caret.Offset = startOffset;
            CodeEditor.TextArea.Caret.Line = lineNumber;
            CodeEditor.TextArea.Caret.Column = Math.Max(1, safeColumn + 1);
            CodeEditor.SelectionStart = startOffset;
            CodeEditor.SelectionLength = selectionLength;
            CodeEditor.ScrollToLine(lineNumber);
            CodeEditor.Focus();
            UpdateSearchHighlightsForActiveDocument();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool NavigateToOffset(string documentKey, int offset, int length)
    {
        if (!string.Equals(_activeDocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
        {
            ActivateDocument(documentKey);
        }

        if (CodeEditor?.Document == null || offset < 0 || offset >= CodeEditor.Document.TextLength)
        {
            UpdateSearchHighlightsForActiveDocument();
            return false;
        }

        var line = CodeEditor.Document.GetLineByOffset(offset);
        var column = Math.Max(1, offset - line.Offset + 1);
        var maxLength = Math.Max(0, CodeEditor.Document.TextLength - offset);
        var selectionLength = Math.Max(1, Math.Min(length, maxLength));

        CodeEditor.TextArea.Caret.Offset = offset;
        CodeEditor.TextArea.Caret.Line = line.LineNumber;
        CodeEditor.TextArea.Caret.Column = column;
        CodeEditor.SelectionStart = offset;
        CodeEditor.SelectionLength = selectionLength;
        CodeEditor.ScrollToLine(line.LineNumber);
        CodeEditor.Focus();
        UpdateSearchHighlightsForActiveDocument();
        return true;
    }

    
    // Menu Event Handlers
    
    // File Menu
    private void FileNew_Click(object sender, RoutedEventArgs e)
    {
        // Stop debugger if running
        if (_debuggerService.State.IsRunning)
        {
            _debuggerHook?.Stop();
            _debugCancellation?.Cancel();
            _debuggerService.Stop();
            _debuggerHook = null;
            _debugTask = null;
            _debugCancellation?.Dispose();
            _debugCancellation = null;
            ResetDualDebugState();
            _ = StopJsDebuggerAsync();
            ClearCurrentLineHighlight();
            UpdateButtonStates();
        }
        
        // Stop regular run if running
        if (_runTask != null && !_runTask.IsCompleted)
        {
            _runCancellation?.Cancel();
            _runTask = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
            UpdateButtonStates();
        }
        
        // Clear breakpoints for the old file
        var oldFilePath = GetCurrentPhysicalFilePath();
        _debuggerService.ClearBreakpointsForFile(oldFilePath);

        ShowStarterLauncher(initialTrack: "student", fallbackToBlank: true);
    }

    
    private void FileOpen_Click(object sender, RoutedEventArgs e)
    {
        LoadButton_Click(sender, e);
    }

    private void LearningBranchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string relativeExamplePath ||
            string.IsNullOrWhiteSpace(relativeExamplePath))
        {
            return;
        }

        OpenExampleByRelativePath(relativeExamplePath, "Loaded specialization");
    }

    private void LearningNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string relativeExamplePath ||
            string.IsNullOrWhiteSpace(relativeExamplePath))
        {
            return;
        }

        OpenExampleByRelativePath(relativeExamplePath, "Loaded next lesson");
    }

    
    private void FileSave_Click(object sender, RoutedEventArgs e)
    {
        SaveButton_Click(sender, e);
    }

    private void FileSaveAs_Click(object sender, RoutedEventArgs e)
    {
        SaveActiveDocumentAs();
    }

    private void FileOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Open Folder"
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return;
        }

        try
        {
            _workspaceFiles.SetExplicitWorkspaceRoot(dialog.FolderName);
            RefreshWorkspaceFilesTree();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshWorkspaceFilesTree()
    {
        WorkspaceFilesTreeView.Items.Clear();
        var root = _workspaceFiles.ExplicitWorkspaceRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            WorkspaceFolderLabel.Text = "Open a folder to browse .malda files.";
            return;
        }

        WorkspaceFolderLabel.Text = root;
        var files = _workspaceFiles.GetExplicitWorkspaceMaldaFiles();
        foreach (var path in files)
        {
            var relative = Path.GetRelativePath(root, path);
            WorkspaceFilesTreeView.Items.Add(new TreeViewItem
            {
                Header = relative,
                Tag = path
            });
        }
    }

    private void WorkspaceFilesTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WorkspaceFilesTreeView.SelectedItem is TreeViewItem { Tag: string path } && File.Exists(path))
        {
            OpenFileAndIncludedDocuments(path);
        }
    }

    private bool SaveActiveDocumentAs()
    {
        SaveEditorIntoActiveDocument();
        var activeDocument = GetActiveDocument();
        var dialog = new SaveFileDialog
        {
            Filter = "MALDA Files (*.malda)|*.malda|All Files (*.*)|*.*",
            DefaultExt = "malda",
            FileName = string.IsNullOrWhiteSpace(activeDocument.FilePath)
                ? "program.malda"
                : Path.GetFileName(GetPhysicalPath(activeDocument) ?? "program.malda")
        };

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        var targetPath = dialog.FileName;
        var content = IsVirtualDocument(activeDocument)
            ? RecomposeVirtualFamily(activeDocument)
            : activeDocument.Content;

        File.WriteAllText(targetPath, content);
        OpenFileAndIncludedDocuments(targetPath);
        if (_workspaceFiles.ExplicitWorkspaceRoot != null)
        {
            RefreshWorkspaceFilesTree();
        }

        return true;
    }

    private void FileCloseOthers_Click(object sender, RoutedEventArgs e)
    {
        CloseOtherDocuments();
    }

    private void FileCloseAll_Click(object sender, RoutedEventArgs e)
    {
        CloseAllDocuments();
    }

    
    private void FileExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
