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

// Windows API declarations for title bar customization
internal static class NativeMethods
{
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int attrValue, int attrSize);
    
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_CAPTION_COLOR = 35;
    public const int DWMWA_TEXT_COLOR = 36;
    public const int DWMWA_BORDER_COLOR = 34;
}

public partial class MainWindow : Window
{
    private enum FullStackRunChoice
    {
        Server,
        ClientPreview,
        FullStack
    }

    private sealed class SearchResultItem
    {
        public required string DocumentKey { get; init; }
        public required int Offset { get; init; }
        public required int Length { get; init; }
        public required int Line { get; init; }
        public required int Column { get; init; }
        public required string Preview { get; init; }
    }

    private sealed class SyntaxSnippet
    {
        public required string Id { get; init; }
        public required string Category { get; init; }
        public required string Label { get; init; }
        public required string Description { get; init; }
        public required string TemplateText { get; init; }
        public required string Preview { get; init; }
    }

    private sealed class OutlineNodeItem
    {
        public required string DisplayText { get; init; }
        public required DocumentSymbolInfo Symbol { get; init; }
        public List<OutlineNodeItem> Children { get; init; } = new();
    }

    private sealed class WorkspaceSymbolPickItem
    {
        public required string Display { get; init; }
        public required WorkspaceSymbolInfo Symbol { get; init; }
    }

    private readonly ExecutionService _executionService;
    private readonly DebuggerService _debuggerService;
    private readonly LanguageService _languageService;
    private readonly SymbolNavigationService _symbolNavigationService;
    private readonly WorkspaceFileSet _workspaceFiles = new();
    private readonly FileService _fileService;
    private readonly Services.CompilerService _compilerService;
    private readonly VirtualDocumentSegmentationService _virtualDocumentSegmentationService;
    private readonly EditorDiagnosticsService _editorDiagnosticsService = new();
    private readonly EditorQuickFixService _editorQuickFixService = new();
    private readonly ToolCallLogService _toolCallLogService;
    private readonly ThemeService _themeService;
    private readonly TypeAnalysisSettingsService _typeAnalysisSettingsService;
    private readonly CodeDiffService _codeDiffService;
    private readonly MCPServerConfigService _mcpConfigService;
    private readonly MCPServerConnectionService _mcpConnectionService;
    private UserControls.AIChatPanel? _aiChatPanel;
    private CurrentLineBackgroundRenderer? _currentLineRenderer;
    private SearchResultsBackgroundRenderer? _searchResultsRenderer;
    private SearchResultsBackgroundRenderer? _documentHighlightRenderer;
    private DiagnosticSquiggleRenderer? _diagnosticRenderer;
    private QuickFixMargin? _quickFixMargin;
    private DispatcherTimer? _documentHighlightTimer;
    private DebuggerHook? _debuggerHook;
    private readonly List<string> _watchExpressions = new();
    private readonly DebugInspectExpansionState _inspectExpansion = new();
    private int _selectedDebugFrameId = 1;
    private bool _suppressCallStackSelection;
    private bool _suppressInspectExpansionTracking;
    private Task? _debugTask;
    private CancellationTokenSource? _debugCancellation;
    private Task? _runTask;
    private CancellationTokenSource? _runCancellation;
    private Process? _activeRunProcess;
    private readonly object _activeRunProcessLock = new();
    private string _activeTab = "output";
    private DispatcherTimer? _diagnosticsTimer;
    private readonly List<int> _breakpointLines = new();
    private CompletionWindow? _completionWindow;
    private OverloadInsightWindow? _signatureHelpWindow;
    private SignatureHelpOverloadProvider? _signatureHelpProvider;
    private bool _isShowingModelLoadingError = false;
    private DispatcherTimer? _modelLoadingErrorTimer;
    private string? _lastDetectedWebUiUrl;
    private const string DefaultUiHostUrl = "http://localhost:50114";
    private const string PreviewArtifactsDirectoryName = ".malda-preview";
    private const string DefaultWebPreviewHostFileName = "program.html";
    private const string UntitledDocumentKey = "__untitled__";
    private const string VirtualDocumentPrefix = "#virtual:";
    private static readonly Regex IncludeStatementRegex = new(
        @"^\s*include\s+[""'](?<path>[^""']+)[""']\s*;",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private readonly Dictionary<string, OpenDocument> _openDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _documentOrder = new();
    private string _activeDocumentKey = UntitledDocumentKey;
    private bool _isSwitchingDocument;
    private readonly List<SearchResultItem> _searchResults = new();
    private readonly Dictionary<SearchResultItem, TreeViewItem> _searchResultNodes = new();
    private int _currentSearchResultIndex = -1;
    private readonly List<SyntaxSnippet> _syntaxSnippets = new();
    private List<DocumentSymbolInfo> _outlineSymbols = new();
    private bool _isSyntaxPanelVisible = true;
    private GridLength _syntaxPanelPreviousWidth = new(280, GridUnitType.Pixel);
    private PropertyRegressionArtifactRequest? _pendingRegressionRequest;
    private ToolTip? _hoverToolTip;
    private const string SnippetCaretMarker = "__CARET__";
    private bool _starterLauncherShown;
    private ExampleProgram? _currentExample;
    private bool _learningBranchBannerDismissed;

    public MainWindow()
    {
        InitializeComponent();
        
        // Setup keyboard shortcuts
        SetupKeyboardShortcuts();
        
        _executionService = new ExecutionService();
        _debuggerService = new DebuggerService();
        _languageService = new LanguageService();
        _symbolNavigationService = new SymbolNavigationService();
        _fileService = new FileService();
        _compilerService = new Services.CompilerService();
        _virtualDocumentSegmentationService = new VirtualDocumentSegmentationService();
        _toolCallLogService = new ToolCallLogService();
        _themeService = new ThemeService();
        _typeAnalysisSettingsService = new TypeAnalysisSettingsService();
        _typeAnalysisSettingsService.Load();
        _codeDiffService = new Services.CodeDiffService();
        _mcpConfigService = new MCPServerConfigService();
        _mcpConnectionService = new MCPServerConnectionService(_mcpConfigService);
        
        // Setup themes
        SetupThemes();
        
        // Subscribe to input requests
        var inputProvider = _executionService.GetInputProvider();
        if (inputProvider != null)
        {
            inputProvider.InputRequested += OnInputRequested;
            inputProvider.ConfirmRequested += OnConfirmRequested;
        }
        
        // Subscribe to output needs update event (e.g., during sleep)
        _executionService.OutputNeedsUpdate += OnOutputNeedsUpdate;
        
        // Subscribe to tool call logging
        _toolCallLogService.ToolCallLogged += OnToolCallLogged;
        
        // Set tool call log service in execution service
        _executionService.SetToolCallLogService(_toolCallLogService);
        
        // Setup tool calls browser to auto-scroll and zoom
        ToolCallsWebBrowser.LoadCompleted += ToolCallsWebBrowser_LoadCompleted;
        
        // Setup output browser zoom
        OutputWebBrowser.LoadCompleted += OutputWebBrowser_LoadCompleted;

        // Initialize modern embedded browser for Web UI panel.
        _ = InitializeWebUiPreviewAsync();
        
        SetupSyntaxHighlighting();
        SetupEditor();
        SetupExamples();
        SetupDataBinding();
        InitializeSyntaxPanel();
        InitializeDocumentSystem();
        SetupDiagnostics();
        
        _debuggerService.BreakpointsChanged += OnBreakpointsChanged;
        
        // Subscribe to theme changes
        _themeService.ThemeChanged += OnThemeChanged;
        
        // Initialize AI Chat Panel
        InitializeAIChatPanel();
        
        // Initialize MCP server connections
        _ = Task.Run(async () =>
        {
            try
            {
                await _mcpConnectionService.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize MCP connections: {ex.Message}");
            }
        });
        
        // Subscribe to model loading progress
        ModelLoadingService.OnProgressChanged += OnModelLoadingProgress;
        ModelLoadingService.OnLoadingStarted += OnModelLoadingStarted;
        ModelLoadingService.OnLoadingCompleted += OnModelLoadingCompleted;
        
        // Initialize View menu states and hook menu events
        Loaded += (s, e) =>
        {
            UpdateViewMenuStates();
            // Hook into menu item submenu opening to update popup background
            if (MainMenu != null)
            {
                MainMenu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnSubmenuOpened));
            }

            if (!_starterLauncherShown && string.IsNullOrWhiteSpace(CodeEditor.Text))
            {
                _starterLauncherShown = true;
                Dispatcher.BeginInvoke(() => ShowStarterLauncher(initialTrack: "student", fallbackToBlank: false));
            }
        };
    }
    
    private void OnModelLoadingProgress(ModelLoadingService.ModelLoadingProgress progress)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (progress.IsError)
            {
                // Stop any existing error timer
                if (_modelLoadingErrorTimer != null)
                {
                    _modelLoadingErrorTimer.Stop();
                    _modelLoadingErrorTimer = null;
                }
                
                // Mark that we're showing an error
                _isShowingModelLoadingError = true;
                
                // Show error message and hide progress bar immediately
                ModelLoadingOverlay.Visibility = Visibility.Visible;
                ModelLoadingText.Text = progress.Message;
                ModelLoadingProgressBar.Visibility = Visibility.Collapsed;
                ModelLoadingProgressBar.IsIndeterminate = false;
                // Stop any animation by setting a fixed value
                ModelLoadingProgressBar.Value = 0;
                
                // Keep overlay visible briefly to show error, then hide it
                _modelLoadingErrorTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                _modelLoadingErrorTimer.Tick += (s, e) =>
                {
                    _modelLoadingErrorTimer?.Stop();
                    _modelLoadingErrorTimer = null;
                    _isShowingModelLoadingError = false;
                    if (!ModelLoadingService.IsAnyModelLoading())
                    {
                        ModelLoadingOverlay.Visibility = Visibility.Collapsed;
                        ModelLoadingProgressBar.Visibility = Visibility.Visible;
                        ModelLoadingProgressBar.Value = 0;
                    }
                };
                _modelLoadingErrorTimer.Start();
            }
            else if (progress.IsLoading && progress.Percentage < 100)
            {
                // Reset error flag when normal loading resumes
                _isShowingModelLoadingError = false;
                if (_modelLoadingErrorTimer != null)
                {
                    _modelLoadingErrorTimer.Stop();
                    _modelLoadingErrorTimer = null;
                }
                
                ModelLoadingOverlay.Visibility = Visibility.Visible;
                ModelLoadingProgressBar.Visibility = Visibility.Visible;
                ModelLoadingText.Text = progress.Message;
                ModelLoadingProgressBar.Value = progress.Percentage;
                ModelLoadingProgressBar.IsIndeterminate = false;
            }
        });
    }
    
    private void OnModelLoadingStarted(ModelLoadingService.ModelLoadingProgress progress)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ModelLoadingOverlay.Visibility = Visibility.Visible;
            ModelLoadingText.Text = progress.Message;
            ModelLoadingProgressBar.Value = progress.Percentage;
            ModelLoadingProgressBar.IsIndeterminate = progress.Percentage < 10;
        });
    }
    
    private void OnModelLoadingCompleted(string modelPath)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Check if there are any other models still loading
            // Only hide if no models are loading AND we're not showing an error
            if (!ModelLoadingService.IsAnyModelLoading() && !_isShowingModelLoadingError)
            {
                ModelLoadingOverlay.Visibility = Visibility.Collapsed;
                ModelLoadingProgressBar.Value = 100;
                ModelLoadingProgressBar.Visibility = Visibility.Visible;
                ModelLoadingProgressBar.IsIndeterminate = false;
            }
        });
    }
    
    private void OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        // When a submenu opens, find the Popup and update its background
        if (e.OriginalSource is MenuItem menuItem)
        {
            // Find the Popup in the visual tree
            var popup = FindVisualChild<System.Windows.Controls.Primitives.Popup>(menuItem);
            if (popup != null && popup.Child is Border border)
            {
                border.Background = new SolidColorBrush(_themeService.CurrentTheme.InputBackground);
            }
        }
    }
    
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;
            
            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
                return childOfChild;
        }
        return null;
    }
    
    private void SetupKeyboardShortcuts()
    {
        // File menu shortcuts
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.New,
            (s, e) => FileNew_Click(s, e)));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Open,
            (s, e) => FileOpen_Click(s, e)));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Save,
            (s, e) => FileSave_Click(s, e)));
        var saveAsCommand = new RoutedCommand("SaveAs", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            saveAsCommand,
            (s, e) => FileSaveAs_Click(s, e)));
        InputBindings.Add(new KeyBinding(saveAsCommand, Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        
        // Edit menu shortcuts
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Undo,
            (s, e) => EditUndo_Click(s, e),
            (s, e) => e.CanExecute = CodeEditor.CanUndo));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Redo,
            (s, e) => EditRedo_Click(s, e),
            (s, e) => e.CanExecute = CodeEditor.CanRedo));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Cut,
            (s, e) => EditCut_Click(s, e),
            (s, e) => e.CanExecute = !string.IsNullOrEmpty(CodeEditor.SelectedText)));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Copy,
            (s, e) => EditCopy_Click(s, e),
            (s, e) => e.CanExecute = !string.IsNullOrEmpty(CodeEditor.SelectedText)));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Paste,
            (s, e) => EditPaste_Click(s, e),
            (s, e) => e.CanExecute = Clipboard.ContainsText()));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.SelectAll,
            (s, e) => EditSelectAll_Click(s, e)));
        
        // Find shortcut
        var findCommand = new RoutedCommand("Find", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            findCommand,
            (s, e) => EditFind_Click(s, e)));
        InputBindings.Add(new KeyBinding(findCommand, Key.F, ModifierKeys.Control));

        var replaceCommand = new RoutedCommand("Replace", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            replaceCommand,
            (s, e) => EditReplace_Click(s, e)));
        InputBindings.Add(new KeyBinding(replaceCommand, Key.H, ModifierKeys.Control));

        var findNextCommand = new RoutedCommand("FindNext", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            findNextCommand,
            (s, e) => NavigateSearchResults(1)));
        InputBindings.Add(new KeyBinding(findNextCommand, Key.F3, ModifierKeys.None));

        var findPreviousCommand = new RoutedCommand("FindPrevious", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            findPreviousCommand,
            (s, e) => NavigateSearchResults(-1)));
        InputBindings.Add(new KeyBinding(findPreviousCommand, Key.F3, ModifierKeys.Shift));

        var goToDefinitionCommand = new RoutedCommand("GoToDefinition", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            goToDefinitionCommand,
            (s, e) => NavigateGoToDefinition_Click(s, e)));
        InputBindings.Add(new KeyBinding(goToDefinitionCommand, Key.F12, ModifierKeys.None));

        var findReferencesCommand = new RoutedCommand("FindReferences", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            findReferencesCommand,
            (s, e) => NavigateFindReferences_Click(s, e)));
        InputBindings.Add(new KeyBinding(findReferencesCommand, Key.F12, ModifierKeys.Shift));

        var formatDocumentCommand = new RoutedCommand("FormatDocument", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            formatDocumentCommand,
            (s, e) => FormatDocumentAtCaret()));
        InputBindings.Add(new KeyBinding(formatDocumentCommand, Key.F, ModifierKeys.Control | ModifierKeys.Alt));

        var goToSymbolCommand = new RoutedCommand("GoToWorkspaceSymbol", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            goToSymbolCommand,
            (s, e) => GoToWorkspaceSymbol()));
        InputBindings.Add(new KeyBinding(goToSymbolCommand, Key.T, ModifierKeys.Control));

        var renameSymbolCommand = new RoutedCommand("RenameSymbol", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            renameSymbolCommand,
            (s, e) => NavigateRenameSymbol_Click(s, e)));
        InputBindings.Add(new KeyBinding(renameSymbolCommand, Key.R, ModifierKeys.Control | ModifierKeys.Alt));

        var quickFixCommand = new RoutedCommand("QuickFix", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            quickFixCommand,
            (s, e) => EditQuickFix_Click(s, e)));
        InputBindings.Add(new KeyBinding(quickFixCommand, Key.OemPeriod, ModifierKeys.Control));
        
        // Run shortcuts: F5 starts debug / continues; Ctrl+F5 runs without debugging; F9 toggles breakpoints.
        var runCommand = new RoutedCommand("RunWithoutDebugging", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            runCommand,
            (s, e) => RunButton_Click(s, e),
            (s, e) => e.CanExecute = DesktopEditorCommandPolicy.ResolveCtrlF5(GetEditorCommandContext()) != DesktopEditorCommand.None));
        InputBindings.Add(new KeyBinding(runCommand, Key.F5, ModifierKeys.Control));
        
        var stopCommand = new RoutedCommand("Stop", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            stopCommand,
            (s, e) => StopButton_Click(s, e)));
        InputBindings.Add(new KeyBinding(stopCommand, Key.F5, ModifierKeys.Shift));

        var reloadOpenFilesCommand = new RoutedCommand("ReloadOpenFiles", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            reloadOpenFilesCommand,
            (s, e) => ReloadOpenFilesButton_Click(s, e)));
        InputBindings.Add(new KeyBinding(reloadOpenFilesCommand, Key.R, ModifierKeys.Control | ModifierKeys.Shift));
        
        var compileCommand = new RoutedCommand("Compile", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            compileCommand,
            (s, e) => CompileButton_Click(s, e)));
        InputBindings.Add(new KeyBinding(compileCommand, Key.B, ModifierKeys.Control | ModifierKeys.Shift));

        var previewWebCommand = new RoutedCommand("PreviewWeb", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            previewWebCommand,
            (s, e) => PreviewWebButton_Click(s, e)));
        InputBindings.Add(new KeyBinding(previewWebCommand, Key.F6, ModifierKeys.None));

        var toggleSyntaxPanelCommand = new RoutedCommand("ToggleSyntaxPanel", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            toggleSyntaxPanelCommand,
            (s, e) => ToggleSyntaxPanelVisibility()));
        InputBindings.Add(new KeyBinding(toggleSyntaxPanelCommand, Key.L, ModifierKeys.Control | ModifierKeys.Shift));
        
        // Debug shortcuts
        var f5Command = new RoutedCommand("DebugOrContinue", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            f5Command,
            (s, e) => ExecutePrimaryF5(),
            (s, e) => e.CanExecute = DesktopEditorCommandPolicy.ResolveF5(GetEditorCommandContext()) != DesktopEditorCommand.None));
        InputBindings.Add(new KeyBinding(f5Command, Key.F5, ModifierKeys.None));
        
        var stepOverCommand = new RoutedCommand("StepOver", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            stepOverCommand,
            (s, e) => StepOverButton_Click(s, e)));
        InputBindings.Add(new KeyBinding(stepOverCommand, Key.F10, ModifierKeys.None));
        
        var stepIntoCommand = new RoutedCommand("StepInto", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            stepIntoCommand,
            (s, e) => StepIntoButton_Click(s, e)));
        InputBindings.Add(new KeyBinding(stepIntoCommand, Key.F11, ModifierKeys.None));
        
        var stepOutCommand = new RoutedCommand("StepOut", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            stepOutCommand,
            (s, e) => StepOutButton_Click(s, e)));
        InputBindings.Add(new KeyBinding(stepOutCommand, Key.F11, ModifierKeys.Shift));
        
        var toggleBreakpointCommand = new RoutedCommand("ToggleBreakpoint", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            toggleBreakpointCommand,
            (s, e) => DebugToggleBreakpoint_Click(s, e)));
        InputBindings.Add(new KeyBinding(toggleBreakpointCommand, Key.F9, ModifierKeys.None));
        
        // Update command states when editor changes
        CodeEditor.TextChanged += (s, e) =>
        {
            CommandManager.InvalidateRequerySuggested();
        };
        CodeEditor.TextArea.SelectionChanged += (s, e) =>
        {
            CommandManager.InvalidateRequerySuggested();
        };
    }
    
    private void InitializeAIChatPanel()
    {
        _aiChatPanel = AIChatPanelControl;
        if (_aiChatPanel != null)
        {
            _aiChatPanel.OnCodeChange += ApplyAICodeChange;
            _aiChatPanel.SetToolCallLogService(_toolCallLogService);
            UpdateAIChatPanelContext();
        }
    }
    
    private void SetupThemes()
    {
        ThemeComboBox.Items.Clear();
        foreach (var theme in _themeService.AvailableThemes)
        {
            var item = new ComboBoxItem 
            { 
                Content = theme.DisplayName, 
                Tag = theme
            };
            ThemeComboBox.Items.Add(item);
            if (theme.Name == _themeService.CurrentTheme.Name)
            {
                ThemeComboBox.SelectedItem = item;
            }
        }
        
        // Apply initial theme
        ApplyTheme(_themeService.CurrentTheme);
    }
    
    private void OnThemeChanged(object? sender, Theme theme)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyTheme(theme);
        });
    }
    
    private void ApplyTheme(Theme theme)
    {
        // Update all dynamic resources
        Resources["WindowBackgroundBrush"] = new SolidColorBrush(theme.WindowBackground);
        Resources["MainBackgroundBrush"] = new SolidColorBrush(theme.MainBackground);
        Resources["SidebarBackgroundBrush"] = new SolidColorBrush(theme.SidebarBackground);
        Resources["ToolbarBackgroundBrush"] = new SolidColorBrush(theme.ToolbarBackground);
        Resources["ToolbarBorderBrush"] = new SolidColorBrush(theme.ToolbarBorder);
        Resources["EditorBackgroundBrush"] = new SolidColorBrush(theme.EditorBackground);
        Resources["EditorForegroundBrush"] = new SolidColorBrush(theme.EditorForeground);
        Resources["EditorLineNumbersBrush"] = new SolidColorBrush(theme.EditorLineNumbers);
        Resources["TextForegroundBrush"] = new SolidColorBrush(theme.TextForeground);
        Resources["TextSecondaryBrush"] = new SolidColorBrush(theme.TextSecondary);
        Resources["ButtonBackgroundBrush"] = new SolidColorBrush(theme.ButtonBackground);
        Resources["ButtonForegroundBrush"] = new SolidColorBrush(theme.ButtonForeground);
        Resources["ButtonBorderBrush"] = new SolidColorBrush(theme.ButtonBorder);
        Resources["ButtonHoverBrush"] = new SolidColorBrush(theme.ButtonHover);
        Resources["ButtonHoverBorderBrush"] = new SolidColorBrush(theme.ButtonHoverBorder);
        Resources["PrimaryButtonBackgroundBrush"] = new SolidColorBrush(theme.PrimaryButtonBackground);
        Resources["PrimaryButtonForegroundBrush"] = new SolidColorBrush(theme.PrimaryButtonForeground);
        Resources["PrimaryButtonBorderBrush"] = new SolidColorBrush(theme.PrimaryButtonBorder);
        Resources["PrimaryButtonHoverBrush"] = new SolidColorBrush(theme.PrimaryButtonHover);
        Resources["PrimaryButtonHoverBorderBrush"] = new SolidColorBrush(theme.PrimaryButtonHoverBorder);
        Resources["TabBackgroundBrush"] = new SolidColorBrush(theme.TabBackground);
        Resources["TabActiveBackgroundBrush"] = new SolidColorBrush(theme.TabActiveBackground);
        Resources["TabForegroundBrush"] = new SolidColorBrush(theme.TabForeground);
        Resources["TabHoverBrush"] = new SolidColorBrush(theme.TabHover);
        Resources["InputBackgroundBrush"] = new SolidColorBrush(theme.InputBackground);
        Resources["InputForegroundBrush"] = new SolidColorBrush(theme.InputForeground);
        Resources["InputBorderBrush"] = new SolidColorBrush(theme.InputBorder);
        Resources["BorderBrush"] = new SolidColorBrush(theme.BorderColor);
        Resources["GridSplitterBackgroundBrush"] = new SolidColorBrush(theme.GridSplitterBackground);
        Resources["ListBackgroundBrush"] = new SolidColorBrush(theme.ListBackground);
        Resources["ListForegroundBrush"] = new SolidColorBrush(theme.ListForeground);
        Resources["ListBorderBrush"] = new SolidColorBrush(theme.ListBorder);
        Resources["DebugAccentBrush"] = new SolidColorBrush(theme.DebugAccent);
        Resources["ErrorBrush"] = new SolidColorBrush(theme.ErrorColor);
        Resources["WarningBrush"] = new SolidColorBrush(theme.WarningColor);
        Resources["InfoBrush"] = new SolidColorBrush(theme.InfoColor);
        EditorPopupTheming.PublishApplicationResources(theme);
        if (_completionWindow != null)
        {
            EditorPopupTheming.Apply(_completionWindow, theme);
        }

        if (_signatureHelpWindow != null)
        {
            EditorPopupTheming.Apply(_signatureHelpWindow, theme);
        }

        if (_hoverToolTip != null)
        {
            EditorPopupTheming.Apply(_hoverToolTip, theme);
        }
        
        // Update system menu colors for submenu popups
        // This ensures submenu backgrounds use the correct theme color
        Resources[SystemColors.MenuBrushKey] = new SolidColorBrush(theme.InputBackground);
        Resources[SystemColors.MenuTextBrushKey] = new SolidColorBrush(theme.TextForeground);
        
        // Also update at Application level to ensure it propagates to all windows
        Application.Current.Resources[SystemColors.MenuBrushKey] = new SolidColorBrush(theme.InputBackground);
        Application.Current.Resources[SystemColors.MenuTextBrushKey] = new SolidColorBrush(theme.TextForeground);
        
        // Force update of any open menu popups by invalidating visual tree
        MainMenu?.InvalidateVisual();
        
        // Calculate scrollbar colors based on theme
        // For dark themes, make scrollbar slightly lighter than background
        // For light themes, make scrollbar slightly darker than background
        var isDarkTheme = theme.ListBackground.R < 128;
        Color scrollBarTrack, scrollBarThumb, scrollBarThumbHover, scrollBarButton;
        
        if (isDarkTheme)
        {
            // Dark theme: slightly lighter colors
            scrollBarTrack = Color.FromRgb(
                (byte)Math.Min(255, theme.ListBackground.R + 30),
                (byte)Math.Min(255, theme.ListBackground.G + 30),
                (byte)Math.Min(255, theme.ListBackground.B + 30));
            scrollBarThumb = Color.FromRgb(
                (byte)Math.Min(255, theme.ListBackground.R + 60),
                (byte)Math.Min(255, theme.ListBackground.G + 60),
                (byte)Math.Min(255, theme.ListBackground.B + 60));
            scrollBarThumbHover = Color.FromRgb(
                (byte)Math.Min(255, theme.ListBackground.R + 80),
                (byte)Math.Min(255, theme.ListBackground.G + 80),
                (byte)Math.Min(255, theme.ListBackground.B + 80));
            scrollBarButton = Color.FromRgb(
                (byte)Math.Min(255, theme.ListBackground.R + 50),
                (byte)Math.Min(255, theme.ListBackground.G + 50),
                (byte)Math.Min(255, theme.ListBackground.B + 50));
        }
        else
        {
            // Light theme: slightly darker colors
            scrollBarTrack = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 20),
                (byte)Math.Max(0, theme.ListBackground.G - 20),
                (byte)Math.Max(0, theme.ListBackground.B - 20));
            scrollBarThumb = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 40),
                (byte)Math.Max(0, theme.ListBackground.G - 40),
                (byte)Math.Max(0, theme.ListBackground.B - 40));
            scrollBarThumbHover = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 60),
                (byte)Math.Max(0, theme.ListBackground.G - 60),
                (byte)Math.Max(0, theme.ListBackground.B - 60));
            scrollBarButton = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 30),
                (byte)Math.Max(0, theme.ListBackground.G - 30),
                (byte)Math.Max(0, theme.ListBackground.B - 30));
        }
        
        Resources["ScrollBarTrackBrush"] = new SolidColorBrush(scrollBarTrack);
        Resources["ScrollBarThumbBrush"] = new SolidColorBrush(scrollBarThumb);
        Resources["ScrollBarThumbHoverBrush"] = new SolidColorBrush(scrollBarThumbHover);
        Resources["ScrollBarButtonBrush"] = new SolidColorBrush(scrollBarButton);
        
        // Update current line highlight color to match theme
        _currentLineRenderer?.SetColor(theme.DebugAccent);
        
        // Update tab button backgrounds
        UpdateTabButtonBackgrounds();
        
        // Update syntax highlighting for the new theme
        UpdateSyntaxHighlighting();
        
        // Refresh WebBrowser content to apply new theme colors
        // Note: WebBrowser doesn't support Background property, so we rely on HTML background
        // Always update the output panel to apply theme colors, even when empty
        SetOutputText(_executionService?.GetCurrentOutput() ?? "");
        UpdateToolCallsDisplay();
        
        // Update window title bar colors to match theme
        UpdateTitleBarColors(theme);
    }
    
    private void UpdateTitleBarColors(Theme theme)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                // Window handle not available yet, try again after window is loaded
                if (!IsLoaded)
                {
                    Loaded += OnWindowLoadedForTitleBar;
                }
                return;
            }
            
            ApplyTitleBarColors(hwnd, theme);
        }
        catch
        {
            // If DWM API calls fail (e.g., on older Windows versions), silently ignore
            // The window will use default title bar styling
        }
    }
    
    private void OnWindowLoadedForTitleBar(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoadedForTitleBar;
        UpdateTitleBarColors(_themeService.CurrentTheme);
    }
    
    private void ApplyTitleBarColors(IntPtr hwnd, Theme theme)
    {
        try
        {
            
            // Determine if this is a dark theme
            var isDarkTheme = theme.WindowBackground.R < 128;
            
            // Set dark mode for title bar
            int darkMode = isDarkTheme ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            
            // Set title bar background color (caption color)
            // Convert WPF Color to ARGB int (0xAARRGGBB format)
            var captionColor = (int)((255 << 24) | (theme.WindowBackground.R << 16) | (theme.WindowBackground.G << 8) | theme.WindowBackground.B);
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            
            // Set title bar text color
            var textColor = (int)((255 << 24) | (theme.TextForeground.R << 16) | (theme.TextForeground.G << 8) | theme.TextForeground.B);
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
            
            // Set border color (optional, uses caption color if not set)
            var borderColor = (int)((255 << 24) | (theme.BorderColor.R << 16) | (theme.BorderColor.G << 8) | theme.BorderColor.B);
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
        catch
        {
            // If DWM API calls fail (e.g., on older Windows versions), silently ignore
            // The window will use default title bar styling
        }
    }
    
    private void UpdateTitleBarColorsForWindow(Window window, Theme theme)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ApplyTitleBarColors(hwnd, theme);
            }
            else
            {
                // Window handle not available yet, try again after window is loaded
                window.Loaded += (s, e) =>
                {
                    var hwnd2 = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    if (hwnd2 != IntPtr.Zero)
                    {
                        ApplyTitleBarColors(hwnd2, theme);
                    }
                };
            }
        }
        catch
        {
            // If DWM API calls fail, silently ignore
        }
    }
    
    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is Theme theme)
        {
            _themeService.SetTheme(theme.Name);
        }
    }
    
    private void ToolCallsWebBrowser_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        // Scroll to bottom when content loads and set zoom
        // The JavaScript in the HTML handles this, but we also try here as a backup
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (ToolCallsWebBrowser.Document != null)
                {
                    // Set zoom to 150% for better readability
                    SetWebBrowserZoom(ToolCallsWebBrowser, 150);
                    
                    var script = "window.scrollTo(0, document.body.scrollHeight);";
                    dynamic doc = ToolCallsWebBrowser.Document;
                    if (doc != null)
                    {
                        dynamic window = doc.parentWindow;
                        if (window != null)
                        {
                            window.execScript(script, "JavaScript");
                        }
                    }
                }
            }
            catch
            {
                // JavaScript in HTML will handle scrolling if this fails
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    
    private void OutputWebBrowser_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        // Set zoom to 150% for better readability
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                SetWebBrowserZoom(OutputWebBrowser, 150);
            }
            catch
            {
                // Ignore zoom errors
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    
    private void OnToolCallLogged(ToolCallLogEntry entry)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateToolCallsDisplay();
        });
    }
    
    private string ColorToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
    
    private void SetWebBrowserZoom(System.Windows.Controls.WebBrowser browser, int zoomPercent = 150)
    {
        try
        {
            if (browser.Document != null)
            {
                dynamic doc = browser.Document;
                if (doc != null)
                {
                    dynamic window = doc.parentWindow;
                    if (window != null)
                    {
                        // Use CSS zoom property - works reliably in IE/Trident engine
                        var script = $@"
                            (function() {{
                                document.body.style.zoom = '{zoomPercent}%';
                                if (document.documentElement) {{
                                    document.documentElement.style.zoom = '{zoomPercent}%';
                                }}
                            }})();
                        ";
                        window.execScript(script, "JavaScript");
                    }
                }
            }
        }
        catch
        {
            // If zoom fails, the increased font sizes will still make text more readable
        }
    }
    
    private string GetScrollbarCss(Theme theme)
    {
        // Calculate scrollbar colors based on theme (same logic as in ApplyTheme)
        var isDarkTheme = theme.ListBackground.R < 128;
        Color scrollBarTrack, scrollBarThumb, scrollBarThumbHover;
        
        if (isDarkTheme)
        {
            // Dark theme: use darker, more visible colors
            // For very dark backgrounds, use medium-dark grays for better visibility
            var bgLuminance = (theme.ListBackground.R + theme.ListBackground.G + theme.ListBackground.B) / 3.0;
            if (bgLuminance < 30)
            {
                // Very dark background - use medium-dark grays
                scrollBarTrack = Color.FromRgb(40, 40, 40);
                scrollBarThumb = Color.FromRgb(70, 70, 70);
                scrollBarThumbHover = Color.FromRgb(90, 90, 90);
            }
            else
            {
                // Moderately dark background - slightly lighter
                scrollBarTrack = Color.FromRgb(
                    (byte)Math.Min(255, theme.ListBackground.R + 30),
                    (byte)Math.Min(255, theme.ListBackground.G + 30),
                    (byte)Math.Min(255, theme.ListBackground.B + 30));
                scrollBarThumb = Color.FromRgb(
                    (byte)Math.Min(255, theme.ListBackground.R + 60),
                    (byte)Math.Min(255, theme.ListBackground.G + 60),
                    (byte)Math.Min(255, theme.ListBackground.B + 60));
                scrollBarThumbHover = Color.FromRgb(
                    (byte)Math.Min(255, theme.ListBackground.R + 80),
                    (byte)Math.Min(255, theme.ListBackground.G + 80),
                    (byte)Math.Min(255, theme.ListBackground.B + 80));
            }
        }
        else
        {
            // Light theme: slightly darker colors
            scrollBarTrack = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 20),
                (byte)Math.Max(0, theme.ListBackground.G - 20),
                (byte)Math.Max(0, theme.ListBackground.B - 20));
            scrollBarThumb = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 40),
                (byte)Math.Max(0, theme.ListBackground.G - 40),
                (byte)Math.Max(0, theme.ListBackground.B - 40));
            scrollBarThumbHover = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 60),
                (byte)Math.Max(0, theme.ListBackground.G - 60),
                (byte)Math.Max(0, theme.ListBackground.B - 60));
        }
        
        // Generate CSS for scrollbars - support multiple browsers
        // Note: WPF WebBrowser uses IE/Trident engine which has limited scrollbar styling support
        // IE/Edge (Trident) scrollbar properties - these must be on body/html elements
        var ieScrollbarCss = $@"
        body, html {{
            scrollbar-base-color: {ColorToHex(scrollBarTrack)};
            scrollbar-face-color: {ColorToHex(scrollBarThumb)};
            scrollbar-track-color: {ColorToHex(scrollBarTrack)};
            scrollbar-arrow-color: {ColorToHex(scrollBarThumb)};
            scrollbar-shadow-color: {ColorToHex(scrollBarThumb)};
            scrollbar-highlight-color: {ColorToHex(scrollBarThumbHover)};
            scrollbar-3dlight-color: {ColorToHex(scrollBarTrack)};
            scrollbar-darkshadow-color: {ColorToHex(scrollBarThumb)};
        }}
        ";
        
        // Webkit scrollbars (Chrome, Edge Chromium, Safari) - for newer engines
        var webkitScrollbarCss = $@"
        ::-webkit-scrollbar {{
            width: 12px !important;
            height: 12px !important;
        }}
        ::-webkit-scrollbar-track {{
            background: {ColorToHex(scrollBarTrack)} !important;
        }}
        ::-webkit-scrollbar-thumb {{
            background: {ColorToHex(scrollBarThumb)} !important;
            border-radius: 6px !important;
        }}
        ::-webkit-scrollbar-thumb:hover {{
            background: {ColorToHex(scrollBarThumbHover)} !important;
        }}
        ::-webkit-scrollbar-corner {{
            background: {ColorToHex(scrollBarTrack)} !important;
        }}
        ";
        
        // Firefox scrollbars
        var firefoxScrollbarCss = $@"
        * {{
            scrollbar-width: thin !important;
            scrollbar-color: {ColorToHex(scrollBarThumb)} {ColorToHex(scrollBarTrack)} !important;
        }}
        ";
        
        return ieScrollbarCss + webkitScrollbarCss + firefoxScrollbarCss;
    }
    
    private void UpdateToolCallsDisplay()
    {
        var theme = _themeService.CurrentTheme;
        var entries = _toolCallLogService.GetEntriesForHtml();
        var contextUsage = _toolCallLogService.GetContextUsage();
        
        var html = new StringBuilder();
        html.AppendLine("<html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><meta charset=\"UTF-8\"><style>");
        html.AppendLine($"html {{ background: {ColorToHex(theme.ListBackground)}; margin: 0; padding: 0; height: 100%; }}");
        html.AppendLine($"body {{ font-family: Consolas, Monaco, monospace; font-size: 16px; color: {ColorToHex(theme.TextForeground)}; background: {ColorToHex(theme.ListBackground)}; padding: 8px; margin: 0; min-height: 100vh; }}");
        var primaryColor = theme.PrimaryButtonBackground;
        var primaryDark = Color.FromRgb((byte)(primaryColor.R * 0.7), (byte)(primaryColor.G * 0.7), (byte)(primaryColor.B * 0.7));
        html.AppendLine($".context-summary {{ background: {ColorToHex(primaryColor)} !important; color: {ColorToHex(theme.PrimaryButtonForeground)} !important; padding: 12px; border-radius: 6px; margin-top: 12px; margin-bottom: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.2); border: 2px solid {ColorToHex(primaryDark)}; }}");
        html.AppendLine($".context-summary-title {{ font-weight: bold; font-size: 13px; margin-bottom: 8px; color: {ColorToHex(theme.PrimaryButtonForeground)} !important; }}");
        html.AppendLine(".context-summary-stats { display: flex; flex-wrap: wrap; gap: 24px; margin-bottom: 8px; }");
        html.AppendLine(".context-stat { display: flex; flex-direction: column; min-width: 80px; padding-right: 12px; }");
        html.AppendLine($".context-stat-label {{ font-size: 10px; margin-bottom: 2px; color: {ColorToHex(theme.PrimaryButtonForeground)} !important; opacity: 0.9; }}");
        html.AppendLine($".context-stat-value {{ font-size: 14px; font-weight: bold; color: {ColorToHex(theme.PrimaryButtonForeground)} !important; }}");
        html.AppendLine(".context-total-bar { width: 100%; height: 8px; background: rgba(255,255,255,0.3); border-radius: 4px; margin-top: 8px; overflow: hidden; }");
        html.AppendLine(".context-total-bar-fill { height: 100%; transition: width 0.3s ease; background: #FFFFFF; }");
        var entryBg = theme.ListBackground.R < 128 ? Color.FromRgb((byte)(theme.ListBackground.R + 20), (byte)(theme.ListBackground.G + 20), (byte)(theme.ListBackground.B + 20)) : Color.FromRgb((byte)(theme.ListBackground.R - 10), (byte)(theme.ListBackground.G - 10), (byte)(theme.ListBackground.B - 10));
        html.AppendLine($".entry {{ margin-bottom: 12px; padding: 8px; background: {ColorToHex(entryBg)}; border-left: 3px solid {ColorToHex(theme.DebugAccent)}; }}");
        html.AppendLine($".timestamp {{ color: {ColorToHex(theme.TextSecondary)}; font-size: 15px; }}");
        html.AppendLine($".tool-name {{ color: {ColorToHex(theme.DebugAccent)}; font-weight: bold; font-size: 18px; margin: 4px 0; }}");
        html.AppendLine($".label {{ color: {ColorToHex(theme.TextSecondary)}; font-size: 15px; margin-top: 4px; }}");
        html.AppendLine($".arguments {{ color: {ColorToHex(theme.TextForeground)}; font-size: 16px; margin-left: 16px; white-space: pre-wrap; word-wrap: break-word; }}");
        html.AppendLine($".result {{ color: #4caf50; font-size: 16px; margin-left: 16px; white-space: pre-wrap; word-wrap: break-word; }}");
        html.AppendLine($".error {{ color: {ColorToHex(theme.ErrorColor)}; font-size: 16px; margin-left: 16px; white-space: pre-wrap; word-wrap: break-word; }}");
        html.AppendLine(".size-badge { display: inline-block; padding: 2px 6px; border-radius: 3px; font-size: 10px; font-weight: bold; margin-left: 6px; }");
        html.AppendLine(".size-small { background: #c8e6c9; color: #2e7d32; }");
        html.AppendLine(".size-medium { background: #fff9c4; color: #f57f17; }");
        html.AppendLine(".size-large { background: #ffe0b2; color: #e65100; }");
        html.AppendLine(".size-xlarge { background: #ffcdd2; color: #c62828; }");
        html.AppendLine(".size-bar-container { width: 100%; height: 4px; background: #e0e0e0; border-radius: 2px; margin-top: 2px; overflow: hidden; }");
        html.AppendLine(".size-bar { height: 100%; transition: width 0.3s ease; }");
        html.AppendLine(".size-bar-small { background: #4caf50; }");
        html.AppendLine(".size-bar-medium { background: #ffc107; }");
        html.AppendLine(".size-bar-large { background: #ff9800; }");
        html.AppendLine(".size-bar-xlarge { background: #f44336; }");
        html.AppendLine(".copy-button { display: inline-block; margin-left: 8px; padding: 4px 8px; background: #d32f2f; color: white; border: none; border-radius: 3px; cursor: pointer; font-size: 10px; font-weight: bold; }");
        html.AppendLine(".copy-button:hover { background: #b71c1c; }");
        html.AppendLine(".copy-button:active { background: #8b0000; }");
        html.AppendLine(".copy-button.copied { background: #4caf50; }");
        html.AppendLine(GetScrollbarCss(theme));
        html.AppendLine("</style></head><body>");
        
        if (entries.Count == 0)
        {
            html.AppendLine($"<p style='color: {ColorToHex(theme.TextSecondary)}; font-style: italic; padding: 8px;'>No tool calls yet. Tool calls will appear here when agents use tools.</p>");
        }
        else
        {
            // Collect full arguments data for entries with errors
            var fullArgumentsData = new Dictionary<int, string>();
            int entryIndex = 0;
            
            foreach (var entry in entries)
            {
                html.AppendLine("<div class='entry'>");
                html.AppendLine($"<div class='timestamp'>[{entry.Timestamp:HH:mm:ss}]</div>");
                html.AppendLine($"<div class='tool-name'>🔧 {System.Security.SecurityElement.Escape(entry.ToolName)}</div>");
                
                if (!string.IsNullOrEmpty(entry.Arguments))
                {
                    var escapedArgs = System.Security.SecurityElement.Escape(entry.Arguments);
                    var argsSizeInfo = GetSizeInfo(entry.ArgumentsSize);
                    html.AppendLine($"<div class='label'>📥 Arguments: <span class='size-badge {argsSizeInfo.Class}'>{argsSizeInfo.Label}</span></div>");
                    html.AppendLine($"<div class='size-bar-container'><div class='size-bar {argsSizeInfo.BarClass}' style='width: {argsSizeInfo.Percentage:F1}%'></div></div>");
                    html.AppendLine($"<div class='arguments'>{escapedArgs}</div>");
                }
                
                if (!string.IsNullOrEmpty(entry.Result))
                {
                    var escapedResult = System.Security.SecurityElement.Escape(entry.Result);
                    var resultClass = entry.IsError ? "error" : "result";
                    var resultLabel = entry.IsError ? "❌ Error:" : "✅ Result:";
                    var resultSizeInfo = GetSizeInfo(entry.ResultSize);
                    html.AppendLine($"<div class='label'>{resultLabel} <span class='size-badge {resultSizeInfo.Class}'>{resultSizeInfo.Label}</span>");
                    
                    // Add copy button for errors with full arguments available
                    if (entry.IsError && !string.IsNullOrEmpty(entry.FullArguments) && entry.FullArguments != entry.Arguments)
                    {
                        fullArgumentsData[entryIndex] = entry.FullArguments;
                        html.AppendLine($"<button class='copy-button' onclick='copyFullArguments({entryIndex}, this)' title='Copy full tool parameters'>📋 Copy Parameters</button>");
                    }
                    
                    html.AppendLine("</div>");
                    html.AppendLine($"<div class='size-bar-container'><div class='size-bar {resultSizeInfo.BarClass}' style='width: {resultSizeInfo.Percentage:F1}%'></div></div>");
                    html.AppendLine($"<div class='{resultClass}'>{escapedResult}</div>");
                }
                
                html.AppendLine("</div>");
                entryIndex++;
            }
            
            // Add script with full arguments data (always initialize, even if empty)
            html.AppendLine("<script type='text/javascript'>");
            html.AppendLine("var fullArgumentsData = {};");
            foreach (var kvp in fullArgumentsData)
            {
                var jsonEscaped = System.Text.Json.JsonSerializer.Serialize(kvp.Value);
                html.AppendLine($"fullArgumentsData[{kvp.Key}] = {jsonEscaped};");
            }
            html.AppendLine("</script>");
            
            // Add context summary at the bottom (so it's visible when auto-scrolling)
            var totalSizeInfo = GetSizeInfo(contextUsage.TotalSize);
            // Estimate token usage (rough approximation: 1 token ≈ 4 characters)
            var estimatedTokens = contextUsage.TotalSize / 4;
            var tokenPercentage = Math.Min(100, (estimatedTokens / 200000.0) * 100); // Assuming 200K token context window
            
            html.AppendLine("<div class='context-summary' style='background-color: #1565C0; color: #FFFFFF; padding: 12px; border-radius: 6px; margin-top: 12px; margin-bottom: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.2); border: 2px solid #0D47A1;'>");
            html.AppendLine("<div class='context-summary-title' style='font-weight: bold; font-size: 13px; margin-bottom: 8px; color: #FFFFFF;'>📊 Conversation Context Usage</div>");
            html.AppendLine("<div class='context-summary-stats' style='display: flex; flex-wrap: wrap; gap: 24px; margin-bottom: 8px;'>");
            html.AppendLine($"<div class='context-stat' style='display: flex; flex-direction: column; min-width: 80px; padding-right: 12px;'><span class='context-stat-label' style='font-size: 10px; margin-bottom: 6px; color: #E3F2FD;'>Tool Calls:</span><span class='context-stat-value' style='font-size: 14px; font-weight: bold; color: #FFFFFF;'>{contextUsage.ToolCallCount}</span></div>");
            html.AppendLine($"<div class='context-stat' style='display: flex; flex-direction: column; min-width: 80px; padding-right: 12px;'><span class='context-stat-label' style='font-size: 10px; margin-bottom: 6px; color: #E3F2FD;'>Arguments:</span><span class='context-stat-value' style='font-size: 14px; font-weight: bold; color: #FFFFFF;'>{FormatSize(contextUsage.TotalArgumentsSize)}</span></div>");
            html.AppendLine($"<div class='context-stat' style='display: flex; flex-direction: column; min-width: 80px; padding-right: 12px;'><span class='context-stat-label' style='font-size: 10px; margin-bottom: 6px; color: #E3F2FD;'>Results:</span><span class='context-stat-value' style='font-size: 14px; font-weight: bold; color: #FFFFFF;'>{FormatSize(contextUsage.TotalResultSize)}</span></div>");
            html.AppendLine($"<div class='context-stat' style='display: flex; flex-direction: column; min-width: 80px; padding-right: 12px;'><span class='context-stat-label' style='font-size: 10px; margin-bottom: 6px; color: #E3F2FD;'>Total Context:</span><span class='context-stat-value' style='font-size: 14px; font-weight: bold; color: #FFFFFF;'>{FormatSize(contextUsage.TotalSize)}</span></div>");
            html.AppendLine($"<div class='context-stat' style='display: flex; flex-direction: column; min-width: 80px; padding-right: 12px;'><span class='context-stat-label' style='font-size: 10px; margin-bottom: 6px; color: #E3F2FD;'>Est. Tokens:</span><span class='context-stat-value' style='font-size: 14px; font-weight: bold; color: #FFFFFF;'>{estimatedTokens:N0}</span></div>");
            html.AppendLine("</div>");
            html.AppendLine($"<div class='context-total-bar' style='width: 100%; height: 8px; background: rgba(255,255,255,0.3); border-radius: 4px; margin-top: 8px; overflow: hidden;'><div class='context-total-bar-fill' style='width: {tokenPercentage:F1}%; height: 100%; background: #FFFFFF;'></div></div>");
            html.AppendLine($"<div style='font-size: 10px; margin-top: 4px; color: #E3F2FD;'>Estimated token usage: {tokenPercentage:F1}% of 200K context window</div>");
            html.AppendLine("</div>");
        }
        
        html.AppendLine("<script>");
        html.AppendLine("window.onload = function() { window.scrollTo(0, document.body.scrollHeight); };");
        html.AppendLine("function copyFullArguments(index, buttonElement) {");
        html.AppendLine("    if (typeof fullArgumentsData !== 'undefined' && fullArgumentsData[index]) {");
        html.AppendLine("        var text = fullArgumentsData[index];");
        html.AppendLine("        var textArea = document.createElement('textarea');");
        html.AppendLine("        textArea.value = text;");
        html.AppendLine("        textArea.style.position = 'fixed';");
        html.AppendLine("        textArea.style.left = '-999999px';");
        html.AppendLine("        textArea.style.top = '-999999px';");
        html.AppendLine("        document.body.appendChild(textArea);");
        html.AppendLine("        textArea.focus();");
        html.AppendLine("        textArea.select();");
        html.AppendLine("        try {");
        html.AppendLine("            var successful = document.execCommand('copy');");
        html.AppendLine("            if (successful && buttonElement) {");
        html.AppendLine("                var originalText = buttonElement.innerHTML;");
        html.AppendLine("                buttonElement.innerHTML = '✓ Copied';");
        html.AppendLine("                buttonElement.classList.add('copied');");
        html.AppendLine("                setTimeout(function() {");
        html.AppendLine("                    buttonElement.innerHTML = originalText;");
        html.AppendLine("                    buttonElement.classList.remove('copied');");
        html.AppendLine("                }, 2000);");
        html.AppendLine("            }");
        html.AppendLine("        } catch (err) {");
        html.AppendLine("            // Silently handle copy errors");
        html.AppendLine("        }");
        html.AppendLine("        document.body.removeChild(textArea);");
        html.AppendLine("    }");
        html.AppendLine("}");
        html.AppendLine("</script>");
        html.AppendLine("</body></html>");
        
        ToolCallsWebBrowser.NavigateToString(html.ToString());
    }
    
    private (string Label, string Class, string BarClass, double Percentage) GetSizeInfo(int size)
    {
        // Size thresholds (in characters)
        const int smallThreshold = 1000;      // < 1KB
        const int mediumThreshold = 5000;     // < 5KB
        const int largeThreshold = 20000;      // < 20KB
        // >= 20KB is xlarge
        
        string label;
        string cssClass;
        string barClass;
        double percentage;
        
        if (size < smallThreshold)
        {
            label = FormatSize(size);
            cssClass = "size-small";
            barClass = "size-bar-small";
            percentage = Math.Min(100, (size / (double)smallThreshold) * 25); // Use 25% of bar for small
        }
        else if (size < mediumThreshold)
        {
            label = FormatSize(size);
            cssClass = "size-medium";
            barClass = "size-bar-medium";
            percentage = 25 + Math.Min(25, ((size - smallThreshold) / (double)(mediumThreshold - smallThreshold)) * 25); // 25-50%
        }
        else if (size < largeThreshold)
        {
            label = FormatSize(size);
            cssClass = "size-large";
            barClass = "size-bar-large";
            percentage = 50 + Math.Min(25, ((size - mediumThreshold) / (double)(largeThreshold - mediumThreshold)) * 25); // 50-75%
        }
        else
        {
            label = FormatSize(size);
            cssClass = "size-xlarge";
            barClass = "size-bar-xlarge";
            // For xlarge, show percentage based on a max of 100KB
            percentage = 75 + Math.Min(25, (size / 100000.0) * 25); // 75-100%
        }
        
        return (label, cssClass, barClass, percentage);
    }
    
    private string FormatSize(int size)
    {
        if (size < 1024)
            return $"{size} B";
        else if (size < 1024 * 1024)
            return $"{size / 1024.0:F1} KB";
        else
            return $"{size / (1024.0 * 1024.0):F2} MB";
    }
    
    private void OnOutputNeedsUpdate()
    {
        // Update output panel (e.g., during sleep)
        try
        {
            Dispatcher.Invoke(() =>
            {
                if (_executionService != null)
                {
                    SetOutputText(_executionService.GetCurrentOutput());
                    SwitchToTab("output"); // Ensure output tab is visible
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnOutputNeedsUpdate: {ex.Message}");
        }
    }
    
    private void OnInputRequested(string prompt)
    {
        // Show input dialog on UI thread
        try
        {
            Dispatcher.Invoke(() =>
            {
                // Update output before showing input dialog so user can see progress
                if (_executionService != null)
                {
                    SetOutputText(_executionService.GetCurrentOutput());
                    SwitchToTab("output"); // Ensure output tab is visible
                }
                
                DesktopInputProvider? provider = null;
                try
                {
                    if (_executionService == null)
                    {
                        // Execution service is null - this shouldn't happen, but if it does,
                        // we can't provide input. The TaskCompletionSource will hang.
                        // This is a critical error condition.
                        System.Diagnostics.Debug.WriteLine("ERROR: _executionService is null in OnInputRequested");
                        return;
                    }
                    
                    provider = _executionService.GetInputProvider();
                    if (provider == null)
                    {
                        // Input provider is null - this shouldn't happen, but if it does,
                        // we can't provide input. The TaskCompletionSource will hang.
                        // This is a critical error condition.
                        System.Diagnostics.Debug.WriteLine("ERROR: inputProvider is null in OnInputRequested");
                        return;
                    }
                    
                    // Get current theme for dialog styling
                    var theme = _themeService.CurrentTheme;
                    
                    var inputDialog = new Window
                    {
                        Title = "Input Required",
                        Width = 400,
                        Height = 180,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Owner = this,
                        ResizeMode = ResizeMode.NoResize,
                        Background = new SolidColorBrush(theme.WindowBackground)
                    };
                    
                    var stackPanel = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Background = new SolidColorBrush(theme.WindowBackground)
                    };
                    
                    var promptText = new TextBlock
                    {
                        Text = prompt ?? "Enter value:",
                        Margin = new Thickness(0, 0, 0, 10),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(theme.TextForeground)
                    };
                    
                    var inputTextBox = new TextBox
                    {
                        Name = "InputTextBox",
                        Margin = new Thickness(0, 0, 0, 10),
                        Background = new SolidColorBrush(theme.InputBackground),
                        Foreground = new SolidColorBrush(theme.InputForeground),
                        BorderBrush = new SolidColorBrush(theme.InputBorder)
                    };
                    
                    var buttonPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    
                    // Create round button template using FrameworkElementFactory
                    ControlTemplate roundButtonTemplate;
                    try
                    {
                        roundButtonTemplate = new ControlTemplate(typeof(Button));
                        var borderFactory = new FrameworkElementFactory(typeof(Border));
                        borderFactory.Name = "border";
                        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                        borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
                        borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
                        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
                        borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
                        
                        var contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
                        contentPresenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                        contentPresenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                        borderFactory.AppendChild(contentPresenterFactory);
                        
                        roundButtonTemplate.VisualTree = borderFactory;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error creating round button template: {ex.Message}");
                        // Fallback to null - buttons will use default template
                        roundButtonTemplate = null;
                    }
                    
                    var okButton = new Button
                    {
                        Content = "OK",
                        Width = 75,
                        Height = 30,
                        Margin = new Thickness(5, 0, 0, 0),
                        IsDefault = true,
                        Background = new SolidColorBrush(theme.PrimaryButtonBackground),
                        Foreground = new SolidColorBrush(theme.PrimaryButtonForeground),
                        BorderBrush = new SolidColorBrush(theme.PrimaryButtonBorder),
                        BorderThickness = new Thickness(1)
                    };
                    if (roundButtonTemplate != null)
                    {
                        okButton.Template = roundButtonTemplate;
                    }
                    
                    okButton.Click += (s, e) =>
                    {
                        string inputValue = "";
                        try
                        {
                            // Safely get input text
                            if (inputTextBox != null)
                            {
                                inputValue = inputTextBox.Text ?? "";
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error getting input text: {ex.Message}");
                            inputValue = "";
                        }
                        
                        try
                        {
                            // Use the captured provider variable to queue input
                            if (provider != null)
                            {
                                provider.QueueInput(inputValue);
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("ERROR: provider is null in OK button click");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log error but continue
                            System.Diagnostics.Debug.WriteLine($"Error queuing input: {ex.Message}");
                        }
                        
                        try
                        {
                            inputDialog.DialogResult = true;
                            inputDialog.Close();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error closing dialog: {ex.Message}");
                        }
                    };
                    
                    var cancelButton = new Button
                    {
                        Content = "Cancel",
                        Width = 75,
                        Height = 30,
                        Margin = new Thickness(5, 0, 0, 0),
                        IsCancel = true,
                        Background = new SolidColorBrush(theme.ButtonBackground),
                        Foreground = new SolidColorBrush(theme.ButtonForeground),
                        BorderBrush = new SolidColorBrush(theme.ButtonBorder),
                        BorderThickness = new Thickness(1)
                    };
                    if (roundButtonTemplate != null)
                    {
                        cancelButton.Template = roundButtonTemplate;
                    }
                    
                    // Add hover effects for buttons
                    okButton.MouseEnter += (s, e) => 
                    {
                        okButton.Background = new SolidColorBrush(theme.PrimaryButtonHover);
                        okButton.BorderBrush = new SolidColorBrush(theme.PrimaryButtonHoverBorder);
                    };
                    okButton.MouseLeave += (s, e) => 
                    {
                        okButton.Background = new SolidColorBrush(theme.PrimaryButtonBackground);
                        okButton.BorderBrush = new SolidColorBrush(theme.PrimaryButtonBorder);
                    };
                    
                    cancelButton.MouseEnter += (s, e) => 
                    {
                        cancelButton.Background = new SolidColorBrush(theme.ButtonHover);
                        cancelButton.BorderBrush = new SolidColorBrush(theme.ButtonHoverBorder);
                    };
                    cancelButton.MouseLeave += (s, e) => 
                    {
                        cancelButton.Background = new SolidColorBrush(theme.ButtonBackground);
                        cancelButton.BorderBrush = new SolidColorBrush(theme.ButtonBorder);
                    };
                    
                    cancelButton.Click += (s, e) =>
                    {
                        try
                        {
                            // Use the captured provider variable
                            if (provider != null)
                            {
                                provider.QueueInput(""); // Provide empty string on cancel
                            }
                            inputDialog.DialogResult = false;
                            inputDialog.Close();
                        }
                        catch (Exception ex)
                        {
                            // Log error but still close dialog and provide empty input
                            System.Diagnostics.Debug.WriteLine($"Error in Cancel button click: {ex.Message}");
                            try
                            {
                                if (provider != null)
                                {
                                    provider.QueueInput("");
                                }
                            }
                            catch { }
                            inputDialog.DialogResult = false;
                            inputDialog.Close();
                        }
                    };
                    
                    buttonPanel.Children.Add(okButton);
                    buttonPanel.Children.Add(cancelButton);
                    
                    stackPanel.Children.Add(promptText);
                    stackPanel.Children.Add(inputTextBox);
                    stackPanel.Children.Add(buttonPanel);
                    
                    inputDialog.Content = stackPanel;
                    inputDialog.Loaded += (s, e) => 
                    {
                        inputTextBox.Focus();
                        // Apply title bar colors to the dialog window
                        UpdateTitleBarColorsForWindow(inputDialog, theme);
                    };
                    
                    // Handle dialog closing without clicking a button (e.g., X button)
                    inputDialog.Closed += (s, e) =>
                    {
                        try
                        {
                            // If dialog was closed without setting DialogResult, provide empty input
                            if (inputDialog.DialogResult == null)
                            {
                                // Use the captured provider variable
                                if (provider != null)
                                {
                                    provider.QueueInput(""); // Provide empty string if closed without OK/Cancel
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error in dialog Closed event: {ex.Message}");
                            try
                            {
                                if (provider != null)
                                {
                                    provider.QueueInput("");
                                }
                            }
                            catch { }
                        }
                    };
                    
                    var result = inputDialog.ShowDialog();
                    
                    // Ensure input is provided even if dialog was closed unexpectedly
                    if (result != true && result != false)
                    {
                        try
                        {
                            // Use the captured provider variable
                            if (provider != null)
                            {
                                provider.QueueInput(""); // Provide empty string as fallback
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error providing fallback input: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If dialog creation fails, provide empty input to prevent hang
                    System.Diagnostics.Debug.WriteLine($"Error creating input dialog: {ex.Message}");
                    try
                    {
                        // Use the provider variable if it was successfully captured, otherwise get a new one
                        if (provider == null && _executionService != null)
                        {
                            provider = _executionService.GetInputProvider();
                        }
                        if (provider != null)
                        {
                            provider.QueueInput("");
                        }
                    }
                    catch
                    {
                        // Ignore errors when trying to provide fallback input
                    }
                }
            });
        }
        catch (Exception ex)
        {
            // If Dispatcher.Invoke fails, try to provide empty input
            System.Diagnostics.Debug.WriteLine($"Error in OnInputRequested: {ex.Message}");
            try
            {
                var provider = _executionService?.GetInputProvider();
                if (provider != null)
                {
                    provider.QueueInput("");
                }
            }
            catch
            {
                // Ignore errors
            }
        }
    }

    private void OnConfirmRequested(string message, TaskCompletionSource<bool> completion)
    {
        try
        {
            Dispatcher.Invoke(() =>
            {
                if (_executionService != null)
                {
                    SetOutputText(_executionService.GetCurrentOutput());
                    SwitchToTab("output");
                }

                var theme = _themeService.CurrentTheme;
                var dialog = new Window
                {
                    Title = "Allow Command?",
                    Width = 480,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    Background = new SolidColorBrush(theme.WindowBackground)
                };

                var stackPanel = new StackPanel
                {
                    Margin = new Thickness(20),
                    Background = new SolidColorBrush(theme.WindowBackground)
                };

                stackPanel.Children.Add(new TextBlock
                {
                    Text = message ?? "Allow this command?",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16),
                    Foreground = new SolidColorBrush(theme.TextForeground)
                });

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var denyButton = new Button
                {
                    Content = "Deny",
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0),
                    IsCancel = true,
                    Background = new SolidColorBrush(theme.ButtonBackground),
                    Foreground = new SolidColorBrush(theme.ButtonForeground),
                    BorderBrush = new SolidColorBrush(theme.ButtonBorder),
                    BorderThickness = new Thickness(1)
                };

                var allowButton = new Button
                {
                    Content = "Allow",
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0),
                    IsDefault = true,
                    Background = new SolidColorBrush(theme.PrimaryButtonBackground),
                    Foreground = new SolidColorBrush(theme.PrimaryButtonForeground),
                    BorderBrush = new SolidColorBrush(theme.PrimaryButtonBorder),
                    BorderThickness = new Thickness(1)
                };

                denyButton.Click += (_, _) =>
                {
                    try { completion.TrySetResult(false); } catch { }
                    dialog.DialogResult = false;
                    dialog.Close();
                };

                allowButton.Click += (_, _) =>
                {
                    try { completion.TrySetResult(true); } catch { }
                    dialog.DialogResult = true;
                    dialog.Close();
                };

                buttonPanel.Children.Add(denyButton);
                buttonPanel.Children.Add(allowButton);
                stackPanel.Children.Add(buttonPanel);
                dialog.Content = stackPanel;
                dialog.Closed += (_, _) => completion.TrySetResult(false);
                dialog.ShowDialog();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnConfirmRequested: {ex.Message}");
            completion.TrySetResult(false);
        }
    }

    private void SetupExamples()
    {
        // Examples are now loaded from files via ExampleBrowserWindow
        // This method is kept for compatibility but no longer populates a ComboBox
    }

    private void SetupDataBinding()
    {
        UpdateButtonStates();
    }

    private void BrowseExamplesButton_Click(object sender, RoutedEventArgs e)
    {
        var browserWindow = new Windows.ExampleBrowserWindow(_themeService);
        browserWindow.Owner = this;
        browserWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        
        if (browserWindow.ShowDialog() == true && browserWindow.SelectedExample != null)
        {
            LoadExample(browserWindow.SelectedExample);
        }
    }
    
    private void LoadExample(MaldaLang.IDE.ExampleProgram example)
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
        
        // Clear breakpoints for the old file before loading new example
        var oldFilePath = GetCurrentPhysicalFilePath();
        _debuggerService.ClearBreakpointsForFile(oldFilePath);
        
        var resolvedExamplePath = TryResolveExampleAbsolutePath(example.AbsoluteFilePath);
        if (string.IsNullOrWhiteSpace(resolvedExamplePath))
        {
            resolvedExamplePath = TryResolveExampleAbsolutePath(example.FilePath);
        }
        if (!string.IsNullOrWhiteSpace(resolvedExamplePath) && File.Exists(resolvedExamplePath))
        {
            OpenFileAndIncludedDocuments(resolvedExamplePath);
        }
        else
        {
            ResetToSingleDocument(example.Code, null);
            UpdateAIChatPanelContext(); // Fallback to inline example code
        }

        SetCurrentExample(example);
    }

    private bool OpenExampleByRelativePath(string? relativeExamplePath, string statusPrefix)
    {
        var example = ExampleProgramsService.GetExampleByRelativePath(relativeExamplePath);
        if (example == null)
        {
            MessageBox.Show(this, "The requested example could not be loaded.", "Example Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        LoadExample(example);
        SetOutputText($"{statusPrefix}: {example.Name}");
        return true;
    }

    private void ShowStarterLauncher(string initialTrack, bool fallbackToBlank)
    {
        var starterWindow = new Windows.StarterLauncherWindow(_themeService, initialTrack);
        starterWindow.Owner = this;
        starterWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        if (starterWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(starterWindow.SelectedExampleRelativePath))
        {
            if (OpenExampleByRelativePath(starterWindow.SelectedExampleRelativePath, "Loaded starter"))
            {
                return;
            }
        }

        if (starterWindow.BrowseExamplesRequested)
        {
            BrowseExamplesButton_Click(this, new RoutedEventArgs());
            return;
        }

        if (starterWindow.StartBlankRequested || fallbackToBlank)
        {
            ResetToSingleDocument("", null);
            UpdateAIChatPanelContext();
        }
    }

    private void OutputTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("output");
    }

    private void DebugTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("debug");
    }

    private void ErrorsTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("errors");
    }

    private void SearchTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("search");
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }
    
    private void ToolCallsTabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("toolcalls");
        UpdateToolCallsDisplay();
    }
    
    private void AITabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("ai");
        UpdateAIChatPanelContext();
    }

    private void WebUITabButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchToTab("webui");
    }
    
    private void ClearToolCallsButton_Click(object sender, RoutedEventArgs e)
    {
        _toolCallLogService.Clear();
        UpdateToolCallsDisplay();
    }

    private void SwitchToTab(string tab)
    {
        _activeTab = tab;
        
        OutputPanel.Visibility = tab == "output" ? Visibility.Visible : Visibility.Collapsed;
        DebugPanel.Visibility = tab == "debug" ? Visibility.Visible : Visibility.Collapsed;
        ToolCallsPanel.Visibility = tab == "toolcalls" ? Visibility.Visible : Visibility.Collapsed;
        ErrorsPanel.Visibility = tab == "errors" ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = tab == "search" ? Visibility.Visible : Visibility.Collapsed;
        AIChatPanel.Visibility = tab == "ai" ? Visibility.Visible : Visibility.Collapsed;
        WebUIPanel.Visibility = tab == "webui" ? Visibility.Visible : Visibility.Collapsed;
        
        // Update button styles using theme colors
        UpdateTabButtonBackgrounds();
        
        // Update View menu check states
        UpdateViewMenuStates();
    }
    
    private void UpdateViewMenuStates()
    {
        // Find the View menu item and update its children
        if (MainMenu == null) return;
        
        foreach (var topLevelItem in MainMenu.Items)
        {
            if (topLevelItem is not MenuItem menuItem) continue;
            
            var header = menuItem.Header?.ToString()?.Replace("_", "");
            if (header == "View")
            {
                foreach (var childItem in menuItem.Items)
                {
                    if (childItem is not MenuItem childMenuItem) continue;
                    
                    var itemHeader = childMenuItem.Header?.ToString()?.Replace("_", "");
                    if (itemHeader == "Show Syntax Panel")
                        childMenuItem.IsChecked = _isSyntaxPanelVisible;
                    else if (itemHeader == "Show Output Panel")
                        childMenuItem.IsChecked = _activeTab == "output";
                    else if (itemHeader == "Show Debug Panel")
                        childMenuItem.IsChecked = _activeTab == "debug";
                    else if (itemHeader == "Show Tool Calls Panel")
                        childMenuItem.IsChecked = _activeTab == "toolcalls";
                    else if (itemHeader == "Show Errors Panel")
                        childMenuItem.IsChecked = _activeTab == "errors";
                    else if (itemHeader == "Show Search Panel")
                        childMenuItem.IsChecked = _activeTab == "search";
                    else if (itemHeader == "Show AI Panel")
                        childMenuItem.IsChecked = _activeTab == "ai";
                    else if (itemHeader == "Show Web UI Panel")
                        childMenuItem.IsChecked = _activeTab == "webui";
                    else if (itemHeader == "Type Errors as Errors")
                        childMenuItem.IsChecked = _typeAnalysisSettingsService.TypeErrors;
                }
                break;
            }
        }
    }

    private void ViewToggleTypeErrors_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
            return;

        _typeAnalysisSettingsService.SetTypeErrors(menuItem.IsChecked);
        UpdateViewMenuStates();
        UpdateDiagnostics();
    }
    
    private void UpdateTabButtonBackgrounds()
    {
        var theme = _themeService.CurrentTheme;
        var activeBrush = new SolidColorBrush(theme.TabActiveBackground);
        var inactiveBrush = new SolidColorBrush(theme.TabBackground);
        var accentBrush = new SolidColorBrush(theme.DebugAccent);
        var transparent = Brushes.Transparent;

        ApplySidebarTabChrome(OutputTabButton, _activeTab == "output", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(DebugTabButton, _activeTab == "debug", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(ToolCallsTabButton, _activeTab == "toolcalls", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(ErrorsTabButton, _activeTab == "errors", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(SearchTabButton, _activeTab == "search", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(AITabButton, _activeTab == "ai", activeBrush, inactiveBrush, accentBrush, transparent);
        ApplySidebarTabChrome(WebUITabButton, _activeTab == "webui", activeBrush, inactiveBrush, accentBrush, transparent);
        RefreshDocumentTabs();
    }

    private static void ApplySidebarTabChrome(Button button, bool isActive, Brush activeBrush, Brush inactiveBrush, Brush accentBrush, Brush transparent)
    {
        button.Background = isActive ? activeBrush : inactiveBrush;
        button.BorderBrush = isActive ? accentBrush : transparent;
        button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
    }
    
    private void UpdateAIChatPanelContext()
    {
        if (_aiChatPanel != null)
        {
            _aiChatPanel.CurrentCode = GetCurrentCode();
            _aiChatPanel.CursorPosition = GetCursorPosition();
            _aiChatPanel.SelectedCode = GetSelectedText();
            var activeDocument = GetActiveDocument();
            var (source, sourceKey) = GetSourceForAnalysis(activeDocument);
            _aiChatPanel.Errors = _languageService.GetDiagnostics(
                source,
                sourceKey,
                strictTypesOptions: _typeAnalysisSettingsService.ToOptions());
        }
    }
    
    private void ApplyAICodeChange(string newCode)
    {
        SetEditorTextUndoable(newCode);
        _fileService.SetContent(newCode);
        // Update diagnostics
        _diagnosticsTimer?.Stop();
        _diagnosticsTimer?.Start();
        // Update AI chat panel context
        UpdateAIChatPanelContext();
    }

    private void SetCurrentExample(ExampleProgram? example)
    {
        _currentExample = example;
        _learningBranchBannerDismissed = false;
        UpdateLearningBranchBanner();
    }

    private void CloseLearningBranchBanner_Click(object sender, RoutedEventArgs e)
    {
        _learningBranchBannerDismissed = true;
        UpdateLearningBranchBanner();
    }

    private void UpdateLearningBranchBanner()
    {
        if (LearningBranchBanner == null || LearningBranchButtonsPanel == null)
        {
            return;
        }

        LearningBranchButtonsPanel.Children.Clear();

        if (_learningBranchBannerDismissed || _currentExample == null || !IsCoreLearningExample(_currentExample))
        {
            LearningBranchBanner.Visibility = Visibility.Collapsed;
            return;
        }

        LearningBranchTitleTextBlock.Text = $"Recommended next tracks after {_currentExample.Name}";
        LearningBranchDescriptionTextBlock.Text = $"After the core path, branch into {StarterCatalog.GetBranchTitleSummary()}.";

        foreach (var branch in StarterCatalog.GetBranches())
        {
            var button = new Button
            {
                Content = $"{branch.Title} ({branch.EstimatedTime})",
                Tag = branch.RelativeExamplePath,
                ToolTip = branch.Description,
                Margin = new Thickness(4),
                Padding = new Thickness(10, 6, 10, 6)
            };
            button.Click += LearningBranchButton_Click;
            LearningBranchButtonsPanel.Children.Add(button);
        }

        LearningBranchBanner.Visibility = Visibility.Visible;
    }

    private static bool IsCoreLearningExample(ExampleProgram example)
    {
        return example.Track.Equals("student", StringComparison.OrdinalIgnoreCase) &&
               (example.Category.Equals("Basics", StringComparison.OrdinalIgnoreCase) ||
                example.Category.Equals("OOP", StringComparison.OrdinalIgnoreCase));
    }
    
    // View Menu
    private void ViewThemeLight_Click(object sender, RoutedEventArgs e)
    {
        _themeService.SetTheme("Light");
        ThemeComboBox.SelectedItem = ThemeComboBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is Theme theme && theme.Name == "Light");
    }
    
    private void ViewThemeDark_Click(object sender, RoutedEventArgs e)
    {
        _themeService.SetTheme("Dark");
        ThemeComboBox.SelectedItem = ThemeComboBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is Theme theme && theme.Name == "Dark");
    }
    
    private void ViewThemeHighContrast_Click(object sender, RoutedEventArgs e)
    {
        _themeService.SetTheme("HighContrast");
        ThemeComboBox.SelectedItem = ThemeComboBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is Theme theme && theme.Name == "HighContrast");
    }
    
    private void ViewToggleOutputPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                OutputTabButton_Click(sender, e);
            }
            else
            {
                // If hiding, switch to another tab
                if (_activeTab == "output")
                {
                    SwitchToTab("errors");
                }
            }
            UpdateViewMenuStates();
        }
    }
    
    private void ViewToggleDebugPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                DebugTabButton_Click(sender, e);
            }
            else
            {
                if (_activeTab == "debug")
                {
                    SwitchToTab("output");
                }
            }
            UpdateViewMenuStates();
        }
    }
    
    private void ViewToggleToolCallsPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                ToolCallsTabButton_Click(sender, e);
            }
            else
            {
                if (_activeTab == "toolcalls")
                {
                    SwitchToTab("output");
                }
            }
            UpdateViewMenuStates();
        }
    }
    
    private void ViewToggleErrorsPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                ErrorsTabButton_Click(sender, e);
            }
            else
            {
                if (_activeTab == "errors")
                {
                    SwitchToTab("output");
                }
            }
            UpdateViewMenuStates();
        }
    }

    private void ViewToggleSearchPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                SearchTabButton_Click(sender, e);
            }
            else if (_activeTab == "search")
            {
                SwitchToTab("output");
            }

            UpdateViewMenuStates();
        }
    }
    
    private void ViewToggleAIPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                AITabButton_Click(sender, e);
            }
            else
            {
                if (_activeTab == "ai")
                {
                    SwitchToTab("output");
                }
            }
            UpdateViewMenuStates();
        }
    }

    private void ViewToggleWebUIPanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            if (menuItem.IsChecked)
            {
                WebUITabButton_Click(sender, e);
            }
            else
            {
                if (_activeTab == "webui")
                {
                    SwitchToTab("output");
                }
            }
            UpdateViewMenuStates();
        }
    }

    private void WebUiGoButton_Click(object sender, RoutedEventArgs e)
    {
        var target = WebUiUrlTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var uri = TryResolveWebViewUri(target);
        if (uri == null)
        {
            MessageBox.Show(this, "Enter a valid URL or a local HTML file path.", "Web UI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _ = OpenUriInWebUiPanelAsync(uri, target, switchToTab: true, ensureUiHost: !uri.IsFile);
    }

    private void WebUiOpenExternalButton_Click(object sender, RoutedEventArgs e)
    {
        var target = WebUiUrlTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var uri = TryResolveWebViewUri(target);
        var externalTarget = uri?.IsFile == true ? uri.LocalPath : uri?.AbsoluteUri ?? target;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = externalTarget,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open URL: {ex.Message}", "Web UI", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TryAutoOpenWebUiFromOutput(string outputText)
    {
        if (string.IsNullOrWhiteSpace(outputText))
        {
            return;
        }

        var match = Regex.Match(outputText, @"Open:\s*(https?://\S+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return;
        }

        var detectedUrl = match.Groups[1].Value.Trim();
        if (string.Equals(detectedUrl, _lastDetectedWebUiUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _lastDetectedWebUiUrl = detectedUrl;
        _ = OpenWebUiInPanelAsync(detectedUrl, switchToTab: true);
    }

    private async Task OpenWebUiInPanelAsync(string url, bool switchToTab)
    {
        var uri = TryResolveWebViewUri(url);
        if (uri == null)
        {
            throw new InvalidOperationException("The requested Web UI target is not a valid URL or local file.");
        }

        await OpenUriInWebUiPanelAsync(uri, url, switchToTab, ensureUiHost: !uri.IsFile);
    }

    private async Task OpenUriInWebUiPanelAsync(Uri uri, string displayText, bool switchToTab, bool ensureUiHost)
    {
        if (ensureUiHost)
        {
            await EnsureUiHostRunningAsync();
        }

        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                WebUiUrlTextBox.Text = displayText;
                WebUiWebView.Source = uri;
                if (switchToTab)
                {
                    SwitchToTab("webui");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open Web UI: {ex.Message}", "Web UI", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private static Uri? TryResolveWebViewUri(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        if (Uri.TryCreate(target, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp ||
             absoluteUri.Scheme == Uri.UriSchemeHttps ||
             absoluteUri.Scheme == Uri.UriSchemeFile))
        {
            return absoluteUri;
        }

        var expandedPath = System.Environment.ExpandEnvironmentVariables(target);
        if (File.Exists(expandedPath))
        {
            return new Uri(Path.GetFullPath(expandedPath));
        }

        return null;
    }

    private async Task EnsureUiHostRunningAsync()
    {
        var uiHostUrl = System.Environment.GetEnvironmentVariable("MALDA_UI_HOST_URL");
        if (string.IsNullOrWhiteSpace(uiHostUrl))
        {
            uiHostUrl = DefaultUiHostUrl;
        }

        if (await IsUiHostHealthyAsync(uiHostUrl))
        {
            return;
        }

        try
        {
            await EmbeddedUiHostRuntime.TryStartAsync();
            await WaitForUiHostAsync(uiHostUrl);
        }
        catch
        {
            // Keep web preview usable even if embedded UIHost startup fails.
        }
    }

    private static async Task<bool> IsUiHostHealthyAsync(string uiHostUrl)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await client.GetAsync($"{uiHostUrl.TrimEnd('/')}/health");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WaitForUiHostAsync(string uiHostUrl)
    {
        const int maxAttempts = 120;
        for (var i = 0; i < maxAttempts; i++)
        {
            if (await IsUiHostHealthyAsync(uiHostUrl))
            {
                return;
            }
            await Task.Delay(500);
        }
    }

    private static string? FindRepoRoot()
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            var current = new DirectoryInfo(dir);
            while (current != null)
            {
                var sourceMarker = Path.Combine(current.FullName, "MaldaLang.UIHost");
                var manualIndex = Path.Combine(current.FullName, "ReferenceManual", "index.html");
                var distBin = Path.Combine(current.FullName, "bin");
                var hasDistLayout =
                    File.Exists(manualIndex) &&
                    (Directory.Exists(Path.Combine(distBin, "desktop-ide")) ||
                     Directory.Exists(Path.Combine(distBin, "ui-host")) ||
                     Directory.Exists(Path.Combine(distBin, "malda")));

                if (Directory.Exists(sourceMarker) || hasDistLayout)
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
        }
        catch
        {
            // Best effort only.
        }
        return null;
    }

    private async Task InitializeWebUiPreviewAsync()
    {
        try
        {
            await WebUiWebView.EnsureCoreWebView2Async();
            WebUiWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            WebUiWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebUiWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
        }
        catch
        {
            // Keep IDE functional even if WebView2 runtime is unavailable.
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }

    private void ViewOpenTrace_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "MALDA trace files (*.malda-trace.jsonl)|*.malda-trace.jsonl|All files (*.*)|*.*",
            Title = "Open Trace File"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var session = TraceViewerService.LoadTrace(dlg.FileName);
            var window = new Windows.TraceViewerWindow(_themeService, session);
            window.Owner = this;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Trace load failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    
    // Tools Menu
    private void ToolsConfigureMCPServers_Click(object sender, RoutedEventArgs e)
    {
        var window = new Windows.MCPServerConfigWindow(_mcpConfigService, _mcpConnectionService, _themeService);
        window.Owner = this;
        window.ShowDialog();
    }

    private void ToolsBrowseModels_Click(object sender, RoutedEventArgs e)
    {
        var window = new MaldaLang.DesktopIDE.Windows.ModelBrowserWindow(_themeService);
        window.ShowDialog();
    }

    private void ToolsPackageManager_Click(object sender, RoutedEventArgs e)
    {
        var window = new MaldaLang.DesktopIDE.Windows.PackageBrowserWindow(_themeService);
        window.Owner = this;
        window.ShowDialog();
    }

    private void ToolsLoadExample_Click(object sender, RoutedEventArgs e)
    {
        // Focus the example combo box and show it
        // ExampleComboBox removed - examples are now browsed via ExampleBrowserWindow
        // Could also open a dialog here, but for now just focus the combo
    }
    
    // Help Menu
    private void HelpAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "MALDA: The AI-First Programming Language - Desktop IDE\n\n" +
            "Version 1.0\n\n" +
            "A desktop IDE for the Multi Agent Language with Development Automation (MALDA).\n" +
            "Built with WPF and AvalonEdit.\n\n" +
            "(c) 2026 - Andrea Maldini",
            "About",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
    
    private void HelpDocumentation_Click(object sender, RoutedEventArgs e)
    {
        if (TryOpenReferenceManual())
        {
            return;
        }

        OpenDocumentationUrl();
    }

    private void HelpReferenceManual_Click(object sender, RoutedEventArgs e)
    {
        if (TryOpenReferenceManual())
        {
            return;
        }

        MessageBox.Show(
            this,
            "Could not find the local reference manual. Opening online documentation instead.",
            "Reference Manual",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        OpenDocumentationUrl();
    }

    private bool TryOpenReferenceManual()
    {
        try
        {
            var repoRoot = FindRepoRoot();
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                return false;
            }

            var manualPath = Path.Combine(repoRoot, "ReferenceManual", "index.html");
            if (!File.Exists(manualPath))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = manualPath,
                UseShellExecute = true
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private const string ProjectRepositoryUrl = "https://github.com/amaldini/maldalang";

    private void OpenDocumentationUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ProjectRepositoryUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show("Could not open documentation. Please visit the project repository manually.",
                "Documentation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    
    private void HelpShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var shortcuts = "Keyboard Shortcuts:\n\n" +
            "File:\n" +
            "  Ctrl+N - New File\n" +
            "  Ctrl+O - Open File\n" +
            "  Ctrl+S - Save File\n" +
            "  Ctrl+Shift+S - Save As\n" +
            "  Alt+F4 - Exit\n\n" +
            "Edit:\n" +
            "  Ctrl+Z - Undo\n" +
            "  Ctrl+Y - Redo\n" +
            "  Ctrl+X - Cut\n" +
            "  Ctrl+C - Copy\n" +
            "  Ctrl+V - Paste\n" +
            "  Ctrl+A - Select All\n" +
            "  Ctrl+F - Find\n" +
            "  Ctrl+H - Replace\n" +
            "  Ctrl+Alt+F - Format Document\n" +
            "  Ctrl+. - Quick Fix\n\n" +
            "View:\n" +
            "  Ctrl+Shift+L - Toggle Syntax Panel\n\n" +
            "Run:\n" +
            "  Ctrl+F5 - Run without debugging\n" +
            "  F5 - Start Debugging / Continue\n" +
            "  Shift+F5 - Stop\n" +
            "  Ctrl+Shift+B - Compile\n\n" +
            "Debug:\n" +
            "  F5 - Continue when paused\n" +
            "  F10 - Step Over\n" +
            "  F11 - Step Into\n" +
            "  Shift+F11 - Step Out\n" +
            "  F9 - Toggle Breakpoint";
        
        MessageBox.Show(shortcuts, "Keyboard Shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
