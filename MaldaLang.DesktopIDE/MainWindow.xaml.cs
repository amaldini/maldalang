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

    private sealed class OpenDocument
    {
        public string? FilePath { get; set; }
        public string? PhysicalFilePath { get; set; }
        public string? VirtualTabId { get; set; }
        public string? VirtualDisplayName { get; set; }
        public int VirtualOrder { get; set; }
        public int VirtualStartLine { get; set; }
        public int VirtualEndLine { get; set; }
        public string Content { get; set; } = "";
        public string LastSavedContent { get; set; } = "";
        public bool IsDirty { get; set; }
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

    private readonly ExecutionService _executionService;
    private readonly DebuggerService _debuggerService;
    private readonly LanguageService _languageService;
    private readonly SymbolNavigationService _symbolNavigationService;
    private readonly FileService _fileService;
    private readonly Services.CompilerService _compilerService;
    private readonly VirtualDocumentSegmentationService _virtualDocumentSegmentationService;
    private readonly ToolCallLogService _toolCallLogService;
    private readonly ThemeService _themeService;
    private readonly TypeAnalysisSettingsService _typeAnalysisSettingsService;
    private readonly CodeDiffService _codeDiffService;
    private readonly MCPServerConfigService _mcpConfigService;
    private readonly MCPServerConnectionService _mcpConnectionService;
    private UserControls.AIChatPanel? _aiChatPanel;
    private CurrentLineBackgroundRenderer? _currentLineRenderer;
    private SearchResultsBackgroundRenderer? _searchResultsRenderer;
    private DebuggerHook? _debuggerHook;
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

        var renameSymbolCommand = new RoutedCommand("RenameSymbol", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            renameSymbolCommand,
            (s, e) => NavigateRenameSymbol_Click(s, e)));
        InputBindings.Add(new KeyBinding(renameSymbolCommand, Key.R, ModifierKeys.Control | ModifierKeys.Alt));
        
        // Run shortcuts
        var runCommand = new RoutedCommand("Run", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            runCommand,
            (s, e) => RunButton_Click(s, e)));
        InputBindings.Add(new KeyBinding(runCommand, Key.F5, ModifierKeys.None));
        
        var debugCommand = new RoutedCommand("Debug", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            debugCommand,
            (s, e) => DebugButton_Click(s, e)));
        InputBindings.Add(new KeyBinding(debugCommand, Key.F9, ModifierKeys.None));
        
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
        var continueCommand = new RoutedCommand("Continue", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(
            continueCommand,
            (s, e) => ContinueButton_Click(s, e)));
        InputBindings.Add(new KeyBinding(continueCommand, Key.F5, ModifierKeys.None));
        
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
        var isDarkTheme = theme.Name == "Dark" || theme.Name == "HighContrast";
        
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
            int startLine = line.LineNumber - 1; // Convert to 0-based
            if (IsVirtualDocument(activeDocument))
            {
                startLine += activeDocument.VirtualStartLine;
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
        
        // Enable glyph margin for breakpoint indicators
        CodeEditor.TextArea.LeftMargins.Insert(0, new BreakpointMargin(CodeEditor.TextArea, this));
        
        // Add autocomplete support
        CodeEditor.TextArea.TextEntered += TextArea_TextEntered;
        CodeEditor.TextArea.KeyDown += TextArea_KeyDown;
        CodeEditor.TextArea.TextView.MouseHover += TextArea_MouseHover;
        CodeEditor.TextArea.TextView.MouseHoverStopped += TextArea_MouseHoverStopped;
        
        // Track cursor position and selection for AI chat panel
        CodeEditor.TextArea.Caret.PositionChanged += (s, e) =>
        {
            if (_activeTab == "ai" && _aiChatPanel != null)
            {
                _aiChatPanel.CursorPosition = GetCursorPosition();
                _aiChatPanel.SelectedCode = GetSelectedText();
            }

            UpdateSignatureHelp();
        };
        
        CodeEditor.TextArea.SelectionChanged += (s, e) =>
        {
            if (_activeTab == "ai" && _aiChatPanel != null)
            {
                _aiChatPanel.SelectedCode = GetSelectedText();
            }
        };
    }
    
    private void UpdateBreakpointVisuals()
    {
        var activeDocument = GetActiveDocument();
        var fileName = GetPhysicalPath(activeDocument) ?? "main.malda";
        var breakpoints = _debuggerService.Breakpoints.Where(bp => bp.FilePath == fileName && bp.Enabled);
        var localBreakpoints = IsVirtualDocument(activeDocument)
            ? breakpoints
                .Where(bp => bp.Line >= activeDocument.VirtualStartLine && bp.Line <= activeDocument.VirtualEndLine)
                .Select(bp => bp.Line - activeDocument.VirtualStartLine + 1)
                .ToList()
            : breakpoints.Select(bp => bp.Line + 1).ToList();
        
        _breakpointLines.Clear();
        _breakpointLines.AddRange(localBreakpoints);
        
        // Force redraw
        CodeEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }
    
    private void TextArea_TextEntered(object? sender, TextCompositionEventArgs e)
    {
        // Trigger autocomplete on typing letters, numbers, or certain characters
        // Include @ for decorator support
        if (e.Text != null && e.Text.Length > 0 && 
            (char.IsLetterOrDigit(e.Text[0]) || e.Text == "." || e.Text == "(" || e.Text == "@"))
        {
            ShowCompletion();
        }

        UpdateSignatureHelp();
    }
    
    private void TextArea_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Space to manually trigger autocomplete
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ShowCompletion();
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
    
    private void ShowCompletion()
    {
        // Close existing completion window if open
        if (_completionWindow != null)
        {
            _completionWindow.Close();
            _completionWindow = null;
        }
        
        var textArea = CodeEditor.TextArea;
        var caret = textArea.Caret;
        var document = CodeEditor.Document;
        var line = document.GetLineByNumber(caret.Line);
        
        // Find the start of the current word by looking backwards from the caret
        // Include @ character for decorator support
        int wordStart = caret.Offset;
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
        
        // Extract the current prefix
        string prefix = document.GetText(wordStart, caret.Offset - wordStart);
        
        // Get completions from language service
        var source = CodeEditor.Text;
        var completions = _languageService.GetCompletions(source, caret.Line - 1, caret.Column - 1);
        
        // Check if we're in decorator context (prefix starts with @)
        bool isDecoratorContext = prefix.StartsWith("@");
        
        // For decorator context, language service already filtered the completions
        // So we should use all returned completions without additional filtering
        // For other contexts, filter with the full prefix
        List<CompletionItem> filteredCompletions;
        if (isDecoratorContext)
        {
            // Language service already filtered decorators, use all returned completions
            filteredCompletions = completions;
        }
        else
        {
            // Filter completions based on the current prefix (case-insensitive)
            filteredCompletions = completions
                .Where(c => prefix == "" || c.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        
        if (filteredCompletions.Count == 0)
            return;
        
        // Create completion window with the correct start offset
        _completionWindow = new CompletionWindow(textArea);
        var data = _completionWindow.CompletionList.CompletionData;
        
        foreach (var item in filteredCompletions)
        {
            var insertText = item.InsertText ?? item.Label;
            var description = item.Detail ?? item.Label;
            // Use insertText for both Text and Content so the correct text is inserted
            data.Add(new SimpleCompletionData(insertText, insertText, description));
        }
        
        // Set the start offset so the completion window knows what to replace
        _completionWindow.StartOffset = wordStart;
        
        if (data.Count > 0)
        {
            _completionWindow.CompletionList.SelectedItem = data[0];
            _completionWindow.Show();
            _completionWindow.Closed += (s, e) => _completionWindow = null;
        }
        else
        {
            _completionWindow.Close();
            _completionWindow = null;
        }
    }

    private void UpdateSignatureHelp()
    {
        var caret = CodeEditor.TextArea.Caret;
        var source = CodeEditor.Text;
        var help = _languageService.GetSignatureHelp(source, caret.Line - 1, caret.Column - 1);
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
            _signatureHelpWindow.Closed += (s, e) =>
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
            var hover = _languageService.GetHoverInformation(
                CodeEditor.Text,
                position.Value.Line - 1,
                position.Value.Column - 1,
                sourceFileName,
                CancellationToken.None);

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
        }
        catch
        {
            CloseHoverToolTip();
        }
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

    private void SetupExamples()
    {
        // Examples are now loaded from files via ExampleBrowserWindow
        // This method is kept for compatibility but no longer populates a ComboBox
    }

    private void SetupDataBinding()
    {
        UpdateButtonStates();
    }

    private void InitializeSyntaxPanel()
    {
        _syntaxSnippets.Clear();
        _syntaxSnippets.AddRange(CreateDefaultSyntaxSnippets());

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

    private static List<SyntaxSnippet> CreateDefaultSyntaxSnippets()
    {
        return new List<SyntaxSnippet>
        {
            CreateSnippet(
                id: "class",
                category: "OOP",
                label: "Class",
                description: "Define a class with constructor and method.",
                templateText: $"class ClassName {{\n\tvar value;\n\n\tfunction ClassName(value) {{\n\t\tthis.value = {SnippetCaretMarker}value;\n\t}}\n\n\tfunction methodName() {{\n\t\t\n\t}}\n}}"),
            CreateSnippet(
                id: "function",
                category: "Declarations",
                label: "Function",
                description: "Define a reusable function.",
                templateText: $"function functionName(param1, param2) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "prompt",
                category: "Declarations",
                label: "Prompt",
                description: "Define an AI prompt function.",
                templateText: $"prompt promptName(input) -> OutputType {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "if",
                category: "Statements",
                label: "If",
                description: "Conditional block.",
                templateText: $"if (condition) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "if-else",
                category: "Statements",
                label: "If / Else",
                description: "Two-way conditional block.",
                templateText: $"if (condition) {{\n\t{SnippetCaretMarker}\n}} else {{\n\t\n}}"),
            CreateSnippet(
                id: "while",
                category: "Loops",
                label: "While Loop",
                description: "Repeat while condition is true.",
                templateText: $"while (condition) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "for-in",
                category: "Loops",
                label: "For-In Loop",
                description: "Iterate items in a collection.",
                templateText: $"for (var item in collection) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "foreach",
                category: "Loops",
                label: "Foreach Loop",
                description: "Alternative foreach syntax over a collection.",
                templateText: $"foreach (var item in collection) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "lambda-expression",
                category: "Functional",
                label: "Lambda Expression",
                description: "Inline function with expression body.",
                templateText: $"var transform = (x) => {SnippetCaretMarker}x;"),
            CreateSnippet(
                id: "lambda-block",
                category: "Functional",
                label: "Lambda Block",
                description: "Inline function with block body.",
                templateText: $"var transform = (x) => {{\n\t{SnippetCaretMarker}\n}};"),
            CreateSnippet(
                id: "var-declaration",
                category: "Basics",
                label: "Variable Declaration",
                description: "Declare and initialize a variable.",
                templateText: $"var variableName = {SnippetCaretMarker}value;"),
            CreateSnippet(
                id: "new-instance",
                category: "OOP",
                label: "Create Object",
                description: "Instantiate a class with constructor args.",
                templateText: $"var instance = new ClassName({SnippetCaretMarker}value);"),
            CreateSnippet(
                id: "new-instance-no-args",
                category: "OOP",
                label: "Create Object (No Args)",
                description: "Instantiate a class with a parameterless constructor.",
                templateText: $"var instance = new {SnippetCaretMarker}ClassName();"),
            CreateSnippet(
                id: "method-call",
                category: "OOP",
                label: "Method Call",
                description: "Call an instance method.",
                templateText: $"var result = instance.{SnippetCaretMarker}methodName();"),
            CreateSnippet(
                id: "field-access",
                category: "OOP",
                label: "Field Access",
                description: "Read or write an instance field.",
                templateText: $"instance.{SnippetCaretMarker}value = 42;"),
            CreateSnippet(
                id: "try-catch",
                category: "Statements",
                label: "Try / Catch",
                description: "Handle runtime errors.",
                templateText: $"try {{\n\t{SnippetCaretMarker}\n}} catch (err) {{\n\tprint(err);\n}}"),
            CreateSnippet(
                id: "for-classic",
                category: "Loops",
                label: "For Loop (Classic)",
                description: "Traditional counter-based loop.",
                templateText: $"for (var i = 0; i < count; i = i + 1) {{\n\t{SnippetCaretMarker}\n}}"),
            CreateSnippet(
                id: "break",
                category: "Statements",
                label: "Break",
                description: "Exit current loop.",
                templateText: $"{SnippetCaretMarker}break;"),
            CreateSnippet(
                id: "continue",
                category: "Statements",
                label: "Continue",
                description: "Skip to next loop iteration.",
                templateText: $"{SnippetCaretMarker}continue;"),
            CreateSnippet(
                id: "return",
                category: "Statements",
                label: "Return",
                description: "Return from function.",
                templateText: $"return {SnippetCaretMarker}value;"),
            CreateSnippet(
                id: "array-literal",
                category: "Basics",
                label: "Array",
                description: "Create an array literal.",
                templateText: $"var items = [{SnippetCaretMarker}1, 2, 3];"),
            CreateSnippet(
                id: "object-literal",
                category: "Basics",
                label: "Object",
                description: "Create an object literal.",
                templateText: $"var obj = {{\n\t\"name\": {SnippetCaretMarker}\"value\"\n}};"),
            CreateSnippet(
                id: "string-interpolation",
                category: "Strings",
                label: "String Interpolation",
                description: "Interpolate variables with $\"...{expr}...\".",
                templateText: "var name = \"Alice\";\nvar total = 42;\nvar message = $\"Hello {name}, total={total}\";\nprint(" + SnippetCaretMarker + "message);"),
            CreateSnippet(
                id: "multiline-string",
                category: "Strings",
                label: "Multiline String",
                description: "Triple-quoted multiline string literal.",
                templateText: "var text = \"\"\"\n" + SnippetCaretMarker + "Line 1\nLine 2\n\"\"\";\nprint(text);"),
            CreateSnippet(
                id: "multiline-interpolated-string",
                category: "Strings",
                label: "Multiline Interpolated String",
                description: "Interpolated triple-quoted string: $\"\"\"...\"\"\".",
                templateText: "var name = \"MALDA\";\nvar greeting = $\"\"\"\nHello {name}!\n" + SnippetCaretMarker + "Welcome to multiline interpolation.\n\"\"\";\nprint(greeting);"),
            CreateSnippet(
                id: "match-array-rest-pattern",
                category: "Pattern Matching",
                label: "Match Array + Rest Pattern",
                description: "Advanced match with nested object pattern and ...rest.",
                templateText: "var data = [{ type: \"A\", value: 1 }, { type: \"B\", value: 2 }];\nvar result = match data {\n\tcase [{ type: \"A\", value: v }, ...rest]: \"first=\" + v;\n\tdefault: \"none\";\n};\nprint(" + SnippetCaretMarker + "result);"),
            CreateSnippet(
                id: "match-object-pattern",
                category: "Pattern Matching",
                label: "Match Object Pattern",
                description: "Advanced match with object shorthand and wildcard.",
                templateText: "var profile = { name: \"Alice\", age: 30, city: \"Rome\" };\nvar result = match profile {\n\tcase { name, age }: name + \" is \" + age;\n\tcase { name }: name;\n\tcase _: \"unknown\";\n};\nprint(" + SnippetCaretMarker + "result);"),
            CreateSnippet(
                id: "match-variant-pattern",
                category: "Pattern Matching",
                label: "Match Variant Pattern",
                description: "Sum-type variant matching with constructor patterns.",
                templateText: "type Result = Ok(value) | Err(message);\nvar r = Ok(42);\nvar result = match r {\n\tcase Ok(v): \"ok: \" + v;\n\tcase Err(msg): \"error: \" + msg;\n};\nprint(" + SnippetCaretMarker + "result);"),
            CreateSnippet(
                id: "actor",
                category: "Actors",
                label: "Actor",
                description: "Define an actor with a reply handler.",
                templateText: $"actor Worker {{\n\tfunction start() {{\n\t\tprint(\"Worker started\");\n\t}}\n\n\tfunction compute(value) {{\n\t\t{SnippetCaretMarker}reply(value * 2);\n\t}}\n}}"),
            CreateSnippet(
                id: "spawn",
                category: "Actors",
                label: "Spawn Actor",
                description: "Create a new actor instance.",
                templateText: $"var worker = spawn {SnippetCaretMarker}Worker();"),
            CreateSnippet(
                id: "send",
                category: "Actors",
                label: "Send Message",
                description: "Send async message to actor.",
                templateText: $"send {SnippetCaretMarker}worker.start();"),
            CreateSnippet(
                id: "send-then-timeout",
                category: "Actors",
                label: "Send With Callback + Timeout",
                description: "Handle actor response with timeout fallback.",
                templateText: $"send worker.compute(42) then (result) {{\n\t{SnippetCaretMarker}print(result);\n}} timeout 500 catch (error) {{\n\tprint(error);\n}}"),
            CreateSnippet(
                id: "using",
                category: "Declarations",
                label: "Using",
                description: "Import a package or namespace.",
                templateText: $"using {SnippetCaretMarker}Package.Name;"),
            CreateSnippet(
                id: "include",
                category: "Declarations",
                label: "Include",
                description: "Include another MALDA file.",
                templateText: $"include {SnippetCaretMarker}\"shared.malda\";"),
            CreateSnippet(
                id: "comment-block",
                category: "Utilities",
                label: "Comment Block",
                description: "Insert a multi-line comment block.",
                templateText: $"/*\n\t{SnippetCaretMarker}Notes\n*/"),
            CreateSnippet(
                id: "print",
                category: "Utilities",
                label: "Print",
                description: "Print a value to output.",
                templateText: $"print({SnippetCaretMarker}\"text\");"),
            CreateSnippet(
                id: "sleep",
                category: "Utilities",
                label: "Sleep",
                description: "Pause execution for milliseconds.",
                templateText: $"sleep({SnippetCaretMarker}100);")
        };
    }

    private static SyntaxSnippet CreateSnippet(string id, string category, string label, string description, string templateText)
    {
        return new SyntaxSnippet
        {
            Id = id,
            Category = category,
            Label = label,
            Description = description,
            TemplateText = templateText,
            Preview = templateText.Replace(SnippetCaretMarker, "")
        };
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

    private void SyncEditorFromActiveDocument()
    {
        var document = GetActiveDocument();

        _isSwitchingDocument = true;
        try
        {
            if (CodeEditor.Text != document.Content)
            {
                CodeEditor.Text = document.Content;
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

        foreach (var key in _documentOrder)
        {
            if (!_openDocuments.TryGetValue(key, out var document))
            {
                continue;
            }

            var button = new Button
            {
                Content = document.IsDirty
                    ? $"{GetDocumentDisplayName(document)} *"
                    : GetDocumentDisplayName(document),
                Margin = new Thickness(2, 2, 0, 2),
                Padding = new Thickness(12, 4, 12, 4),
                ToolTip = GetPhysicalPath(document) ?? "Unsaved file",
                Background = key == _activeDocumentKey ? activeBrush : inactiveBrush
            };

            var documentKey = key;
            button.Click += (_, _) => ActivateDocument(documentKey);

            var canClose = _documentOrder.Count > 1;
            var tabContainer = new DockPanel
            {
                Margin = new Thickness(2, 0, 0, 0),
                LastChildFill = false
            };

            DockPanel.SetDock(button, Dock.Left);
            tabContainer.Children.Add(button);

            if (canClose)
            {
                var closeButton = new Button
                {
                    Content = "x",
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(2, 2, 0, 2),
                    Padding = new Thickness(0),
                    ToolTip = $"Close {GetDocumentDisplayName(document)}",
                    Background = key == _activeDocumentKey ? activeBrush : inactiveBrush
                };
                closeButton.Click += (_, _) => CloseDocument(documentKey);
                DockPanel.SetDock(closeButton, Dock.Right);
                tabContainer.Children.Add(closeButton);
            }

            DocumentTabsPanel.Children.Add(tabContainer);
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
                Filter = "Simple Language Files (*.malda)|*.malda|All Files (*.*)|*.*",
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
        });
    }

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
        var diagnostics = _languageService.GetDiagnostics(
            source,
            sourceKey,
            strictTypesOptions: _typeAnalysisSettingsService.ToOptions());
        if (IsVirtualDocument(activeDocument))
        {
            diagnostics = diagnostics
                .Where(diagnostic => diagnostic.Line >= activeDocument.VirtualStartLine && diagnostic.Line <= activeDocument.VirtualEndLine)
                .Select(diagnostic => new Diagnostic
                {
                    Message = diagnostic.Message,
                    Severity = diagnostic.Severity,
                    Line = diagnostic.Line - activeDocument.VirtualStartLine,
                    Column = diagnostic.Column,
                    Length = diagnostic.Length,
                    AutoFix = diagnostic.AutoFix
                })
                .ToList();
        }

        UpdateErrorsPanel(diagnostics);
        RefreshOutline();
    }

    private void UpdateErrorsPanel(List<Diagnostic> diagnostics)
    {
        ErrorsListBox.Items.Clear();
        foreach (var diagnostic in diagnostics)
        {
            // Add Diagnostic objects directly - the DataTemplate will handle the display
            ErrorsListBox.Items.Add(diagnostic);
        }
    }

    private void OnBreakpointsChanged()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateBreakpointsPanel();
            UpdateBreakpointVisuals();
        });
    }
    
    public bool IsBreakpointLine(int lineNumber)
    {
        return _breakpointLines.Contains(lineNumber);
    }
    
    public void ToggleBreakpointAtLine(int lineNumber)
    {
        var activeDocument = GetActiveDocument();
        var fileName = GetPhysicalPath(activeDocument) ?? "main.malda";
        if (IsVirtualDocument(activeDocument))
        {
            lineNumber += activeDocument.VirtualStartLine;
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
            var text = $"Line {bp.Line + 1}";
            if (!string.IsNullOrEmpty(bp.Condition))
            {
                text += $" ({bp.Condition})";
            }
            BreakpointsListBox.Items.Add(text);
        }
    }

    private void CodeEditor_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var line = CodeEditor.Document.GetLineByNumber(CodeEditor.TextArea.Caret.Line);
        var lineNumber = line.LineNumber - 1; // 0-based
        var activeDocument = GetActiveDocument();
        var fileName = GetPhysicalPath(activeDocument) ?? "main.malda";
        if (IsVirtualDocument(activeDocument))
        {
            lineNumber += activeDocument.VirtualStartLine;
        }

        _debuggerService.ToggleBreakpoint(lineNumber, fileName);
        UpdateBreakpointVisuals();
        UpdateBreakpointsPanel();
    }

    private bool IsMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        // More strict markdown detection - only detect actual markdown structures
        // Check for markdown patterns that indicate structured content, not just any text with * or #
        var trimmed = text.TrimStart();
        
        // Check for headings at the start of lines
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^#{1,6}\s+\w", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        
        // Check for code blocks
        if (trimmed.Contains("```"))
            return true;
        
        // Check for horizontal rules (--- on its own line)
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^---+$", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        
        // Check for markdown lists (lines starting with - or * or numbers)
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[\s]*[-*+]\s+\w", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[\s]*\d+\.\s+\w", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        
        // Check for markdown tables
        if (trimmed.Contains("|") && trimmed.Contains("---"))
            return true;
        
        // Check for blockquotes
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^>\s+\w", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        
        return false;
    }

    private void SetOutputText(string text, bool isError = false)
    {
        TryAutoOpenWebUiFromOutput(text);
        UpdateRegressionActionState(text);

        var theme = _themeService.CurrentTheme;
        var scrollbarCss = GetScrollbarCss(theme);
        if (string.IsNullOrEmpty(text))
        {
            OutputWebBrowser.NavigateToString($"<html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><meta charset=\"UTF-8\"><style>body {{ margin: 0; padding: 8px; font-family: Consolas; color: {ColorToHex(theme.TextForeground)}; background: {ColorToHex(theme.ListBackground)}; min-height: 100vh; }} html {{ background: {ColorToHex(theme.ListBackground)}; }}{scrollbarCss}</style></head><body><p style='color: {ColorToHex(theme.TextSecondary)}; font-style: italic;'>No output yet. Run your program to see output here.</p></body></html>");
            return;
        }
        
        if (IsMarkdown(text))
        {
            // First, protect code blocks by replacing them with placeholders
            var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var codeBlockPlaceholders = new System.Collections.Generic.Dictionary<string, string>();
            var placeholderCounter = 0;
            
            // Protect code blocks (```...```)
            var protectedText = System.Text.RegularExpressions.Regex.Replace(normalized, @"```[\s\S]*?```", 
                m => {
                    var placeholder = $"__CODE_BLOCK_{placeholderCounter}__";
                    codeBlockPlaceholders[placeholder] = m.Value;
                    placeholderCounter++;
                    return placeholder;
                });
            
            // Convert single newlines to <br> tags BEFORE markdown processing
            // This ensures newlines are preserved
            protectedText = System.Text.RegularExpressions.Regex.Replace(
                protectedText,
                @"(?<!\n)\n(?!\n)",
                "<br>" // Convert to HTML br tag
            );
            
            // Restore code blocks
            foreach (var kvp in codeBlockPlaceholders)
            {
                protectedText = protectedText.Replace(kvp.Key, kvp.Value);
            }
            
            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();
            
            var html = Markdown.ToHtml(protectedText, pipeline);
            
            // Post-process: Replace any remaining newlines outside of code/pre blocks with <br>
            // (as a backup in case some newlines weren't converted)
            var htmlCodeBlockPlaceholders = new System.Collections.Generic.Dictionary<string, string>();
            var htmlPlaceholderCounter = 0;
            var protectedHtml = System.Text.RegularExpressions.Regex.Replace(html, @"(<pre[^>]*>.*?</pre>|<code[^>]*>.*?</code>)", 
                m => {
                    var placeholder = $"__HTML_CODE_BLOCK_{htmlPlaceholderCounter}__";
                    htmlCodeBlockPlaceholders[placeholder] = m.Value;
                    htmlPlaceholderCounter++;
                    return placeholder;
                },
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Replace all newlines with <br> tags
            protectedHtml = protectedHtml.Replace("\n", "<br>");
            
            // Restore code blocks
            foreach (var kvp in htmlCodeBlockPlaceholders)
            {
                protectedHtml = protectedHtml.Replace(kvp.Key, kvp.Value);
            }
            
            html = protectedHtml;
            var codeBg = theme.ListBackground.R < 128 ? Color.FromRgb((byte)(theme.ListBackground.R + 30), (byte)(theme.ListBackground.G + 30), (byte)(theme.ListBackground.B + 30)) : Color.FromRgb((byte)(theme.ListBackground.R - 20), (byte)(theme.ListBackground.G - 20), (byte)(theme.ListBackground.B - 20));
            var borderColor = theme.BorderColor;
            var fullHtml = $@"
<html>
<head>
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <meta charset=""UTF-8"">
    <style>
        html {{
            background: {ColorToHex(theme.ListBackground)};
            margin: 0;
            padding: 0;
            height: 100%;
        }}
        body {{
            font-family: Consolas, Monaco, monospace;
            font-size: 16px;
            color: {ColorToHex(theme.TextForeground)};
            background: {ColorToHex(theme.ListBackground)};
            padding: 16px;
            line-height: 1.6;
            margin: 0;
            min-height: 100vh;
        }}
        p {{
            margin: 0.5em 0;
        }}
        h1, h2, h3, h4, h5, h6 {{
            color: {ColorToHex(theme.TextForeground)};
            margin-top: 1em;
            margin-bottom: 0.5em;
        }}
        h1 {{ font-size: 1.8em; }}
        h2 {{ font-size: 1.5em; }}
        h3 {{ font-size: 1.3em; }}
        code {{
            background: {ColorToHex(codeBg)};
            padding: 2px 6px;
            border-radius: 3px;
            font-family: Consolas, Monaco, monospace;
        }}
        pre {{
            background: {ColorToHex(codeBg)};
            padding: 12px;
            border-radius: 4px;
            overflow-x: auto;
            margin: 1em 0;
        }}
        pre code {{
            background: transparent;
            padding: 0;
        }}
        blockquote {{
            border-left: 4px solid {ColorToHex(borderColor)};
            padding-left: 1em;
            margin: 1em 0;
            color: {ColorToHex(theme.TextSecondary)};
        }}
        table {{
            border-collapse: collapse;
            width: 100%;
            margin: 1em 0;
        }}
        table th, table td {{
            border: 1px solid {ColorToHex(borderColor)};
            padding: 8px;
        }}
        table th {{
            background: {ColorToHex(codeBg)};
            font-weight: bold;
        }}
        a {{
            color: {ColorToHex(theme.DebugAccent)};
            text-decoration: none;
        }}
        a:hover {{
            text-decoration: underline;
        }}
        ul, ol {{
            margin: 1em 0;
            padding-left: 2em;
        }}
        li {{
            margin: 0.5em 0;
        }}
        .error {{
            color: {ColorToHex(theme.ErrorColor)};
            background: {ColorToHex(Color.FromArgb(255, (byte)Math.Min(255, theme.ErrorColor.R + 50), (byte)Math.Min(255, theme.ErrorColor.G + 30), (byte)Math.Min(255, theme.ErrorColor.B + 30)))};
            padding: 8px;
            border-left: 4px solid {ColorToHex(theme.ErrorColor)};
            margin: 1em 0;
        }}
        {scrollbarCss}
    </style>
</head>
<body>
    {(isError ? $"<div class='error'><strong>Error:</strong><br/>{html}</div>" : html)}
</body>
</html>";
            
            OutputWebBrowser.NavigateToString(fullHtml);
        }
        else
        {
            // Plain text output - convert newlines to <br> tags for proper display
            var escapedText = System.Security.SecurityElement.Escape(text);
            // Replace newlines with <br> tags after escaping
            escapedText = escapedText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br>");
            
            var plainHtml = $@"
<html>
<head>
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <meta charset=""UTF-8"">
    <style>
        html {{
            background: {ColorToHex(theme.ListBackground)};
            margin: 0;
            padding: 0;
            height: 100%;
        }}
        body {{
            font-family: Consolas, Monaco, monospace;
            font-size: 16px;
            color: {(isError ? ColorToHex(theme.ErrorColor) : ColorToHex(theme.TextForeground))};
            background: {ColorToHex(theme.ListBackground)};
            padding: 8px;
            word-wrap: break-word;
            margin: 0;
            min-height: 100vh;
        }}
        {scrollbarCss}
    </style>
</head>
<body>
    {(isError ? $"<strong>Error:</strong><br/>{escapedText}" : escapedText)}
</body>
</html>";
            
            OutputWebBrowser.NavigateToString(plainHtml);
        }
    }

    private void UpdateRegressionActionState(string outputText)
    {
        if (PropertyRegressionArtifactSupport.TryExtractFromOutput(outputText, out var request))
        {
            _pendingRegressionRequest = request;
            RegressionActionBar.Visibility = Visibility.Visible;
            var fileName = request?.RecommendedRegressionFileName;
            if (string.IsNullOrWhiteSpace(fileName) && request != null)
            {
                fileName = PropertyRegressionArtifactSupport.BuildRecommendedFileName(request);
            }

            RegressionHintTextBlock.Text = string.IsNullOrWhiteSpace(fileName)
                ? "CI payload supports regression generation"
                : $"CI payload -> {fileName}";
            return;
        }

        _pendingRegressionRequest = null;
        RegressionActionBar.Visibility = Visibility.Collapsed;
        RegressionHintTextBlock.Text = string.Empty;
    }

    private string ResolveRegressionOutputPath(PropertyRegressionArtifactRequest request)
    {
        var workspaceRoot = GetCurrentWorkspaceDirectory();
        return PropertyRegressionArtifactSupport.ResolveWorkspaceSafePreferredPath(request, workspaceRoot);
    }

    private string GetCurrentWorkspaceDirectory()
    {
        var activePath = GetCurrentPhysicalFilePath();
        if (!string.IsNullOrWhiteSpace(activePath))
        {
            var currentDir = Path.GetDirectoryName(activePath);
            if (!string.IsNullOrWhiteSpace(currentDir))
            {
                return currentDir;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private void CreateRegressionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingRegressionRequest == null)
        {
            MessageBox.Show(
                this,
                "No valid property failure CI payload is currently available in output.",
                "Create Regression",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var content = PropertyRegressionArtifactSupport.BuildArtifactContent(_pendingRegressionRequest);
            var preferredPath = ResolveRegressionOutputPath(_pendingRegressionRequest);
            var outputPath = PropertyRegressionArtifactSupport.ResolveCollisionSafePath(preferredPath, content);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());
            File.WriteAllText(outputPath, content);

            OpenFileAndIncludedDocuments(outputPath);
            MessageBox.Show(
                this,
                $"Regression created:\n{outputPath}",
                "Create Regression",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Failed to create regression artifact.\n{ex.Message}",
                "Create Regression",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        SaveEditorIntoActiveDocument();
        var activeDocument = GetActiveDocument();
        var sourceForExecution = GetSourceForExecution(activeDocument);
        var source = sourceForExecution.Source;
        var input = ProgramInputTextBox.Text;

        if (IsFullStackSource(source))
        {
            var runChoice = ShowFullStackRunDialog();
            if (runChoice == null)
            {
                return;
            }

            if (runChoice == FullStackRunChoice.ClientPreview)
            {
                SetOutputText("Opening the client target in the Web Preview panel...");
                SwitchToTab("output");
                await PreviewCurrentDocumentAsync();
                return;
            }

            StartFullStackRun(source, sourceForExecution.SourceFilePath, runChoice == FullStackRunChoice.FullStack);
            return;
        }
        
        // Clear any debugger line highlight for a normal run
        ClearCurrentLineHighlight();
        
        // Do not clear tool calls log here so Edit mode tool calls persist when user then runs code
        UpdateToolCallsDisplay();
        
        // Cancel any previous run
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        
        // Create new cancellation token for this run
        _runCancellation = new CancellationTokenSource();
        var token = _runCancellation.Token;
        
        SetOutputText(""); // Clear output at start
        
        // Run in a separate task to allow cancellation
        _runTask = Task.Run(async () =>
        {
            try
            {
                var fileName = sourceForExecution.SourceFilePath;
                var result = await _executionService.ExecuteAsync(source, input, fileName);
                
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        SetOutputText($"{result.Output}\n\nError: {result.Error}", isError: true);
                    }
                    else
                    {
                        SetOutputText(result.Output);
                    }
                    SwitchToTab("output");
                    UpdateButtonStates();
                });
            }
            catch (OperationCanceledException)
            {
                Dispatcher.Invoke(() =>
                {
                    SetOutputText(_executionService.GetCurrentOutput() + "\n\nExecution cancelled by user.");
                    SwitchToTab("output");
                    UpdateButtonStates();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    SetOutputText($"Error: {ex.Message}", isError: true);
                    SwitchToTab("output");
                    UpdateButtonStates();
                });
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    _runTask = null;
                    _runCancellation?.Dispose();
                    _runCancellation = null;
                    UpdateButtonStates();
                });
            }
        }, token);
        
        UpdateButtonStates();
    }

    private void StartFullStackRun(string source, string sourcePath, bool openClientPreview)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            MessageBox.Show(
                this,
                "Save the current full-stack MALDA file before running it.",
                "Run Full-Stack App",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ClearCurrentLineHighlight();
        UpdateToolCallsDisplay();

        _runCancellation?.Cancel();
        KillActiveRunProcess();
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        var token = _runCancellation.Token;

        SetOutputText("Compiling server target...");
        SwitchToTab("output");

        _runTask = Task.Run(async () =>
        {
            var output = new StringBuilder();

            void AppendOutput(string text, bool isError = false)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                output.AppendLine(text);
                Dispatcher.Invoke(() =>
                {
                    SetOutputText(output.ToString(), isError);
                    SwitchToTab("output");
                    UpdateButtonStates();
                });
            }

            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "malda-fullstack-run", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                var outputPath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(sourcePath) + ".server.exe");
                var webDirectory = Path.Combine(tempDir, "web");
                Directory.CreateDirectory(webDirectory);
                var clientScriptPath = Path.Combine(webDirectory, Path.GetFileNameWithoutExtension(sourcePath) + ".js");

                AppendOutput("Compiling server target with @server/@shared partitioning...");
                var result = await _compilerService.CompileAsync(
                    sourcePath,
                    outputPath,
                    Compiler.CompilationMode.TranspileToCSharp,
                    includeLLamaSharp: false,
                    cancellationToken: token);

                if (!result.Success)
                {
                    AppendOutput(result.ErrorMessage ?? "Compilation failed.", isError: true);
                    return;
                }

                var executablePath = result.OutputPath ?? outputPath;
                AppendOutput($"Server target compiled: {executablePath}");

                AppendOutput("Compiling client target into the server web root...");
                var clientResult = await _compilerService.CompileAsync(
                    sourcePath,
                    clientScriptPath,
                    Compiler.CompilationMode.JavaScript,
                    includeLLamaSharp: false,
                    cancellationToken: token);

                if (!clientResult.Success)
                {
                    AppendOutput(clientResult.ErrorMessage ?? "Client compilation failed.", isError: true);
                    return;
                }

                AppendOutput($"Client distribution generated in: {webDirectory}");

                var workingDirectory = FindRepoRoot();
                if (string.IsNullOrWhiteSpace(workingDirectory))
                {
                    workingDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };
                process.StartInfo.Environment["MALDA_WEB_DIRECTORY"] = webDirectory;

                if (!process.Start())
                {
                    AppendOutput("Failed to start the compiled server process.", isError: true);
                    return;
                }

                lock (_activeRunProcessLock)
                {
                    _activeRunProcess = process;
                }

                AppendOutput($"Server process started (PID {process.Id}). Press Stop to terminate it.");

                if (openClientPreview)
                {
                    var serverUrl = $"http://localhost:{ExtractFullStackHttpPort(source)}/";
                    AppendOutput($"Opening client served by the app server: {serverUrl}");
                    var previewOperation = Dispatcher.InvokeAsync(() => OpenUriInWebUiPanelAsync(new Uri(serverUrl), serverUrl, switchToTab: true, ensureUiHost: false));
                    await await previewOperation.Task;
                }

                var stdoutTask = ReadRunProcessStreamAsync(process.StandardOutput, line => AppendOutput(line), token);
                var stderrTask = ReadRunProcessStreamAsync(process.StandardError, line => AppendOutput(line, isError: true), token);

                try
                {
                    await process.WaitForExitAsync(token);
                    await Task.WhenAll(stdoutTask, stderrTask);
                }
                catch (OperationCanceledException)
                {
                    KillActiveRunProcess();
                    AppendOutput("Server process stopped.");
                    throw;
                }

                if (process.ExitCode != 0)
                {
                    AppendOutput($"Server process exited with code {process.ExitCode}.", isError: true);
                }
                else
                {
                    AppendOutput("Server process exited.");
                }
            }
            catch (OperationCanceledException)
            {
                Dispatcher.Invoke(() =>
                {
                    SetOutputText(output + "\nExecution cancelled by user.");
                    SwitchToTab("output");
                    UpdateButtonStates();
                });
            }
            catch (Exception ex)
            {
                AppendOutput($"Error: {ex.Message}", isError: true);
            }
            finally
            {
                KillActiveRunProcess();
                Dispatcher.Invoke(() =>
                {
                    _runTask = null;
                    _runCancellation?.Dispose();
                    _runCancellation = null;
                    UpdateButtonStates();
                });
            }
        }, token);

        UpdateButtonStates();
    }

    private static async Task ReadRunProcessStreamAsync(TextReader reader, Action<string> appendOutput, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                break;
            }

            appendOutput(line);
        }
    }

    private void KillActiveRunProcess()
    {
        Process? process;
        lock (_activeRunProcessLock)
        {
            process = _activeRunProcess;
            _activeRunProcess = null;
        }

        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup; process may have exited naturally.
        }
        finally
        {
            process.Dispose();
        }
    }

    private FullStackRunChoice? ShowFullStackRunDialog()
    {
        var dialog = new Window
        {
            Title = "Run Full-Stack MALDA App",
            Width = 520,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var fullStackRadio = new RadioButton
        {
            GroupName = "FullStackRunMode",
            Content = "Run full stack - start server and open client preview",
            IsChecked = true,
            Margin = new Thickness(16, 12, 16, 6)
        };

        var serverRadio = new RadioButton
        {
            GroupName = "FullStackRunMode",
            Content = "Run server only - compile @server/@shared target",
            Margin = new Thickness(16, 0, 16, 6)
        };

        var clientRadio = new RadioButton
        {
            GroupName = "FullStackRunMode",
            Content = "Preview client only - transpile @client/@shared target",
            Margin = new Thickness(16, 0, 16, 12)
        };

        var info = new TextBlock
        {
            Text = "This source contains both server and client target decorators, so it cannot be run as a single interpreter script.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 4)
        };

        var okButton = new Button
        {
            Content = "Run",
            Width = 90,
            Height = 28,
            Margin = new Thickness(0, 0, 10, 16),
            IsDefault = true
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 90,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 16),
            IsCancel = true
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        var panel = new StackPanel();
        panel.Children.Add(info);
        panel.Children.Add(fullStackRadio);
        panel.Children.Add(serverRadio);
        panel.Children.Add(clientRadio);
        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        FullStackRunChoice? choice = null;
        okButton.Click += (_, _) =>
        {
            choice = clientRadio.IsChecked == true
                ? FullStackRunChoice.ClientPreview
                : serverRadio.IsChecked == true
                    ? FullStackRunChoice.Server
                    : FullStackRunChoice.FullStack;
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            choice = null;
            dialog.Close();
        };

        dialog.ShowDialog();
        return choice;
    }

    private static bool IsFullStackSource(string source)
    {
        return FullStackSourceInspector.IsFullStackSource(source);
    }

    private static int ExtractFullStackHttpPort(string source)
    {
        return FullStackSourceInspector.ExtractHttpPort(source, 8090);
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
        
        // Do not clear tool calls log here so Edit mode tool calls persist when user then debugs
        UpdateToolCallsDisplay();
        
        _debuggerService.Start();
        _debuggerHook = new DebuggerHook(_debuggerService);
        _debuggerHook.SetDebugMode(DebugMode.Continue);
        _debuggerHook.OnPaused += (line, file) =>
        {
            Dispatcher.Invoke(() =>
            {
                // Update output immediately when debugger pauses
                SetOutputText(_executionService.GetCurrentOutput());
                
                // Update debug info from interpreter
                var interpreter = _executionService.GetCurrentInterpreter();
                if (interpreter != null && _debuggerHook != null)
                {
                    _debuggerHook.UpdateDebugInfo(interpreter);
                }
                UpdateDebugInfo();
                HighlightCurrentLine(line);
                SwitchToTab("output");
                UpdateButtonStates();
            });
        };
        
        _debugCancellation = new CancellationTokenSource();
        _debugTask = Task.Run(async () =>
        {
            try
            {
                var result = await _executionService.ExecuteWithDebuggerAsync(source, _debuggerHook, input, fileName);
                
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        SetOutputText($"{_executionService.GetCurrentOutput()}\n\nError: {result.Error}", isError: true);
                    }
                    else
                    {
                        SetOutputText(_executionService.GetCurrentOutput());
                    }
                    UpdateDebugInfo();
                    UpdateButtonStates();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    SetOutputText($"Error: {ex.Message}", isError: true);
                    ClearCurrentLineHighlight();
                });
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    _debuggerService.Stop();
                    ClearCurrentLineHighlight();
                    UpdateDebugInfo();
                    UpdateButtonStates();
                });
            }
        }, _debugCancellation.Token);
        
        UpdateButtonStates();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopActiveExecution();
    }

    private void StopActiveExecution()
    {
        // Stop debug execution if running
        _debugCancellation?.Cancel();
        _debuggerService.Stop();
        _debuggerHook = null;
        _debugTask = null;
        _debugCancellation?.Dispose();
        _debugCancellation = null;
        
        // Stop regular run execution if running
        _runCancellation?.Cancel();
        KillActiveRunProcess();
        _runTask = null;
        _runCancellation?.Dispose();
        _runCancellation = null;
        
        ClearCurrentLineHighlight();
        UpdateButtonStates();
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

    private async void CompileButton_Click(object sender, RoutedEventArgs e)
    {
        SaveEditorIntoActiveDocument();
        var activeDocument = GetActiveDocument();
        var sourceForExecution = GetSourceForExecution(activeDocument);
        var source = sourceForExecution.Source;
        if (string.IsNullOrWhiteSpace(source))
        {
            MessageBox.Show("Please enter some code to compile.", "No Code", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Show compilation mode selection dialog
        var modeDialog = new Window
        {
            Title = "Compilation Options",
            Width = 450,
            Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var interpreterRadio = new RadioButton
        {
            GroupName = "CompilationMode",
            Content = "Interpreter executable - Embed source and run via interpreter runtime",
            IsChecked = true,
            Margin = new Thickness(10, 10, 10, 6)
        };

        var transpileRadio = new RadioButton
        {
            GroupName = "CompilationMode",
            Content = "Transpile to C# executable - Convert to C# and compile to native executable",
            Margin = new Thickness(10, 10, 10, 6)
        };

        var dllRadio = new RadioButton
        {
            GroupName = "CompilationMode",
            Content = "Transpile to DLL - Convert to C# and compile as .NET library",
            Margin = new Thickness(10, 0, 10, 6)
        };

        var javascriptRadio = new RadioButton
        {
            GroupName = "CompilationMode",
            Content = "Transpile to JavaScript - Generate browser-ready JavaScript (.js)",
            Margin = new Thickness(10, 0, 10, 10)
        };

        var pwaRadio = new RadioButton
        {
            GroupName = "CompilationMode",
            Content = "Transpile to PWA - Generate a Progressive Web App output directory",
            Margin = new Thickness(10, 0, 10, 10)
        };

        var executableGroup = new GroupBox
        {
            Header = "Executable Output",
            Margin = new Thickness(20, 20, 20, 10),
            Content = new StackPanel
            {
                Children =
                {
                    interpreterRadio
                }
            }
        };

        var transpileGroup = new GroupBox
        {
            Header = "Transpiled Output",
            Margin = new Thickness(20, 0, 20, 10),
            Content = new StackPanel
            {
                Children =
                {
                    transpileRadio,
                    dllRadio,
                    javascriptRadio,
                    pwaRadio
                }
            }
        };

        var includeLLamaSharpCheckbox = new CheckBox
        {
            Content = "Include LLamaSharp and its dependencies",
            Margin = new Thickness(20, 0, 20, 20),
            IsChecked = false
        };
        includeLLamaSharpCheckbox.ToolTip = "Only applicable to executable and DLL outputs. Not used for JavaScript or PWA outputs.";

        var okButton = new Button
        {
            Content = "OK",
            Width = 75,
            Height = 25,
            Margin = new Thickness(0, 0, 10, 20),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 75,
            Height = 25,
            Margin = new Thickness(0, 0, 20, 20),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 0)
        };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        void UpdateLlamaOptionAvailability()
        {
            var isBrowserOutputMode = javascriptRadio.IsChecked == true || pwaRadio.IsChecked == true;
            includeLLamaSharpCheckbox.IsEnabled = !isBrowserOutputMode;
            if (isBrowserOutputMode)
            {
                includeLLamaSharpCheckbox.IsChecked = false;
            }
        }

        interpreterRadio.Checked += (_, _) => UpdateLlamaOptionAvailability();
        transpileRadio.Checked += (_, _) => UpdateLlamaOptionAvailability();
        dllRadio.Checked += (_, _) => UpdateLlamaOptionAvailability();
        javascriptRadio.Checked += (_, _) => UpdateLlamaOptionAvailability();
        pwaRadio.Checked += (_, _) => UpdateLlamaOptionAvailability();
        UpdateLlamaOptionAvailability();

        var mainPanel = new StackPanel();
        mainPanel.Children.Add(executableGroup);
        mainPanel.Children.Add(transpileGroup);
        mainPanel.Children.Add(includeLLamaSharpCheckbox);
        mainPanel.Children.Add(buttonPanel);

        modeDialog.Content = mainPanel;

        bool? dialogResult = null;
        okButton.Click += (s, args) => { dialogResult = true; modeDialog.Close(); };
        cancelButton.Click += (s, args) => { dialogResult = false; modeDialog.Close(); };

        modeDialog.ShowDialog();

        if (dialogResult != true)
        {
            return;
        }

        Compiler.CompilationMode compilationMode;
        if (interpreterRadio.IsChecked == true)
        {
            compilationMode = Compiler.CompilationMode.Interpreter;
        }
        else if (javascriptRadio.IsChecked == true)
        {
            compilationMode = Compiler.CompilationMode.JavaScript;
        }
        else if (pwaRadio.IsChecked == true)
        {
            compilationMode = Compiler.CompilationMode.PWA;
        }
        else if (dllRadio.IsChecked == true)
        {
            compilationMode = Compiler.CompilationMode.TranspileToDll;
        }
        else
        {
            compilationMode = Compiler.CompilationMode.TranspileToCSharp;
        }
        
        var includeLLamaSharp = includeLLamaSharpCheckbox.IsChecked == true;

        // Get output path from user
        string outputPath;
        if (compilationMode == Compiler.CompilationMode.PWA)
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "Select PWA Output Folder"
            };

            if (folderDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(folderDialog.FolderName))
            {
                return;
            }

            outputPath = folderDialog.FolderName;
        }
        else
        {
            var defaultExt = compilationMode switch
            {
                Compiler.CompilationMode.TranspileToDll => "dll",
                Compiler.CompilationMode.JavaScript => "js",
                _ => "zip"
            };
            var defaultFileName = compilationMode switch
            {
                Compiler.CompilationMode.TranspileToDll => "program.dll",
                Compiler.CompilationMode.JavaScript => "program.js",
                _ => "program.zip"
            };
            var filter = compilationMode switch
            {
                Compiler.CompilationMode.TranspileToDll => "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
                Compiler.CompilationMode.JavaScript => "JavaScript Files (*.js)|*.js|All Files (*.*)|*.*",
                _ => "Zip Files (*.zip)|*.zip|Executable Files (*.exe)|*.exe|All Files (*.*)|*.*"
            };

            var saveDialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = defaultFileName
            };

            if (saveDialog.ShowDialog() != true)
            {
                return;
            }

            outputPath = saveDialog.FileName;
        }

        var isJavaScript = compilationMode == Compiler.CompilationMode.JavaScript;
        var isPwa = compilationMode == Compiler.CompilationMode.PWA;
        var isZip = !isJavaScript && outputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var isDll = compilationMode == Compiler.CompilationMode.TranspileToDll;
        var tempExePath = isPwa
            ? outputPath
            : isZip || isDll
            ? Path.Combine(Path.GetTempPath(), $"spl_{Guid.NewGuid()}.{(isDll ? "dll" : "exe")}")
            : outputPath;

        // Show progress
        var progressWindow = new Window
        {
            Title = "Compiling...",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var progressText = new TextBlock
        {
            Margin = new Thickness(20),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(33, 33, 33))
        };

        var progressBar = new ProgressBar
        {
            Margin = new Thickness(20, 0, 20, 20),
            Height = 20,
            IsIndeterminate = true
        };

        var stackPanel = new StackPanel();
        stackPanel.Children.Add(progressText);
        stackPanel.Children.Add(progressBar);
        progressWindow.Content = stackPanel;

        _compilerService.OnProgress += (progress) =>
        {
            Dispatcher.Invoke(() =>
            {
                progressText.Text = progress.Message;
                progressBar.Value = progress.Percentage;
                progressBar.IsIndeterminate = progress.Percentage < 100;
            });
        };

        progressWindow.Show();

        try
        {
            Compiler.Compiler.CompilationResult result;
            
            // Check if we have a file path, otherwise use temp file
            if (sourceForExecution.UsesPhysicalFileOnDisk && File.Exists(sourceForExecution.SourceFilePath))
            {
                result = await _compilerService.CompileAsync(sourceForExecution.SourceFilePath, tempExePath, compilationMode, includeLLamaSharp);
            }
            else
            {
                result = await _compilerService.CompileFromTextAsync(source, tempExePath, compilationMode, includeLLamaSharp);
            }

            progressWindow.Close();

            if (result.Success)
            {
                string finalPath = outputPath;

                if (compilationMode == Compiler.CompilationMode.PWA)
                {
                    MessageBox.Show($"Compilation successful!\n\nPWA saved to:\n{finalPath}", "Compilation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (compilationMode == Compiler.CompilationMode.JavaScript)
                {
                    var distributionDirectory = Path.GetDirectoryName(Path.GetFullPath(finalPath)) ?? Directory.GetCurrentDirectory();
                    MessageBox.Show(
                        $"Compilation successful!\n\nJavaScript distribution saved to:\n{distributionDirectory}\n\nOpen index.html to run the app.",
                        "Compilation Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
                
                // For DLL mode, just copy the DLL to the output path
                if (compilationMode == Compiler.CompilationMode.TranspileToDll && result.OutputPath != null)
                {
                    if (File.Exists(result.OutputPath))
                    {
                        if (File.Exists(outputPath))
                            File.Delete(outputPath);
                        File.Copy(result.OutputPath, outputPath, true);
                        finalPath = outputPath;
                        // Clean up temp DLL
                        try
                        {
                            if (result.OutputPath != outputPath)
                                File.Delete(result.OutputPath);
                        }
                        catch { /* ignore */ }
                    }
                    MessageBox.Show($"Compilation successful!\n\nDLL saved to:\n{finalPath}", "Compilation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                // If user requested zip, create it with dependencies
                if (isZip && result.OutputPath != null)
                {
                    var zipPath = _compilerService.CreateZipWithDependencies(result.OutputPath, outputPath);
                    if (zipPath != null)
                    {
                        finalPath = zipPath;
                        // Clean up temp exe
                        try
                        {
                            if (File.Exists(result.OutputPath) && result.OutputPath != outputPath)
                                File.Delete(result.OutputPath);
                            var dllPath = Path.Combine(Path.GetDirectoryName(result.OutputPath) ?? "", "MaldaLang.dll");
                            if (File.Exists(dllPath))
                                File.Delete(dllPath);
                        }
                        catch { }
                    }
                    else
                    {
                        // Zip creation failed, just copy exe
                        if (File.Exists(result.OutputPath))
                        {
                            File.Copy(result.OutputPath, outputPath, true);
                        }
                    }
                }
                else if (isZip && result.OutputPath != null)
                {
                    // User wanted zip but we'll just copy the exe
                    File.Copy(result.OutputPath, outputPath, true);
                }

                MessageBox.Show(
                    $"Compilation successful!\n\nOutput saved to:\n{finalPath}",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            else
            {
                MessageBox.Show(
                    $"Compilation failed:\n\n{result.ErrorMessage}",
                    "Compilation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            MessageBox.Show(
                $"An error occurred during compilation:\n\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private async void PreviewWebButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await PreviewCurrentDocumentAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not open web preview.\n\n{ex.Message}",
                "Web Preview",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task PreviewCurrentDocumentAsync()
    {
        SaveEditorIntoActiveDocument();
        var activeDocument = GetActiveDocument();
        var activePath = GetPhysicalPath(activeDocument);
        var sourceForExecution = GetSourceForExecution(activeDocument);
        if (string.IsNullOrWhiteSpace(activePath))
        {
            throw new InvalidOperationException("Save the current file first so web preview can keep relative includes and asset paths working.");
        }

        if (IsHtmlPreviewDocument(activePath))
        {
            await OpenUriInWebUiPanelAsync(new Uri(Path.GetFullPath(activePath)), activePath, switchToTab: true, ensureUiHost: false);
            return;
        }

        var repoRoot = FindRepoRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("Could not locate the repository root needed for web preview assets.");
        }

        var hostPath = ResolveWebPreviewHostPath(repoRoot);
        string scriptPath;
        if (IsJavaScriptPreviewDocument(activePath))
        {
            scriptPath = Path.GetFullPath(activePath);
        }
        else if (IsMaldaPreviewDocument(activePath))
        {
            scriptPath = WriteWebPreviewJavaScriptArtifact(repoRoot, sourceForExecution.Source, activePath);
        }
        else
        {
            throw new InvalidOperationException("Web preview currently supports .malda, .malda.html, .js, and .html files.");
        }

        var previewUri = BuildWebPreviewHostUri(hostPath, repoRoot, scriptPath, Path.GetFileNameWithoutExtension(activePath));
        await OpenUriInWebUiPanelAsync(previewUri, previewUri.AbsoluteUri, switchToTab: true, ensureUiHost: false);
    }

    private static bool IsMaldaPreviewDocument(string filePath)
    {
        return filePath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".malda.html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJavaScriptPreviewDocument(string filePath)
    {
        return filePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHtmlPreviewDocument(string filePath)
    {
        if (filePath.EndsWith(".malda.html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return filePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveWebPreviewHostPath(string repoRoot)
    {
        var preferredHost = Path.Combine(repoRoot, DefaultWebPreviewHostFileName);
        if (File.Exists(preferredHost))
        {
            return preferredHost;
        }

        var fallbackHost = Path.Combine(repoRoot, "host.html");
        if (File.Exists(fallbackHost))
        {
            return fallbackHost;
        }

        return EnsureGeneratedWebPreviewHost(repoRoot);
    }

    private static string EnsureGeneratedWebPreviewHost(string repoRoot)
    {
        var previewDir = Path.Combine(repoRoot, PreviewArtifactsDirectoryName);
        Directory.CreateDirectory(previewDir);

        var generatedHost = Path.Combine(previewDir, DefaultWebPreviewHostFileName);
        File.WriteAllText(
            generatedHost,
            GeneratedWebPreviewHostHtml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return generatedHost;
    }

    private static Uri BuildWebPreviewHostUri(string hostPath, string repoRoot, string scriptPath, string title)
    {
        var hostDirectory = Path.GetDirectoryName(Path.GetFullPath(hostPath))
            ?? throw new InvalidOperationException("Could not resolve the web preview host directory.");
        var relativeScriptPath = Path.GetRelativePath(hostDirectory, scriptPath).Replace('\\', '/');
        var baseUri = new Uri(Path.GetFullPath(hostPath));
        var query = $"?script={Uri.EscapeDataString(relativeScriptPath)}&title={Uri.EscapeDataString(title)}";

        // Generated host lives under .malda-preview/; runtime assets stay at repo root.
        if (!string.Equals(Path.GetFullPath(hostDirectory), Path.GetFullPath(repoRoot), StringComparison.OrdinalIgnoreCase))
        {
            query +=
                "&runtime=" + Uri.EscapeDataString("../Examples/Web/wwwroot/malda-js-runtime.js") +
                "&three=" + Uri.EscapeDataString("../Examples/Web/wwwroot/vendor/three.min.js");
        }

        return new Uri(baseUri.AbsoluteUri + query);
    }

    private const string GeneratedWebPreviewHostHtml =
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>MALDA JavaScript App</title>
          <style>
            :root {
              color-scheme: dark;
              font-family: Arial, sans-serif;
            }

            body {
              margin: 0;
              background: #020617;
              color: #e2e8f0;
            }

            #status {
              padding: 10px 14px;
              border-bottom: 1px solid #1e293b;
              background: #0f172a;
              color: #94a3b8;
              font-size: 14px;
            }

            #status.error {
              color: #fecaca;
              background: #450a0a;
              border-bottom-color: #7f1d1d;
            }

            #app {
              min-height: calc(100vh - 45px);
            }
          </style>
        </head>
        <body>
          <div id="status">Loading MALDA web preview...</div>
          <div id="app"></div>
          <script>
            (function () {
              var params = new URLSearchParams(window.location.search);
              var config = {
                title: params.get("title") || "MALDA JavaScript App",
                three: params.get("three") || "../Examples/Web/wwwroot/vendor/three.min.js",
                runtime: params.get("runtime") || "../Examples/Web/wwwroot/malda-js-runtime.js",
                script: params.get("script") || "program.js",
                rootSelector: params.get("root") || "#app",
                entry: params.get("entry") || "auto"
              };

              var statusElement = document.getElementById("status");
              document.title = config.title;

              function setStatus(message, isError) {
                statusElement.textContent = message;
                statusElement.className = isError ? "error" : "";
              }

              function loadScript(src) {
                return new Promise(function (resolve, reject) {
                  var script = document.createElement("script");
                  script.src = src;
                  script.onload = resolve;
                  script.onerror = function () {
                    reject(new Error("Could not load script: " + src));
                  };
                  document.head.appendChild(script);
                });
              }

              async function runEntryPoint() {
                if (!window.MaldaApp) {
                  throw new Error("MaldaApp was not registered by " + config.script + ".");
                }

                if (config.entry === "bootstrap" && typeof window.MaldaApp.bootstrap === "function") {
                  await window.MaldaApp.bootstrap(config.rootSelector);
                  return;
                }

                if (config.entry === "main" && typeof window.MaldaApp.main === "function") {
                  await window.MaldaApp.main();
                  return;
                }

                if (config.entry === "renderRoot" && typeof window.MaldaApp.renderRoot === "function") {
                  await window.MaldaApp.renderRoot(config.rootSelector);
                  return;
                }

                if (typeof window.MaldaApp.bootstrap === "function") {
                  await window.MaldaApp.bootstrap(config.rootSelector);
                  return;
                }

                if (typeof window.MaldaApp.main === "function") {
                  await window.MaldaApp.main();
                  return;
                }

                if (typeof window.MaldaApp.renderRoot === "function") {
                  await window.MaldaApp.renderRoot(config.rootSelector);
                  return;
                }

                throw new Error("No supported MALDA entry point was found. Expected bootstrap(), main(), or renderRoot().");
              }

              async function start() {
                setStatus("Loading browser runtime...", false);
                await loadScript(config.three);
                await loadScript(config.runtime);

                setStatus("Loading " + config.script + "...", false);
                await loadScript(config.script);
                await runEntryPoint();

                setStatus("Loaded " + config.script, false);
              }

              start().catch(function (error) {
                console.error(error);
                setStatus(error && error.message ? error.message : "Web preview failed.", true);
              });
            })();
          </script>
        </body>
        </html>
        """;

    private static string WriteWebPreviewJavaScriptArtifact(string repoRoot, string source, string sourceFilePath)
    {
        var previewDir = Path.Combine(repoRoot, PreviewArtifactsDirectoryName);
        Directory.CreateDirectory(previewDir);

        var relativeSourcePath = Path.GetRelativePath(repoRoot, sourceFilePath);
        var outputFileName = SanitizePreviewArtifactName(relativeSourcePath) + ".js";
        var outputPath = Path.Combine(previewDir, outputFileName);

        var compiler = new Compiler.Compiler();
        var javaScript = compiler.TranspileToJavaScriptFromSource(source, sourceFilePath);
        File.WriteAllText(outputPath, javaScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return outputPath;
    }

    private static string SanitizePreviewArtifactName(string relativePath)
    {
        var builder = new StringBuilder(relativePath.Length);
        foreach (var ch in relativePath)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        var sanitized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "preview" : sanitized;
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_debuggerHook == null || !_debuggerService.State.IsPaused) return;
        
        SetOutputText(_executionService.GetCurrentOutput());
        _debuggerHook.SetDebugMode(DebugMode.Continue);
        _debuggerService.Resume();
        UpdateButtonStates();
    }

    private void StepOverButton_Click(object sender, RoutedEventArgs e)
    {
        if (_debuggerHook == null || !_debuggerService.State.IsPaused) return;
        
        SetOutputText(_executionService.GetCurrentOutput());
        UpdateDebugInfo();
        _debuggerHook.SetDebugMode(DebugMode.StepOver);
        _debuggerService.Resume();
        
        if (_debuggerService.State.CurrentLine.HasValue)
        {
            HighlightCurrentLine(_debuggerService.State.CurrentLine.Value);
        }
        UpdateButtonStates();
    }

    private void StepIntoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_debuggerHook == null || !_debuggerService.State.IsPaused) return;
        
        SetOutputText(_executionService.GetCurrentOutput());
        UpdateDebugInfo();
        _debuggerHook.SetDebugMode(DebugMode.StepInto);
        _debuggerService.Resume();
        
        if (_debuggerService.State.CurrentLine.HasValue)
        {
            HighlightCurrentLine(_debuggerService.State.CurrentLine.Value);
        }
        UpdateButtonStates();
    }

    private void StepOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_debuggerHook == null || !_debuggerService.State.IsPaused) return;
        
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
        
        SetOutputText(_executionService.GetCurrentOutput());
        UpdateDebugInfo();
        _debuggerHook.SetDebugMode(DebugMode.StepOut);
        _debuggerService.Resume();
        
        if (_debuggerService.State.CurrentLine.HasValue)
        {
            HighlightCurrentLine(_debuggerService.State.CurrentLine.Value);
        }
        UpdateButtonStates();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_debuggerHook == null || !_debuggerService.State.IsRunning || _debuggerService.State.IsPaused) return;
        
        _debuggerHook.SetDebugMode(DebugMode.Paused);
        _debuggerService.Pause();
        SetOutputText(_executionService.GetCurrentOutput());
        UpdateDebugInfo();
        
        if (_debuggerService.State.CurrentLine.HasValue)
        {
            HighlightCurrentLine(_debuggerService.State.CurrentLine.Value);
        }
        UpdateButtonStates();
    }

    private void HighlightCurrentLine(int line)
    {
        // Highlight the current line (line is already 1-based from the parser/lexer)
        CodeEditor.TextArea.Caret.Line = line;
        CodeEditor.ScrollToLine(line);
        
        if (_currentLineRenderer != null)
        {
            _currentLineRenderer.CurrentLine = line;
            CodeEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
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

    private void UpdateDebugInfo()
    {
        var interpreter = _executionService.GetCurrentInterpreter();
        if (interpreter != null)
        {
            var callStack = interpreter.GetCallStack();
            var variables = interpreter.GetVariables();
            
            // Update call stack
            CallStackListBox.Items.Clear();
            foreach (var frame in callStack)
            {
                var text = string.IsNullOrEmpty(frame.ClassName) 
                    ? $"{frame.FunctionName} ({frame.File}:{frame.Line})"
                    : $"{frame.ClassName}.{frame.FunctionName} ({frame.File}:{frame.Line})";
                CallStackListBox.Items.Add(text);
            }
            
            // Update variables
            VariablesListBox.Items.Clear();
            foreach (var variable in variables)
            {
                VariablesListBox.Items.Add($"{variable.Key} = {variable.Value}");
            }
        }
        
        UpdateBreakpointsPanel();
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

    private void ClearOutputButton_Click(object sender, RoutedEventArgs e)
    {
        SetOutputText("");
        ProgramInputTextBox.Text = "";
        _toolCallLogService.Clear();
        UpdateToolCallsDisplay();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveActiveDocument(showSuccessMessage: true);
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Simple Language Files (*.malda)|*.malda|All Files (*.*)|*.*",
            DefaultExt = "malda"
        };
        
        if (dialog.ShowDialog() == true)
        {
            // Stop debugger if running
            if (_debuggerService.State.IsRunning)
            {
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
            
            // Clear breakpoints for the old file before loading new file
            var oldFilePath = GetCurrentPhysicalFilePath();
            _debuggerService.ClearBreakpointsForFile(oldFilePath);
            
            OpenFileAndIncludedDocuments(dialog.FileName);
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
        
        OutputTabButton.Background = _activeTab == "output" ? activeBrush : inactiveBrush;
        DebugTabButton.Background = _activeTab == "debug" ? activeBrush : inactiveBrush;
        ToolCallsTabButton.Background = _activeTab == "toolcalls" ? activeBrush : inactiveBrush;
        ErrorsTabButton.Background = _activeTab == "errors" ? activeBrush : inactiveBrush;
        SearchTabButton.Background = _activeTab == "search" ? activeBrush : inactiveBrush;
        AITabButton.Background = _activeTab == "ai" ? activeBrush : inactiveBrush;
        WebUITabButton.Background = _activeTab == "webui" ? activeBrush : inactiveBrush;
        RefreshDocumentTabs();
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
        var definition = _symbolNavigationService.GetDefinition(CodeEditor.Text, line, column, GetCurrentSourceKey());
        if (definition == null)
        {
            MessageBox.Show("No definition found at the current cursor position.", "Go to Definition", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        NavigateToLocation(_activeDocumentKey, definition.Span.Line, definition.Span.Column, Math.Max(1, definition.Span.Length));
    }

    private void FindReferencesAtCaret()
    {
        SaveEditorIntoActiveDocument();
        var (line, column) = GetCursorPosition();
        var target = _symbolNavigationService.PrepareRename(CodeEditor.Text, line, column, GetCurrentSourceKey());
        if (target == null)
        {
            MessageBox.Show("Place the cursor on a symbol to find its references.", "Find References", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var references = _symbolNavigationService.GetReferences(CodeEditor.Text, line, column, GetCurrentSourceKey());
        _searchResults.Clear();
        _searchResultNodes.Clear();
        _currentSearchResultIndex = -1;

        var document = new TextDocument(CodeEditor.Text);
        foreach (var reference in references)
        {
            if (!TryGetOffsetFromLocation(document, reference.Span.Line, reference.Span.Column, out var offset))
            {
                continue;
            }

            var location = document.GetLocation(offset);
            _searchResults.Add(new SearchResultItem
            {
                DocumentKey = _activeDocumentKey,
                Offset = offset,
                Length = Math.Max(1, reference.Span.Length),
                Line = location.Line,
                Column = location.Column,
                Preview = BuildSearchPreview(document, location.Line)
            });
        }

        PopulateSearchTree();
        SwitchToTab("search");
        SearchSummaryTextBlock.Text = _searchResults.Count == 0
            ? $"No references found for '{target.Name}'."
            : $"Found {_searchResults.Count} reference{(_searchResults.Count == 1 ? string.Empty : "s")} for '{target.Name}' in the current document.";

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
        var target = _symbolNavigationService.PrepareRename(CodeEditor.Text, line, column, GetCurrentSourceKey());
        if (target == null)
        {
            MessageBox.Show("Place the cursor on a symbol to rename it.", "Rename Symbol", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newName = PromptForSymbolName(target.Name);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, target.Name, StringComparison.Ordinal))
        {
            return;
        }

        var edits = _symbolNavigationService.Rename(CodeEditor.Text, line, column, newName, GetCurrentSourceKey());
        if (edits == null || edits.Count == 0)
        {
            MessageBox.Show("Rename could not be applied at the current cursor position.", "Rename Symbol", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplyTextEdits(edits);
        SaveEditorIntoActiveDocument();
        UpdateDiagnostics();
        RefreshOutline();
        MessageBox.Show($"Renamed '{target.Name}' to '{newName}' in the current document.", "Rename Symbol", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ApplyTextEdits(List<TextEditInfo> edits)
    {
        if (CodeEditor.Document == null)
        {
            return;
        }

        var orderedEdits = edits
            .OrderByDescending(edit => edit.Span.Line)
            .ThenByDescending(edit => edit.Span.Column)
            .ToList();

        foreach (var edit in orderedEdits)
        {
            if (!TryGetOffsetFromLocation(CodeEditor.Document, edit.Span.Line, edit.Span.Column, out var startOffset))
            {
                continue;
            }

            var maxLength = Math.Max(0, CodeEditor.Document.TextLength - startOffset);
            var replaceLength = Math.Min(edit.Span.Length, maxLength);
            CodeEditor.Document.Replace(startOffset, replaceLength, edit.NewText);
        }
    }

    private static bool TryGetOffsetFromLocation(TextDocument document, int zeroBasedLine, int zeroBasedColumn, out int offset)
    {
        offset = 0;
        var lineNumber = zeroBasedLine + 1;
        if (lineNumber <= 0 || lineNumber > document.LineCount)
        {
            return false;
        }

        try
        {
            var line = document.GetLineByNumber(lineNumber);
            offset = Math.Min(line.Offset + Math.Max(0, zeroBasedColumn), line.EndOffset);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string? PromptForSymbolName(string currentName)
    {
        var dialog = new Window
        {
            Title = "Rename Symbol",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = Brushes.White
        };

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
            Margin = new Thickness(0, 0, 0, 8)
        };

        var input = new TextBox
        {
            MinWidth = 280,
            Text = currentName,
            Margin = new Thickness(0, 0, 0, 12)
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
            IsDefault = true
        };
        okButton.Click += (_, _) => dialog.DialogResult = true;

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80
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
        // Find the Diagnostic associated with this button
        if (sender is Button button && button.Tag is Diagnostic diagnostic)
        {
            ApplySingleAutofix(diagnostic);
        }
    }

    private void ApplyAllAutofixes()
    {
        try
        {
            // Get all diagnostics with autofix suggestions
            var activeDocument = GetActiveDocument();
            var (source, sourceKey) = GetSourceForAnalysis(activeDocument);
            var diagnostics = _languageService.GetDiagnostics(
                source,
                sourceKey,
                strictTypesOptions: _typeAnalysisSettingsService.ToOptions());
            var autofixableDiagnostics = diagnostics
                .Where(d => d.AutoFix != null && d.AutoFix.IsSimpleCharacterFix)
                .ToList();

            if (autofixableDiagnostics.Count == 0)
            {
                MessageBox.Show("No autofixable errors found.", "Autofix", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Sort by position (reverse order to avoid offset issues when inserting)
            autofixableDiagnostics.Sort((a, b) =>
            {
                var lineCompare = b.Line.CompareTo(a.Line);
                if (lineCompare != 0) return lineCompare;
                return b.Column.CompareTo(a.Column);
            });

            // Apply all autofixes
            var fixedCount = 0;
            foreach (var diagnostic in autofixableDiagnostics)
            {
                var autofix = diagnostic.AutoFix;
                if (autofix == null) continue;

                try
                {
                    if (ApplyAutofixInternal(autofix))
                    {
                        fixedCount++;
                    }
                }
                catch
                {
                    // Skip if autofix fails (invalid position, etc.)
                    continue;
                }
            }

            // Update diagnostics
            UpdateDiagnostics();

            // Show results
            if (fixedCount > 0)
            {
                MessageBox.Show($"Successfully fixed {fixedCount} error{(fixedCount > 1 ? "s" : string.Empty)}.",
                    "Autofix Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error applying autofixes: {ex.Message}", "Autofix Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplySingleAutofix(Diagnostic diagnostic)
    {
        try
        {
            var autofix = diagnostic.AutoFix;
            if (autofix == null) return;

            if (ApplyAutofixInternal(autofix))
            {
                // Update diagnostics after successful fix
                UpdateDiagnostics();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error applying autofix: {ex.Message}", "Autofix Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ApplyAutofixInternal(AutoFixInfo autofix)
    {
        if (CodeEditor?.Document == null) return false;

        try
        {
            // Convert 0-based line to 1-based (AvalonEdit uses 1-based)
            var lineNumber = autofix.Line + 1;

            // Get the document line
            if (lineNumber <= 0 || lineNumber > CodeEditor.Document.LineCount)
            {
                return false;
            }

            var documentLine = CodeEditor.Document.GetLineByNumber(lineNumber);
            if (documentLine == null) return false;

            // Calculate start and end offsets
            // Column is 0-based, add to line offset
            var startOffset = documentLine.Offset + autofix.Column;
            var endOffset = startOffset + autofix.LengthToReplace;

            // Validate offsets
            if (startOffset < documentLine.Offset || endOffset > documentLine.EndOffset)
            {
                return false;
            }

            // Apply the fix
            CodeEditor.Document.Replace(startOffset, endOffset - startOffset, autofix.TextToInsert);
            return true;
        }
        catch
        {
            return false;
        }
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
        CodeEditor.Text = newCode;
        _fileService.SetContent(newCode);
        // Update diagnostics
        _diagnosticsTimer?.Stop();
        _diagnosticsTimer?.Start();
        // Update AI chat panel context
        UpdateAIChatPanelContext();
    }
    
    // Menu Event Handlers
    
    // File Menu
    private void FileNew_Click(object sender, RoutedEventArgs e)
    {
        // Stop debugger if running
        if (_debuggerService.State.IsRunning)
        {
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
    
    private void FileSave_Click(object sender, RoutedEventArgs e)
    {
        SaveButton_Click(sender, e);
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

        NavigateToOffset(result.DocumentKey, result.Offset, result.Length);
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
    
    // Debug Menu
    private void DebugToggleBreakpoint_Click(object sender, RoutedEventArgs e)
    {
        var line = CodeEditor.TextArea.Caret.Line - 1; // Convert to 0-based
        var activeDocument = GetActiveDocument();
        var filePath = GetPhysicalPath(activeDocument) ?? "main.malda";
        if (IsVirtualDocument(activeDocument))
        {
            line += activeDocument.VirtualStartLine;
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
            "  Alt+F4 - Exit\n\n" +
            "Edit:\n" +
            "  Ctrl+Z - Undo\n" +
            "  Ctrl+Y - Redo\n" +
            "  Ctrl+X - Cut\n" +
            "  Ctrl+C - Copy\n" +
            "  Ctrl+V - Paste\n" +
            "  Ctrl+A - Select All\n" +
            "  Ctrl+F - Find\n\n" +
            "View:\n" +
            "  Ctrl+Shift+L - Toggle Syntax Panel\n\n" +
            "Run:\n" +
            "  F5 - Run\n" +
            "  F9 - Debug\n" +
            "  Shift+F5 - Stop\n" +
            "  Ctrl+Shift+B - Compile\n\n" +
            "Debug:\n" +
            "  F5 - Continue\n" +
            "  F10 - Step Over\n" +
            "  F11 - Step Into\n" +
            "  Shift+F11 - Step Out\n" +
            "  F9 - Toggle Breakpoint";
        
        MessageBox.Show(shortcuts, "Keyboard Shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
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