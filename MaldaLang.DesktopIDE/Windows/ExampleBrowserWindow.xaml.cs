// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using ICSharpCode.AvalonEdit.Highlighting;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.IDE;

namespace MaldaLang.DesktopIDE.Windows;

// Windows API declarations for title bar customization
internal static class ExampleBrowserNativeMethods
{
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_CAPTION_COLOR = 35;
    public const int DWMWA_TEXT_COLOR = 36;
    public const int DWMWA_BORDER_COLOR = 34;
}

public partial class ExampleBrowserWindow : Window
{
    private readonly ThemeService _themeService;
    private readonly ObservableCollection<string> _categories = new();
    private readonly ObservableCollection<ExampleProgram> _allExamples = new();
    private readonly ObservableCollection<ExampleProgram> _filteredExamples = new();
    private ExampleProgram? _selectedExample;
    private string _currentSearchQuery = string.Empty;
    private string? _selectedCategory;

    public ExampleProgram? SelectedExample { get; private set; }

    public ExampleBrowserWindow(ThemeService themeService)
    {
        InitializeComponent();

        _themeService = themeService;

        CategoriesListBox.ItemsSource = _categories;
        ExamplesListBox.ItemsSource = _filteredExamples;

        // Apply theme
        ApplyTheme(_themeService.CurrentTheme);
        
        // Subscribe to theme changes
        _themeService.ThemeChanged += OnThemeChanged;

        // Setup syntax highlighting
        SetupSyntaxHighlighting();

        LoadExamples();
    }
    
    private void OnThemeChanged(object? sender, Theme theme)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyTheme(theme);
            UpdateSyntaxHighlighting();
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
        Resources["EditorSelectionBrush"] = new SolidColorBrush(theme.EditorSelection);
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
        Resources["ErrorBrush"] = new SolidColorBrush(theme.ErrorColor);
        Resources["WarningBrush"] = new SolidColorBrush(theme.WarningColor);
        
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
        
        // Set ListBox selection colors
        Resources[SystemColors.ControlTextBrushKey] = new SolidColorBrush(theme.ListForeground);
        Resources[SystemColors.WindowTextBrushKey] = new SolidColorBrush(theme.TextForeground);
        Resources[SystemColors.HighlightBrushKey] = new SolidColorBrush(theme.EditorSelection);
        Resources[SystemColors.HighlightTextBrushKey] = new SolidColorBrush(theme.ListForeground);
        Resources[SystemColors.InactiveSelectionHighlightBrushKey] = new SolidColorBrush(theme.EditorSelection);
        Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = new SolidColorBrush(theme.ListForeground);
        
        // Update Avalon editor selection color
        if (PreviewEditor != null)
        {
            PreviewEditor.TextArea.SelectionBrush = new SolidColorBrush(theme.EditorSelection);
            PreviewEditor.TextArea.SelectionForeground = new SolidColorBrush(theme.EditorForeground);
        }
        
        // Update syntax highlighting when theme changes
        UpdateSyntaxHighlighting();
        
        // Update window title bar colors to match theme
        UpdateTitleBarColors(theme);
    }
    
    private void UpdateTitleBarColors(Theme theme)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
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
            ExampleBrowserNativeMethods.DwmSetWindowAttribute(hwnd, ExampleBrowserNativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            
            // Set title bar background color (caption color)
            // Convert WPF Color to ARGB int (0xAARRGGBB format)
            var captionColor = (int)((255 << 24) | (theme.WindowBackground.R << 16) | (theme.WindowBackground.G << 8) | theme.WindowBackground.B);
            ExampleBrowserNativeMethods.DwmSetWindowAttribute(hwnd, ExampleBrowserNativeMethods.DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            
            // Set title bar text color
            var textColor = (int)((255 << 24) | (theme.TextForeground.R << 16) | (theme.TextForeground.G << 8) | theme.TextForeground.B);
            ExampleBrowserNativeMethods.DwmSetWindowAttribute(hwnd, ExampleBrowserNativeMethods.DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
            
            // Set border color (optional, uses caption color if not set)
            var borderColor = (int)((255 << 24) | (theme.BorderColor.R << 16) | (theme.BorderColor.G << 8) | theme.BorderColor.B);
            ExampleBrowserNativeMethods.DwmSetWindowAttribute(hwnd, ExampleBrowserNativeMethods.DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
        catch
        {
            // If DWM API calls fail (e.g., on older Windows versions), silently ignore
            // The window will use default title bar styling
        }
    }
    
    private void SetupSyntaxHighlighting()
    {
        UpdateSyntaxHighlighting();
    }

    private IHighlightingDefinition CreateHighlightingDefinition()
    {
        // Use C# highlighting as a base (similar syntax)
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
        
        PreviewEditor.SyntaxHighlighting = highlightingDefinition;
    }
    
    private void SetHighlightingColor(IHighlightingDefinition definition, string name, Color color)
    {
        var namedColor = definition.NamedHighlightingColors.FirstOrDefault(nc => nc.Name == name);
        if (namedColor != null)
        {
            namedColor.Foreground = new SimpleHighlightingBrush(color);
        }
    }

    private void LoadExamples()
    {
        _allExamples.Clear();
        _categories.Clear();
        _filteredExamples.Clear();

        // Get examples sorted by category order (matching reference manual)
        var examples = ExampleProgramsService.GetExamplesSorted();
        
        foreach (var example in examples)
        {
            _allExamples.Add(example);
        }
        
        // Get categories sorted by display order
        var sortedCategories = ExampleProgramsService.GetCategoriesSorted();
        foreach (var category in sortedCategories)
        {
            _categories.Add(category);
        }

        // Select first category if available
        if (_categories.Count > 0)
        {
            _selectedCategory = _categories[0];
            CategoriesListBox.SelectedItem = _categories[0];
        }
        else
        {
            _selectedCategory = null;
        }

        ApplyFilters();
    }

    private void CategoriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedCategory = CategoriesListBox.SelectedItem as string;
        ApplyFilters();
    }

    private void ExamplesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedExample = ExamplesListBox.SelectedItem as ExampleProgram;
        
        if (_selectedExample != null)
        {
            PreviewEditor.Text = _selectedExample.Code;
            DescriptionTextBlock.Text = _selectedExample.Description;
            LoadButton.IsEnabled = true;
        }
        else
        {
            PreviewEditor.Text = string.Empty;
            DescriptionTextBlock.Text = string.Empty;
            LoadButton.IsEnabled = false;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentSearchQuery = SearchTextBox.Text ?? string.Empty;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        _filteredExamples.Clear();

        var filtered = _allExamples.AsEnumerable();

        // Filter by category
        if (!string.IsNullOrEmpty(_selectedCategory))
        {
            filtered = filtered.Where(ex => ex.Category == _selectedCategory);
        }

        // Filter by search query
        if (!string.IsNullOrEmpty(_currentSearchQuery))
        {
            var query = _currentSearchQuery.ToLowerInvariant();
            filtered = filtered.Where(ex =>
                ex.Name.ToLowerInvariant().Contains(query) ||
                ex.Description.ToLowerInvariant().Contains(query) ||
                (!string.IsNullOrEmpty(ex.Category) && ex.Category.ToLowerInvariant().Contains(query)));
        }

        // Sort filtered examples by category order, then by name
        foreach (var example in filtered.OrderBy(ex => ExampleProgramsService.GetCategoryOrder(ex.Category ?? ""))
                                        .ThenBy(ex => ex.Name))
        {
            _filteredExamples.Add(example);
        }

        // Clear selection if current selection is not in filtered list
        if (_selectedExample != null && !_filteredExamples.Contains(_selectedExample))
        {
            ExamplesListBox.SelectedItem = null;
            _selectedExample = null;
            PreviewEditor.Text = string.Empty;
            DescriptionTextBlock.Text = string.Empty;
            LoadButton.IsEnabled = false;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadExamples();
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedExample != null)
        {
            SelectedExample = _selectedExample;
            DialogResult = true;
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}