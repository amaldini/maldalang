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

public partial class MainWindow : Window
{
    private enum FullStackRunChoice
    {
        Server,
        ClientPreview,
        FullStack
    }

    private enum DebuggeeKind
    {
        None,
        Interpret,
        JavaScript
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
    private readonly LayoutSettingsService _layoutSettings;
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
    private WebViewJsDebugger? _jsDebugger;
    private string _jsDebugOutput = "";
    private DebuggeeKind _focusedDebuggee;
    private bool _interpretPaused;
    private bool _jsPaused;
    private bool _jsStepInFlight;
    private bool _interpretDebugSessionActive;
    private bool _dualDebugSession;
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
    private bool _outputOnRight = true;
    private string _bottomTab = "errors";
    private string _sideTab = "output";
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
    private string? _maximizedSidebarTab;
    private bool IsSidebarMaximized => _maximizedSidebarTab != null;
    private bool IsWebUiMaximized => _maximizedSidebarTab == "webui";
    private bool IsAiPanelMaximized => _maximizedSidebarTab == "ai";
    private GridLength _editorColumnBeforeMaximize = new(1, GridUnitType.Star);
    private GridLength _sidebarColumnBeforeMaximize = new(420);
    private GridLength _leftSplitterColumnBeforeMaximize = new(5);
    private GridLength _rightSplitterColumnBeforeMaximize = new(5);
    private double _syntaxPanelColumnMinWidthBeforeMaximize = 220;
    private double _sidebarColumnMinWidthBeforeMaximize = 280;
    private Visibility _mainMenuVisibilityBeforeMaximize = Visibility.Visible;
    private Visibility _mainToolbarVisibilityBeforeMaximize = Visibility.Visible;
    private Visibility _editorPaneVisibilityBeforeMaximize = Visibility.Visible;
    private Visibility _sidebarTabBarVisibilityBeforeMaximize = Visibility.Visible;
    private Visibility _sidebarSplitterVisibilityBeforeMaximize = Visibility.Visible;
    private PropertyRegressionArtifactRequest? _pendingRegressionRequest;
    private ToolTip? _hoverToolTip;
    private const string SnippetCaretMarker = SyntaxSnippetCatalog.CaretMarker;
    private bool _starterLauncherShown;
    private ShowcaseSession? _showcase;
    private bool _showcasePreviewAutoplay;
    private string? _showcaseCaption;
    private string? _showcaseLyric;
    private Stopwatch? _showcaseClock;
    private bool _showcaseSplashVisible;
    private (FrameworkElement Element, Visibility Visibility)[]? _showcaseAirspace;
    private ExampleProgram? _currentExample;
    private bool _learningBranchBannerDismissed;

    public MainWindow()
    {
        _showcase = ShowcaseSession.Pending;
        ShowcaseSession.Pending = null;
        if (_showcase != null)
        {
            _starterLauncherShown = true;
        }

        InitializeComponent();
        _layoutSettings = new LayoutSettingsService();
        _layoutSettings.Load();
        _outputOnRight = _layoutSettings.OutputOnRight;
        if (_outputOnRight)
        {
            _sideTab = "output";
            _bottomTab = "errors";
            _activeTab = "output";
        }
        else
        {
            _sideTab = "ai";
            _bottomTab = "output";
            _activeTab = "output";
        }

        PlaceBottomPanels();
        ApplyOutputDock(_outputOnRight, refreshChrome: false);
        
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
        WebUiWebView.PreviewKeyDown += WebUiWebView_PreviewKeyDown;
        _ = InitializeWebUiPreviewAsync();
        MaldaLang.Runtime.UI.UiHostLink.RunWaiting += OnUiRunWaiting;
        
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

            if (_showcase != null)
            {
                _starterLauncherShown = true;
                ApplyShowcaseWindowBounds();
                ApplyOutputDock(true, refreshChrome: true);
                _ = RunShowcaseAsync();
            }
            else if (!_starterLauncherShown && string.IsNullOrWhiteSpace(CodeEditor.Text))
            {
                _starterLauncherShown = true;
                if (!App.HoldStartupLauncher)
                {
                    Dispatcher.BeginInvoke(() => ShowStarterLauncher(initialTrack: "student", fallbackToBlank: false));
                }
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

        var toggleMaximizeWebPreviewCommand = new RoutedCommand("ToggleMaximizeWebPreview", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            toggleMaximizeWebPreviewCommand,
            (s, e) => ToggleWebUiMaximized()));
        InputBindings.Add(new KeyBinding(toggleMaximizeWebPreviewCommand, Key.F6, ModifierKeys.Shift));

        var toggleMaximizeAiPanelCommand = new RoutedCommand("ToggleMaximizeAiPanel", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            toggleMaximizeAiPanelCommand,
            (s, e) => ToggleAiPanelMaximized()));
        InputBindings.Add(new KeyBinding(toggleMaximizeAiPanelCommand, Key.F7, ModifierKeys.Shift));

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
    
    protected override void OnClosed(EventArgs e)
    {
        MaldaLang.Runtime.UI.UiHostLink.RunWaiting -= OnUiRunWaiting;
        MaldaLang.Runtime.UI.UiHostLink.StopRuns();
        base.OnClosed(e);
    }
}
