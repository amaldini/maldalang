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
    
    private void UpdateBreakpointVisuals()
    {
        var activeDocument = GetActiveDocument();
        var fileName = GetPhysicalPath(activeDocument) ?? "main.malda";
        var breakpoints = _debuggerService.Breakpoints.Where(bp => bp.FilePath == fileName && bp.Enabled);
        var localBreakpoints = IsVirtualDocument(activeDocument)
            ? breakpoints
                .Where(bp => VirtualDocumentCoordinateMapper.ContainsPhysicalLine(
                    bp.Line,
                    activeDocument.VirtualStartLine,
                    activeDocument.VirtualEndLine))
                .Select(bp => VirtualDocumentCoordinateMapper.ToEditorLine(bp.Line, activeDocument.VirtualStartLine))
                .ToList()
            : breakpoints.Select(bp => bp.Line).ToList();
        
        _breakpointLines.Clear();
        _breakpointLines.AddRange(localBreakpoints);
        
        // Force redraw
        CodeEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void UpdateButtonStates()
    {
        Dispatcher.Invoke(() =>
        {
            var isDebugRunning = _debuggerService.State.IsRunning;
            var isRunRunning = _runTask != null && !_runTask.IsCompleted;
            var isAnyRunning = isDebugRunning || isRunRunning;
            
            var canDebug = !isAnyRunning;
            var canStop = isAnyRunning;
            var canContinue = _debuggerService.State.IsPaused && isDebugRunning;
            var canStep = _debuggerService.State.IsPaused && isDebugRunning;
            var canPause = isDebugRunning && !_debuggerService.State.IsPaused;

            if (DebugButton != null) DebugButton.IsEnabled = canDebug;
            if (StopButton != null) StopButton.IsEnabled = canStop;
            if (ContinueButton != null) ContinueButton.IsEnabled = canContinue;
            if (StepOverButton != null) StepOverButton.IsEnabled = canStep;
            if (StepIntoButton != null) StepIntoButton.IsEnabled = canStep;
            if (StepOutButton != null) StepOutButton.IsEnabled = canStep;
            if (PauseButton != null) PauseButton.IsEnabled = canPause;
            CommandManager.InvalidateRequerySuggested();
        });
    }

    private DesktopEditorCommandContext GetEditorCommandContext()
    {
        return new DesktopEditorCommandContext(
            IsDebugRunning: _debuggerService.State.IsRunning,
            IsPaused: _debuggerService.State.IsPaused,
            IsRunRunning: _runTask != null && !_runTask.IsCompleted);
    }

    private void ExecutePrimaryF5()
    {
        switch (DesktopEditorCommandPolicy.ResolveF5(GetEditorCommandContext()))
        {
            case DesktopEditorCommand.Continue:
                ContinueButton_Click(this, new RoutedEventArgs());
                break;
            case DesktopEditorCommand.StartDebugging:
                DebugButton_Click(this, new RoutedEventArgs());
                break;
        }
    }

    private void OnBreakpointsChanged()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateBreakpointsPanel();
            UpdateBreakpointVisuals();
            if (_jsDebugger is { IsAttached: true })
            {
                _ = _jsDebugger.SyncBreakpointsAsync(_debuggerService.Breakpoints.ToList());
            }
        });
    }

    
    public bool IsBreakpointLine(int lineNumber)
    {
        return _breakpointLines.Contains(lineNumber);
    }

    
    public void ToggleBreakpointAtLine(int lineNumber)
    {
        // lineNumber is 1-based (AvalonEdit / DebugSession). VirtualStartLine is a 0-based section offset.
        var activeDocument = GetActiveDocument();
        var fileName = GetPhysicalPath(activeDocument) ?? "main.malda";
        if (IsVirtualDocument(activeDocument))
        {
            lineNumber = VirtualDocumentCoordinateMapper.ToPhysicalLine(lineNumber, activeDocument.VirtualStartLine);
        }

        _debuggerService.ToggleBreakpoint(lineNumber, fileName);
        UpdateBreakpointVisuals();
        UpdateBreakpointsPanel();
    }

    private void UpdateBreakpointsPanel()
    {
        BreakpointsListBox.Items.Clear();
        foreach (var bp in _debuggerService.Breakpoints)
        {
            BreakpointsListBox.Items.Add(bp);
        }
    }

    private void CodeEditor_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var line = CodeEditor.Document.GetLineByNumber(CodeEditor.TextArea.Caret.Line);
        var lineNumber = line.LineNumber; // 1-based (same as DebuggerService / core)
        var activeDocument = GetActiveDocument();
        var fileName = GetPhysicalPath(activeDocument) ?? "main.malda";
        if (IsVirtualDocument(activeDocument))
        {
            lineNumber = VirtualDocumentCoordinateMapper.ToPhysicalLine(lineNumber, activeDocument.VirtualStartLine);
        }

        _debuggerService.ToggleBreakpoint(lineNumber, fileName);
        UpdateBreakpointVisuals();
        UpdateBreakpointsPanel();
    }

    private async void DebugButton_Click(object sender, RoutedEventArgs e)
    {
        SaveEditorIntoActiveDocument();
        var activeDocument = GetActiveDocument();
        var sourceForExecution = GetSourceForExecution(activeDocument);
        var source = sourceForExecution.Source;
        var fileName = sourceForExecution.SourceFilePath;
        var input = ProgramInputTextBox.Text;
        
        SetOutputText("");
        ResetDualDebugState();

        var launchKind = MaldaDebugLaunch.Classify(source);
        if (launchKind == MaldaDebugLaunchKind.JavaScript)
        {
            try
            {
                await StartJsWebViewDebuggingAsync(source, fileName);
            }
            catch (Exception ex)
            {
                ResetDualDebugState();
                _debuggerService.Stop();
                await StopJsDebuggerAsync();
                SetOutputText($"JavaScript debug failed: {ex.Message}", isError: true);
                UpdateButtonStates();
            }
            return;
        }

        if (launchKind == MaldaDebugLaunchKind.FullStack)
        {
            _dualDebugSession = true;
            try
            {
                await StartJsWebViewDebuggingAsync(source, fileName, dualDebug: true);
            }
            catch (Exception ex)
            {
                ResetDualDebugState();
                _debuggerService.Stop();
                await StopJsDebuggerAsync();
                SetOutputText($"Full-stack debug failed: {ex.Message}", isError: true);
                UpdateButtonStates();
                return;
            }
        }
        
        // Do not clear tool calls log here so Edit mode tool calls persist when user then debugs
        UpdateToolCallsDisplay();
        
        _debuggerService.Start();
        _interpretDebugSessionActive = true;
        if (!_dualDebugSession)
        {
            _focusedDebuggee = DebuggeeKind.Interpret;
        }

        _debuggerHook = new DebuggerHook(_debuggerService);
        _debuggerHook.SetDebugMode(DebugMode.Continue);
        _debuggerHook.Session.ConditionError += message =>
        {
            Dispatcher.Invoke(() => RefreshDebugOutput(message, isError: true));
        };
        _debuggerHook.OnPaused += (line, file) =>
        {
            Dispatcher.Invoke(() =>
            {
                _interpretPaused = true;
                _focusedDebuggee = DebuggeeKind.Interpret;
                RefreshDebugOutput();
                
                var interpreter = _executionService.GetCurrentInterpreter();
                if (interpreter != null && _debuggerHook != null)
                {
                    _debuggerHook.UpdateDebugInfo(interpreter);
                }
                UpdateDebugInfo();
                HighlightCurrentLine(line, file);
                SwitchToTab("debug");
                ApplyDualPauseState();
            });
        };
        
        _debugCancellation = new CancellationTokenSource();
        var hostPartitionOnly = _dualDebugSession;
        _debugTask = Task.Run(async () =>
        {
            try
            {
                var result = await _executionService.ExecuteWithDebuggerAsync(
                    source,
                    _debuggerHook,
                    input,
                    fileName,
                    hostPartitionOnly);
                
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        RefreshDebugOutput($"Error: {result.Error}", isError: true);
                    }
                    else
                    {
                        RefreshDebugOutput();
                    }
                    UpdateDebugInfo();
                    UpdateButtonStates();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    RefreshDebugOutput($"Error: {ex.Message}", isError: true);
                    if (!_jsPaused)
                    {
                        ClearCurrentLineHighlight();
                    }
                });
            }
            finally
            {
                Dispatcher.Invoke(() => FinishInterpretDebugSession());
            }
        }, _debugCancellation.Token);
        
        UpdateButtonStates();
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_debuggerService.State.IsPaused) return;

        if (ShouldDriveJsDebugger())
        {
            _jsStepInFlight = false;
            _jsPaused = false;
            ApplyDualPauseState();
            _ = _jsDebugger!.ContinueAsync();
            if (_interpretPaused)
            {
                ShowInterpretPauseUi();
            }
            return;
        }

        if (_debuggerHook == null) return;

        _interpretPaused = false;
        RefreshDebugOutput();
        _debuggerHook.SetDebugMode(DebugMode.Continue);
        ApplyDualPauseState();
        if (_jsPaused)
        {
            ShowJsPauseUi();
        }
    }

    private void StepOverButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_debuggerService.State.IsPaused) return;

        if (ShouldDriveJsDebugger())
        {
            _jsStepInFlight = true;
            _jsPaused = false;
            ApplyDualPauseState();
            _ = _jsDebugger!.StepOverAsync();
            return;
        }

        if (_debuggerHook == null) return;

        _interpretPaused = false;
        RefreshDebugOutput();
        UpdateDebugInfo();
        _debuggerHook.SetDebugMode(DebugMode.StepOver);
        ApplyDualPauseState();
        
        if (_debuggerService.State.CurrentLine.HasValue)
        {
            HighlightCurrentLine(_debuggerService.State.CurrentLine.Value, _debuggerService.State.CurrentFile);
        }
    }

    private void StepIntoButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_debuggerService.State.IsPaused) return;

        if (ShouldDriveJsDebugger())
        {
            _jsStepInFlight = true;
            _jsPaused = false;
            ApplyDualPauseState();
            _ = _jsDebugger!.StepIntoAsync();
            return;
        }

        if (_debuggerHook == null) return;

        _interpretPaused = false;
        RefreshDebugOutput();
        UpdateDebugInfo();
        _debuggerHook.SetDebugMode(DebugMode.StepInto);
        ApplyDualPauseState();
        
        if (_debuggerService.State.CurrentLine.HasValue)
        {
            HighlightCurrentLine(_debuggerService.State.CurrentLine.Value, _debuggerService.State.CurrentFile);
        }
    }

    private void StepOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_debuggerService.State.IsPaused) return;

        if (ShouldDriveJsDebugger())
        {
            _jsStepInFlight = true;
            _jsPaused = false;
            ApplyDualPauseState();
            _ = _jsDebugger!.StepOutAsync();
            return;
        }

        if (_debuggerHook == null) return;

        _interpretPaused = false;
        var interpreter = _executionService.GetCurrentInterpreter();
        if (interpreter != null)
        {
            var callStack = interpreter.GetCallStack();
            if (callStack.Count > 0)
            {
                var currentFrame = callStack[callStack.Count - 1];
                _debuggerHook.SetStepOutFunction(currentFrame.FunctionName);
            }
        }
        
        RefreshDebugOutput();
        UpdateDebugInfo();
        _debuggerHook.SetDebugMode(DebugMode.StepOut);
        ApplyDualPauseState();
        
        if (_debuggerService.State.CurrentLine.HasValue)
        {
            HighlightCurrentLine(_debuggerService.State.CurrentLine.Value, _debuggerService.State.CurrentFile);
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_debuggerService.State.IsRunning || _debuggerService.State.IsPaused) return;

        if (_dualDebugSession && _jsDebugger is { IsAttached: true } && _debuggerHook != null)
        {
            _debuggerHook.SetDebugMode(DebugMode.Paused);
            _debuggerService.Pause();
            RefreshDebugOutput();
            _ = _jsDebugger.PauseAsync();
            UpdateButtonStates();
            return;
        }

        if (_jsDebugger is { IsAttached: true })
        {
            _debuggerService.Pause();
            UpdateButtonStates();
            _ = _jsDebugger.PauseAsync();
            return;
        }

        if (_debuggerHook == null) return;
        
        _debuggerHook.SetDebugMode(DebugMode.Paused);
        _debuggerService.Pause();
        RefreshDebugOutput();
        UpdateDebugInfo();
        
        if (_debuggerService.State.CurrentLine.HasValue)
        {
            HighlightCurrentLine(_debuggerService.State.CurrentLine.Value, _debuggerService.State.CurrentFile);
        }
        UpdateButtonStates();
    }

    private void HighlightCurrentLine(int line, string? file = null)
    {
        if (!string.IsNullOrWhiteSpace(file))
        {
            ActivateDocumentForPhysicalLine(file, line);
        }

        var activeDocument = GetActiveDocument();
        var editorLine = line;
        if (IsVirtualDocument(activeDocument) &&
            VirtualDocumentCoordinateMapper.ContainsPhysicalLine(
                line,
                activeDocument.VirtualStartLine,
                activeDocument.VirtualEndLine))
        {
            editorLine = VirtualDocumentCoordinateMapper.ToEditorLine(line, activeDocument.VirtualStartLine);
        }

        if (editorLine < 1 || editorLine > CodeEditor.Document.LineCount)
        {
            return;
        }

        CodeEditor.TextArea.Caret.Line = editorLine;
        CodeEditor.ScrollToLine(editorLine);

        if (_currentLineRenderer != null)
        {
            _currentLineRenderer.CurrentLine = editorLine;
            CodeEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    private void ActivateDocumentForPhysicalLine(string file, int physicalOneBasedLine)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(file);
        }
        catch
        {
            return;
        }

        var match = _documentOrder
            .Select(key => (Key: key, Document: _openDocuments[key]))
            .FirstOrDefault(item =>
                IsVirtualDocument(item.Document) &&
                string.Equals(GetPhysicalPath(item.Document), fullPath, StringComparison.OrdinalIgnoreCase) &&
                VirtualDocumentCoordinateMapper.ContainsPhysicalLine(
                    physicalOneBasedLine,
                    item.Document.VirtualStartLine,
                    item.Document.VirtualEndLine));
        if (match.Key != null && match.Key != _activeDocumentKey)
        {
            ActivateDocument(match.Key);
            return;
        }

        var physicalKey = _documentOrder.FirstOrDefault(key =>
            _openDocuments.TryGetValue(key, out var document) &&
            !IsVirtualDocument(document) &&
            string.Equals(GetPhysicalPath(document), fullPath, StringComparison.OrdinalIgnoreCase));
        if (physicalKey != null && physicalKey != _activeDocumentKey)
        {
            ActivateDocument(physicalKey);
        }
    }

    private const string VariablesInspectRoot = "var";
    private const string WatchesInspectRoot = "watch";

    private void UpdateDebugInfo()
    {
        if (UseJsInspect)
        {
            UpdateJsDebugInfo();
            return;
        }

        var session = _debuggerHook?.Session;
        if (session == null)
        {
            ClearInspectTrees();
            CallStackListBox.Items.Clear();
            UpdateBreakpointsPanel();
            return;
        }

        _suppressCallStackSelection = true;
        try
        {
            CallStackListBox.Items.Clear();
            var frames = session.GetStackFrames();
            for (var i = 0; i < frames.Count; i++)
            {
                var frameId = i + 1;
                CallStackListBox.Items.Add(new ListBoxItem
                {
                    Content = DebugInspectSnapshotBuilder.FormatFrame(frames[i]),
                    Tag = frameId
                });
            }

            if (CallStackListBox.Items.Count > 0)
            {
                var selectedIndex = Math.Clamp(_selectedDebugFrameId - 1, 0, CallStackListBox.Items.Count - 1);
                CallStackListBox.SelectedIndex = selectedIndex;
                _selectedDebugFrameId = selectedIndex + 1;
            }
            else
            {
                _selectedDebugFrameId = 1;
            }
        }
        finally
        {
            _suppressCallStackSelection = false;
        }

        RebuildVariablesTree(session);
        _ = RefreshWatchesAsync(session);
        UpdateBreakpointsPanel();
    }

    private void ClearInspectTrees()
    {
        _suppressInspectExpansionTracking = true;
        try
        {
            VariablesTreeView.Items.Clear();
            WatchesTreeView.Items.Clear();
        }
        finally
        {
            _suppressInspectExpansionTracking = false;
        }
    }

    private void RebuildVariablesTree(MaldaLang.Interpreter.Debug.DebugSession session)
    {
        _suppressInspectExpansionTracking = true;
        try
        {
            VariablesTreeView.Items.Clear();
            foreach (var scope in DebugInspectSnapshotBuilder.BuildScopes(session, _selectedDebugFrameId))
            {
                VariablesTreeView.Items.Add(CreateInspectTreeItem(scope, VariablesInspectRoot));
            }

            RestoreInspectTree(VariablesTreeView.Items, VariablesInspectRoot);
        }
        finally
        {
            _suppressInspectExpansionTracking = false;
        }
    }

    private TreeViewItem CreateInspectTreeItem(DebugInspectNode node, string parentPath)
    {
        node.Path = DebugInspectExpansionState.Join(parentPath, node.Name);
        var item = new TreeViewItem
        {
            Header = node.Display,
            Tag = node
        };
        if (node.CanExpand)
        {
            item.Items.Add(new TreeViewItem { Header = "…" });
        }

        return item;
    }

    private void RestoreInspectTree(ItemCollection items, string parentPath)
    {
        _inspectExpansion.RestoreExpanded(
            items.Cast<TreeViewItem>(),
            parentPath,
            item => item.Tag is DebugInspectNode node ? node.Name : "",
            item => item.Tag is DebugInspectNode { CanExpand: true },
            item => item.IsExpanded = true,
            item => item.Items.Cast<TreeViewItem>());
    }

    private void DebugInspectTree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem item || item.Tag is not DebugInspectNode node)
        {
            return;
        }

        if (!_suppressInspectExpansionTracking)
        {
            _inspectExpansion.SetExpanded(node.Path, true);
        }

        if (!node.CanExpand || (_debuggerHook == null && _jsDebugger == null))
        {
            return;
        }

        if (item.Items.Count == 1 && item.Items[0] is TreeViewItem { Tag: null, Header: "…" })
        {
            item.Items.Clear();
            if (UseJsInspect)
            {
                var cached = _jsDebugger.GetCachedChildren(node.VariablesReference);
                if (cached.Count > 0)
                {
                    foreach (var child in cached)
                    {
                        item.Items.Add(CreateInspectTreeItem(child, node.Path));
                    }
                }
                else
                {
                    item.Items.Add(new TreeViewItem { Header = "…" });
                    _ = ExpandJsInspectNodeAsync(item, node);
                    return;
                }
            }
            else if (_debuggerHook != null)
            {
                foreach (var child in DebugInspectSnapshotBuilder.Expand(_debuggerHook.Session, node.VariablesReference, node.FrameId))
                {
                    item.Items.Add(CreateInspectTreeItem(child, node.Path));
                }
            }
        }

        RestoreInspectTree(item.Items, node.Path);
    }

    private void DebugInspectTree_Collapsed(object sender, RoutedEventArgs e)
    {
        if (_suppressInspectExpansionTracking)
        {
            return;
        }

        if (e.OriginalSource is not TreeViewItem item || item.Tag is not DebugInspectNode node)
        {
            return;
        }

        _inspectExpansion.SetExpanded(node.Path, false);
    }

    private void CallStackListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCallStackSelection || (_debuggerHook == null && _jsDebugger == null))
        {
            return;
        }

        if (CallStackListBox.SelectedItem is ListBoxItem { Tag: int frameId })
        {
            _selectedDebugFrameId = frameId;
            if (UseJsInspect)
            {
                RebuildJsVariablesTree();
                _ = RefreshJsWatchesAsync();
            }
            else if (_debuggerHook != null)
            {
                RebuildVariablesTree(_debuggerHook.Session);
                _ = RefreshWatchesAsync(_debuggerHook.Session);
            }
        }
    }

    private async Task RefreshWatchesAsync(MaldaLang.Interpreter.Debug.DebugSession session)
    {
        var nodes = new List<DebugInspectNode>();
        foreach (var expression in _watchExpressions)
        {
            try
            {
                var value = await session.EvaluateWatchAsync(expression, _selectedDebugFrameId).ConfigureAwait(true);
                nodes.Add(DebugInspectSnapshotBuilder.FromVariable(value, _selectedDebugFrameId));
            }
            catch (Exception ex)
            {
                nodes.Add(DebugInspectSnapshotBuilder.FromWatchError(expression, ex.Message, _selectedDebugFrameId));
            }
        }

        RebuildWatchesTree(nodes);
    }

    private void RebuildWatchesTree(IReadOnlyList<DebugInspectNode> nodes)
    {
        _suppressInspectExpansionTracking = true;
        try
        {
            WatchesTreeView.Items.Clear();
            foreach (var node in nodes)
            {
                WatchesTreeView.Items.Add(CreateInspectTreeItem(node, WatchesInspectRoot));
            }

            RestoreInspectTree(WatchesTreeView.Items, WatchesInspectRoot);
        }
        finally
        {
            _suppressInspectExpansionTracking = false;
        }
    }

    private void AddWatchButton_Click(object sender, RoutedEventArgs e)
    {
        AddWatchFromTextBox();
    }

    private void WatchExpressionTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AddWatchFromTextBox();
        }
    }

    private void AddWatchFromTextBox()
    {
        var expression = WatchExpressionTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        if (!_watchExpressions.Contains(expression, StringComparer.Ordinal))
        {
            _watchExpressions.Add(expression);
        }

        WatchExpressionTextBox.Clear();
        if (UseJsInspect)
        {
            _ = RefreshJsWatchesAsync();
        }
        else if (_debuggerHook != null)
        {
            _ = RefreshWatchesAsync(_debuggerHook.Session);
        }
        else
        {
            RebuildWatchesTree(_watchExpressions
                .Select(value => new DebugInspectNode { Display = value, Name = value })
                .ToList());
        }
    }

    private void WatchesTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindWatchRootItem(e.OriginalSource as DependencyObject) is not TreeViewItem root ||
            root.Tag is not DebugInspectNode node)
        {
            return;
        }

        var index = _watchExpressions.FindIndex(expression => string.Equals(expression, node.Name, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        _watchExpressions.RemoveAt(index);
        if (UseJsInspect)
        {
            _ = RefreshJsWatchesAsync();
        }
        else if (_debuggerHook != null)
        {
            _ = RefreshWatchesAsync(_debuggerHook.Session);
        }
        else
        {
            WatchesTreeView.Items.Remove(root);
        }
    }

    private static TreeViewItem? FindWatchRootItem(DependencyObject? source)
    {
        DependencyObject? current = source;
        TreeViewItem? item = null;
        while (current != null)
        {
            if (current is TreeViewItem treeItem)
            {
                item = treeItem;
            }

            if (current is TreeView)
            {
                break;
            }

            current = current is TreeViewItem nested
                ? ItemsControl.ItemsControlFromItemContainer(nested)
                : VisualTreeHelper.GetParent(current);
        }

        while (item != null && ItemsControl.ItemsControlFromItemContainer(item) is TreeViewItem parent)
        {
            item = parent;
        }

        return item;
    }

    private void BreakpointsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BreakpointsListBox.SelectedItem is not Breakpoint breakpoint)
        {
            return;
        }

        var dialog = new Window
        {
            Title = "Breakpoint condition",
            Owner = this,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var box = new TextBox { Text = breakpoint.Condition ?? "", Margin = new Thickness(12) };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(12, 0, 12, 12), IsDefault = true };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Break when expression is truthy:", Margin = new Thickness(12, 12, 12, 0) });
        panel.Children.Add(box);
        panel.Children.Add(ok);
        dialog.Content = panel;
        ok.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() == true)
        {
            _debuggerService.SetBreakpointCondition(breakpoint.Line, breakpoint.FilePath, box.Text);
            UpdateBreakpointsPanel();
        }
    }

    private void ClearCurrentLineHighlight()
    {
        if (_currentLineRenderer != null)
        {
            _currentLineRenderer.CurrentLine = null;
            CodeEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    private bool UseJsInspect =>
        _focusedDebuggee == DebuggeeKind.JavaScript && _jsDebugger is { IsAttached: true };

    private bool ShouldDriveJsDebugger()
    {
        if (_jsDebugger is not { IsAttached: true })
        {
            return false;
        }

        if (_focusedDebuggee == DebuggeeKind.Interpret && _debuggerHook != null)
        {
            return false;
        }

        if (_focusedDebuggee == DebuggeeKind.JavaScript)
        {
            return true;
        }

        return _debuggerHook == null;
    }

    private void ResetDualDebugState()
    {
        _focusedDebuggee = DebuggeeKind.None;
        _interpretPaused = false;
        _jsPaused = false;
        _jsStepInFlight = false;
        _interpretDebugSessionActive = false;
        _dualDebugSession = false;
    }

    private void ApplyDualPauseState()
    {
        if (_interpretPaused || _jsPaused)
        {
            _debuggerService.Pause();
        }
        else
        {
            _debuggerService.Resume();
        }

        UpdateButtonStates();
    }

    private void RefreshDebugOutput(string? extraServerMessage = null, bool isError = false)
    {
        if (_dualDebugSession)
        {
            SetOutputText(FormatDualDebugOutput(extraServerMessage), isError);
            return;
        }

        if (_jsDebugger is { IsAttached: true })
        {
            var client = _jsDebugOutput.TrimEnd();
            if (!string.IsNullOrWhiteSpace(extraServerMessage))
            {
                client = string.IsNullOrEmpty(client)
                    ? extraServerMessage
                    : client + "\n" + extraServerMessage;
            }

            SetOutputText(client, isError);
            return;
        }

        var server = _executionService.GetCurrentOutput();
        if (!string.IsNullOrWhiteSpace(extraServerMessage))
        {
            server = string.IsNullOrEmpty(server)
                ? extraServerMessage
                : server.TrimEnd() + "\n" + extraServerMessage;
        }

        SetOutputText(server, isError);
    }

    private string FormatDualDebugOutput(string? extraServerMessage)
    {
        var builder = new StringBuilder();
        var server = _executionService.GetCurrentOutput();
        if (!string.IsNullOrWhiteSpace(extraServerMessage))
        {
            server = string.IsNullOrEmpty(server)
                ? extraServerMessage
                : server.TrimEnd() + "\n" + extraServerMessage;
        }

        if (!string.IsNullOrWhiteSpace(server))
        {
            builder.AppendLine("[server]");
            builder.AppendLine(server.TrimEnd());
        }

        var client = _jsDebugOutput.TrimEnd();
        if (!string.IsNullOrEmpty(client))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine("[client]");
            builder.AppendLine(client);
        }

        return builder.ToString().TrimEnd();
    }

    private void ShowInterpretPauseUi()
    {
        if (_debuggerHook == null)
        {
            return;
        }

        _focusedDebuggee = DebuggeeKind.Interpret;
        var line = _debuggerHook.Session.CurrentLine;
        var file = _debuggerHook.Session.CurrentFile;
        _debuggerService.SetCurrentLine(line, file ?? "main.malda");
        RefreshDebugOutput();
        var interpreter = _executionService.GetCurrentInterpreter();
        if (interpreter != null)
        {
            _debuggerHook.UpdateDebugInfo(interpreter);
        }

        UpdateDebugInfo();
        HighlightCurrentLine(line, file);
        SwitchToTab("debug");
        ApplyDualPauseState();
    }

    private void ShowJsPauseUi()
    {
        var snapshot = _jsDebugger?.LastPause;
        if (snapshot == null)
        {
            _focusedDebuggee = DebuggeeKind.JavaScript;
            UpdateDebugInfo();
            ApplyDualPauseState();
            return;
        }

        OnJsDebuggerPaused(snapshot);
    }

    private void FinishInterpretDebugSession()
    {
        _interpretDebugSessionActive = false;
        _interpretPaused = false;
        _debuggerHook?.Stop();
        _debuggerHook = null;

        if (_dualDebugSession && _jsDebugger is { IsAttached: true })
        {
            ApplyDualPauseState();
            if (_jsPaused)
            {
                ShowJsPauseUi();
            }
            else
            {
                ClearCurrentLineHighlight();
            }

            RefreshDebugOutput();
            UpdateDebugInfo();
            UpdateButtonStates();
            return;
        }

        ResetDualDebugState();
        _debuggerService.Stop();
        ClearCurrentLineHighlight();
        UpdateDebugInfo();
        UpdateButtonStates();
    }

    private async Task StartJsWebViewDebuggingAsync(string source, string fileName, bool dualDebug = false)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            string.Equals(fileName, "main.malda", StringComparison.Ordinal) ||
            !File.Exists(fileName))
        {
            throw new InvalidOperationException("Save the current file first so JavaScript debug can keep relative includes and asset paths working.");
        }

        var repoRoot = FindRepoRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("Could not locate the repository root needed for web preview assets.");
        }

        var artifact = WriteWebPreviewJavaScriptArtifact(repoRoot, source, fileName);
        if (string.IsNullOrWhiteSpace(artifact.SourceMapJson))
        {
            throw new InvalidOperationException("The JavaScript transpiler did not emit a source map for this file.");
        }

        var sourceMap = JsSourceMap.Parse(artifact.SourceMapJson);
        var hostPath = ResolveWebPreviewHostPath(repoRoot);
        var previewUri = BuildWebPreviewHostUri(
            hostPath,
            repoRoot,
            artifact.ScriptPath,
            Path.GetFileNameWithoutExtension(fileName));

        var core = await EnsureWebUiCoreAsync();
        if (core == null)
        {
            throw new InvalidOperationException("WebView2 is not available. Install the WebView2 runtime to debug JavaScript programs in the Desktop IDE.");
        }

        await StopJsDebuggerAsync();
        _jsDebugOutput = dualDebug
            ? "Debugging full-stack: host interpreter + JavaScript in Web Preview. Breakpoints map from this .malda file onto each runtime.\n"
            : "Debugging JavaScript in Web Preview. Breakpoints map from this .malda file onto the generated script.\n";
        _jsDebugger = new WebViewJsDebugger();
        _jsDebugger.Paused += snapshot => Dispatcher.Invoke(() => OnJsDebuggerPaused(snapshot));
        _jsDebugger.Resumed += () => Dispatcher.Invoke(OnJsDebuggerResumed);
        _jsDebugger.Output += text => Dispatcher.Invoke(() => AppendJsDebugOutput(text));
        _jsDebugger.Failed += message => Dispatcher.Invoke(() => AppendJsDebugOutput(message, isError: true));

        _debuggerService.Start();
        _focusedDebuggee = DebuggeeKind.JavaScript;
        await _jsDebugger.AttachAsync(core, sourceMap, Path.GetFileName(artifact.ScriptPath), fileName);
        await _jsDebugger.SyncBreakpointsAsync(_debuggerService.Breakpoints.ToList());
        await OpenUriInWebUiPanelAsync(previewUri, previewUri.AbsoluteUri, switchToTab: true, ensureUiHost: false);
        RefreshDebugOutput();
        UpdateButtonStates();
    }

    private void OnJsDebuggerPaused(JsDebugPauseSnapshot snapshot)
    {
        _jsPaused = true;
        _jsStepInFlight = false;
        _focusedDebuggee = DebuggeeKind.JavaScript;
        _debuggerService.SetCurrentLine(snapshot.Line, snapshot.File);
        _debuggerService.Pause();
        _debuggerService.UpdateCallStack(snapshot.Frames.ToList());
        HighlightCurrentLine(snapshot.Line, snapshot.File);
        SwitchToTab("debug");
        UpdateDebugInfo();
        ApplyDualPauseState();
    }

    private void OnJsDebuggerResumed()
    {
        _jsPaused = false;
        if (_jsStepInFlight)
        {
            ApplyDualPauseState();
            return;
        }

        if (_interpretPaused)
        {
            ShowInterpretPauseUi();
            return;
        }

        _debuggerService.Resume();
        ClearCurrentLineHighlight();
        UpdateButtonStates();
    }

    private void AppendJsDebugOutput(string text, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _jsDebugOutput = string.IsNullOrEmpty(_jsDebugOutput)
            ? text + "\n"
            : _jsDebugOutput + text + "\n";
        RefreshDebugOutput(isError: isError);
    }

    private void UpdateJsDebugInfo()
    {
        var snapshot = _jsDebugger?.LastPause;
        if (snapshot == null)
        {
            ClearInspectTrees();
            CallStackListBox.Items.Clear();
            UpdateBreakpointsPanel();
            return;
        }

        _suppressCallStackSelection = true;
        try
        {
            CallStackListBox.Items.Clear();
            for (var i = 0; i < snapshot.Frames.Count; i++)
            {
                var frameId = i + 1;
                CallStackListBox.Items.Add(new ListBoxItem
                {
                    Content = DebugInspectSnapshotBuilder.FormatFrame(snapshot.Frames[i]),
                    Tag = frameId
                });
            }

            if (CallStackListBox.Items.Count > 0)
            {
                var selectedIndex = Math.Clamp(_selectedDebugFrameId - 1, 0, CallStackListBox.Items.Count - 1);
                CallStackListBox.SelectedIndex = selectedIndex;
                _selectedDebugFrameId = selectedIndex + 1;
            }
            else
            {
                _selectedDebugFrameId = 1;
            }
        }
        finally
        {
            _suppressCallStackSelection = false;
        }

        RebuildJsVariablesTree();
        _ = RefreshJsWatchesAsync();
        UpdateBreakpointsPanel();
    }

    private void RebuildJsVariablesTree()
    {
        if (_jsDebugger == null)
        {
            return;
        }

        _suppressInspectExpansionTracking = true;
        try
        {
            VariablesTreeView.Items.Clear();
            foreach (var scope in _jsDebugger.GetCachedScopes(_selectedDebugFrameId))
            {
                VariablesTreeView.Items.Add(CreateInspectTreeItem(scope, VariablesInspectRoot));
            }

            RestoreInspectTree(VariablesTreeView.Items, VariablesInspectRoot);
        }
        finally
        {
            _suppressInspectExpansionTracking = false;
        }
    }

    private async Task ExpandJsInspectNodeAsync(TreeViewItem item, DebugInspectNode node)
    {
        if (_jsDebugger == null)
        {
            return;
        }

        var children = await _jsDebugger.ExpandAsync(node.VariablesReference, node.FrameId);
        item.Items.Clear();
        foreach (var child in children)
        {
            item.Items.Add(CreateInspectTreeItem(child, node.Path));
        }

        RestoreInspectTree(item.Items, node.Path);
    }

    private async Task RefreshJsWatchesAsync()
    {
        if (_jsDebugger == null)
        {
            RebuildWatchesTree(_watchExpressions
                .Select(value => new DebugInspectNode { Display = value, Name = value })
                .ToList());
            return;
        }

        var nodes = new List<DebugInspectNode>();
        foreach (var expression in _watchExpressions)
        {
            nodes.Add(await _jsDebugger.EvaluateWatchAsync(expression, _selectedDebugFrameId));
        }

        RebuildWatchesTree(nodes);
    }

    
    // Debug Menu
    private void DebugToggleBreakpoint_Click(object sender, RoutedEventArgs e)
    {
        var line = CodeEditor.TextArea.Caret.Line; // 1-based (same as DebuggerService / core)
        var activeDocument = GetActiveDocument();
        var filePath = GetPhysicalPath(activeDocument) ?? "main.malda";
        if (IsVirtualDocument(activeDocument))
        {
            line = VirtualDocumentCoordinateMapper.ToPhysicalLine(line, activeDocument.VirtualStartLine);
        }
        
        _debuggerService.ToggleBreakpoint(line, filePath);
        UpdateBreakpointVisuals();
        UpdateBreakpointsPanel();
        CodeEditor.TextArea.TextView.Redraw();
    }

    
    private void DebugClearBreakpoints_Click(object sender, RoutedEventArgs e)
    {
        var activeDocument = GetActiveDocument();
        var filePath = GetPhysicalPath(activeDocument) ?? "main.malda";
        _debuggerService.ClearBreakpointsForFile(filePath);
        _breakpointLines.Clear();
        CodeEditor.TextArea.TextView.Redraw();
    }
}
