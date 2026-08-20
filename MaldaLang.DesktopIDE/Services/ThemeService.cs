// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Models;

namespace MaldaLang.DesktopIDE.Services;

public class ThemeService
{
    private Theme _currentTheme;
    private readonly Dictionary<string, Theme> _themes;
    private readonly string _settingsFilePath;
    
    public event EventHandler<Theme>? ThemeChanged;
    
    public ThemeService()
    {
        _themes = new Dictionary<string, Theme>();
        InitializeThemes();
        
        // Get the path to store settings in AppData
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "MaldaLang");
        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "theme-settings.json");
        
        // Load saved theme or default to Light
        var savedThemeName = LoadSavedTheme();
        _currentTheme = _themes.TryGetValue(savedThemeName, out var savedTheme) 
            ? savedTheme 
            : _themes["Light"];
    }
    
    public Theme CurrentTheme => _currentTheme;
    
    public IEnumerable<Theme> AvailableThemes => _themes.Values;
    
    public void SetTheme(string themeName)
    {
        if (_themes.TryGetValue(themeName, out var theme))
        {
            _currentTheme = theme;
            SaveTheme(themeName);
            ThemeChanged?.Invoke(this, theme);
        }
    }
    
    private string LoadSavedTheme()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<ThemeSettings>(json);
                return settings?.ThemeName ?? "Light";
            }
        }
        catch
        {
            // If loading fails, return default theme name
        }
        return "Light";
    }
    
    private void SaveTheme(string themeName)
    {
        try
        {
            var settings = new ThemeSettings { ThemeName = themeName };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // If saving fails, silently ignore
        }
    }
    
    private class ThemeSettings
    {
        public string ThemeName { get; set; } = "Light";
    }
    
    public Theme GetTheme(string themeName)
    {
        return _themes.TryGetValue(themeName, out var theme) ? theme : _themes["Light"];
    }
    
    private void InitializeThemes()
    {
        // Light Theme (default)
        _themes["Light"] = new Theme
        {
            Name = "Light",
            DisplayName = "Light",
            WindowBackground = Color.FromRgb(244, 245, 247),
            MainBackground = Color.FromRgb(244, 245, 247),
            SidebarBackground = Color.FromRgb(244, 245, 247),
            ToolbarBackground = Color.FromRgb(255, 255, 255),
            ToolbarBorder = Color.FromRgb(226, 228, 232),
            EditorBackground = Color.FromRgb(255, 255, 255),
            EditorForeground = Color.FromRgb(31, 35, 40),
            EditorLineNumbers = Color.FromRgb(110, 119, 129),
            EditorSelection = Color.FromRgb(191, 219, 254),
            TextForeground = Color.FromRgb(31, 35, 40),
            TextSecondary = Color.FromRgb(101, 109, 118),
            ButtonBackground = Color.FromRgb(240, 241, 243),
            ButtonForeground = Color.FromRgb(31, 35, 40),
            ButtonBorder = Color.FromRgb(226, 228, 232),
            ButtonHover = Color.FromRgb(232, 234, 237),
            ButtonHoverBorder = Color.FromRgb(201, 205, 212),
            PrimaryButtonBackground = Color.FromRgb(37, 99, 235),
            PrimaryButtonForeground = Color.FromRgb(255, 255, 255),
            PrimaryButtonBorder = Color.FromRgb(29, 78, 216),
            PrimaryButtonHover = Color.FromRgb(29, 78, 216),
            PrimaryButtonHoverBorder = Color.FromRgb(30, 64, 175),
            TabBackground = Color.FromRgb(236, 238, 241),
            TabActiveBackground = Color.FromRgb(255, 255, 255),
            TabForeground = Color.FromRgb(31, 35, 40),
            TabHover = Color.FromRgb(226, 228, 232),
            InputBackground = Color.FromRgb(255, 255, 255),
            InputForeground = Color.FromRgb(31, 35, 40),
            InputBorder = Color.FromRgb(208, 215, 222),
            BorderColor = Color.FromRgb(226, 228, 232),
            GridSplitterBackground = Color.FromRgb(232, 234, 237),
            ListBackground = Color.FromRgb(255, 255, 255),
            ListForeground = Color.FromRgb(31, 35, 40),
            ListBorder = Color.FromRgb(226, 228, 232),
            DebugAccent = Color.FromRgb(37, 99, 235),
            ErrorColor = Color.FromRgb(220, 38, 38),
            WarningColor = Color.FromRgb(217, 119, 6),
            InfoColor = Color.FromRgb(37, 99, 235)
        };
        
        // Dark Theme
        _themes["Dark"] = new Theme
        {
            Name = "Dark",
            DisplayName = "Dark",
            WindowBackground = Color.FromRgb(30, 30, 30),
            MainBackground = Color.FromRgb(30, 30, 30),
            SidebarBackground = Color.FromRgb(37, 37, 38),
            ToolbarBackground = Color.FromRgb(45, 45, 48),
            ToolbarBorder = Color.FromRgb(62, 62, 66),
            EditorBackground = Color.FromRgb(30, 30, 30),
            EditorForeground = Color.FromRgb(212, 212, 212),
            EditorLineNumbers = Color.FromRgb(133, 133, 133),
            EditorSelection = Color.FromRgb(38, 79, 120),
            TextForeground = Color.FromRgb(220, 220, 220),
            TextSecondary = Color.FromRgb(160, 160, 160),
            ButtonBackground = Color.FromRgb(55, 55, 58),
            ButtonForeground = Color.FromRgb(220, 220, 220),
            ButtonBorder = Color.FromRgb(62, 62, 66),
            ButtonHover = Color.FromRgb(70, 70, 74),
            ButtonHoverBorder = Color.FromRgb(90, 90, 94),
            PrimaryButtonBackground = Color.FromRgb(37, 99, 235),
            PrimaryButtonForeground = Color.FromRgb(255, 255, 255),
            PrimaryButtonBorder = Color.FromRgb(29, 78, 216),
            PrimaryButtonHover = Color.FromRgb(29, 78, 216),
            PrimaryButtonHoverBorder = Color.FromRgb(30, 64, 175),
            TabBackground = Color.FromRgb(45, 45, 48),
            TabActiveBackground = Color.FromRgb(30, 30, 30),
            TabForeground = Color.FromRgb(220, 220, 220),
            TabHover = Color.FromRgb(62, 62, 66),
            InputBackground = Color.FromRgb(37, 37, 38),
            InputForeground = Color.FromRgb(220, 220, 220),
            InputBorder = Color.FromRgb(62, 62, 66),
            BorderColor = Color.FromRgb(62, 62, 66),
            GridSplitterBackground = Color.FromRgb(45, 45, 48),
            ListBackground = Color.FromRgb(37, 37, 38),
            ListForeground = Color.FromRgb(220, 220, 220),
            ListBorder = Color.FromRgb(62, 62, 66),
            DebugAccent = Color.FromRgb(59, 130, 246),
            ErrorColor = Color.FromRgb(248, 113, 113),
            WarningColor = Color.FromRgb(251, 191, 36),
            InfoColor = Color.FromRgb(96, 165, 250)
        };
        
        // Blue Theme
        _themes["Blue"] = new Theme
        {
            Name = "Blue",
            DisplayName = "Blue",
            WindowBackground = Color.FromRgb(240, 248, 255),
            MainBackground = Color.FromRgb(240, 248, 255),
            SidebarBackground = Color.FromRgb(230, 240, 250),
            ToolbarBackground = Color.FromRgb(255, 255, 255),
            ToolbarBorder = Color.FromRgb(173, 216, 230),
            EditorBackground = Color.FromRgb(255, 255, 255),
            EditorForeground = Color.FromRgb(25, 25, 112),
            EditorLineNumbers = Color.FromRgb(100, 149, 237),
            EditorSelection = Color.FromRgb(173, 216, 230),
            TextForeground = Color.FromRgb(25, 25, 112),
            TextSecondary = Color.FromRgb(70, 130, 180),
            ButtonBackground = Color.FromRgb(230, 240, 250),
            ButtonForeground = Color.FromRgb(25, 25, 112),
            ButtonBorder = Color.FromRgb(173, 216, 230),
            ButtonHover = Color.FromRgb(200, 220, 240),
            ButtonHoverBorder = Color.FromRgb(100, 149, 237),
            PrimaryButtonBackground = Color.FromRgb(55, 110, 160),
            PrimaryButtonForeground = Color.FromRgb(255, 255, 255),
            PrimaryButtonBorder = Color.FromRgb(45, 95, 145),
            PrimaryButtonHover = Color.FromRgb(45, 95, 145),
            PrimaryButtonHoverBorder = Color.FromRgb(35, 80, 125),
            TabBackground = Color.FromRgb(230, 240, 250),
            TabActiveBackground = Color.FromRgb(255, 255, 255),
            TabForeground = Color.FromRgb(25, 25, 112),
            TabHover = Color.FromRgb(200, 220, 240),
            InputBackground = Color.FromRgb(255, 255, 255),
            InputForeground = Color.FromRgb(25, 25, 112),
            InputBorder = Color.FromRgb(173, 216, 230),
            BorderColor = Color.FromRgb(173, 216, 230),
            GridSplitterBackground = Color.FromRgb(200, 220, 240),
            ListBackground = Color.FromRgb(255, 255, 255),
            ListForeground = Color.FromRgb(25, 25, 112),
            ListBorder = Color.FromRgb(173, 216, 230),
            DebugAccent = Color.FromRgb(70, 130, 180),
            ErrorColor = Color.FromRgb(220, 20, 60),
            WarningColor = Color.FromRgb(255, 165, 0),
            InfoColor = Color.FromRgb(70, 130, 180)
        };
        
        // High Contrast Theme
        _themes["HighContrast"] = new Theme
        {
            Name = "HighContrast",
            DisplayName = "High Contrast",
            WindowBackground = Color.FromRgb(0, 0, 0),
            MainBackground = Color.FromRgb(0, 0, 0),
            SidebarBackground = Color.FromRgb(0, 0, 0),
            ToolbarBackground = Color.FromRgb(0, 0, 0),
            ToolbarBorder = Color.FromRgb(255, 255, 255),
            EditorBackground = Color.FromRgb(0, 0, 0),
            EditorForeground = Color.FromRgb(255, 255, 255),
            EditorLineNumbers = Color.FromRgb(255, 255, 0),
            EditorSelection = Color.FromRgb(255, 255, 0),
            TextForeground = Color.FromRgb(255, 255, 255),
            TextSecondary = Color.FromRgb(255, 255, 255),
            ButtonBackground = Color.FromRgb(0, 0, 0),
            ButtonForeground = Color.FromRgb(255, 255, 255),
            ButtonBorder = Color.FromRgb(255, 255, 255),
            ButtonHover = Color.FromRgb(255, 255, 0),
            ButtonHoverBorder = Color.FromRgb(255, 255, 255),
            PrimaryButtonBackground = Color.FromRgb(255, 255, 0),
            PrimaryButtonForeground = Color.FromRgb(0, 0, 0),
            PrimaryButtonBorder = Color.FromRgb(255, 255, 255),
            PrimaryButtonHover = Color.FromRgb(255, 255, 255),
            PrimaryButtonHoverBorder = Color.FromRgb(255, 255, 255),
            TabBackground = Color.FromRgb(0, 0, 0),
            TabActiveBackground = Color.FromRgb(0, 0, 0),
            TabForeground = Color.FromRgb(255, 255, 255),
            TabHover = Color.FromRgb(255, 255, 0),
            InputBackground = Color.FromRgb(0, 0, 0),
            InputForeground = Color.FromRgb(255, 255, 255),
            InputBorder = Color.FromRgb(255, 255, 255),
            BorderColor = Color.FromRgb(255, 255, 255),
            GridSplitterBackground = Color.FromRgb(255, 255, 255),
            ListBackground = Color.FromRgb(0, 0, 0),
            ListForeground = Color.FromRgb(255, 255, 255),
            ListBorder = Color.FromRgb(255, 255, 255),
            DebugAccent = Color.FromRgb(255, 255, 0),
            ErrorColor = Color.FromRgb(255, 0, 0),
            WarningColor = Color.FromRgb(255, 255, 0),
            InfoColor = Color.FromRgb(0, 255, 255)
        };

        // Dark+ — cooler GitHub-style dim dark (distinct from VS-like Dark)
        RegisterPalette(
            name: "DarkPlus",
            displayName: "Dark+",
            window: "#22272E",
            sidebar: "#2D333B",
            toolbar: "#2D333B",
            editorBg: "#22272E",
            editorFg: "#ADBAC7",
            lineNumbers: "#768390",
            selection: "#2F4D69",
            text: "#ADBAC7",
            secondary: "#768390",
            button: "#373E47",
            border: "#444C56",
            input: "#2D333B",
            primary: "#006CBE",
            primaryHover: "#005A9E",
            debug: "#539BF5",
            error: "#F85149",
            warning: "#D29922",
            info: "#6CB6FF");

        // One Dark — Atom / VS Code One Dark
        RegisterPalette(
            name: "OneDark",
            displayName: "One Dark",
            window: "#21252B",
            sidebar: "#21252B",
            toolbar: "#282C34",
            editorBg: "#282C34",
            editorFg: "#ABB2BF",
            lineNumbers: "#5C6370",
            selection: "#3E4451",
            text: "#ABB2BF",
            secondary: "#5C6370",
            button: "#3E4451",
            border: "#181A1F",
            input: "#21252B",
            primary: "#2B6CB0",
            primaryHover: "#245E9B",
            debug: "#61AFEF",
            error: "#E06C75",
            warning: "#E5C07B",
            info: "#56B6C2");

        // Nord — polar night
        RegisterPalette(
            name: "Nord",
            displayName: "Nord",
            window: "#2E3440",
            sidebar: "#3B4252",
            toolbar: "#3B4252",
            editorBg: "#2E3440",
            editorFg: "#D8DEE9",
            lineNumbers: "#4C566A",
            selection: "#434C5E",
            text: "#D8DEE9",
            secondary: "#81A1C1",
            button: "#434C5E",
            border: "#4C566A",
            input: "#3B4252",
            primary: "#4C6A94",
            primaryHover: "#435E84",
            debug: "#88C0D0",
            error: "#BF616A",
            warning: "#EBCB8B",
            info: "#81A1C1");

        // Dracula
        RegisterPalette(
            name: "Dracula",
            displayName: "Dracula",
            window: "#21222C",
            sidebar: "#21222C",
            toolbar: "#343746",
            editorBg: "#282A36",
            editorFg: "#F8F8F2",
            lineNumbers: "#6272A4",
            selection: "#44475A",
            text: "#F8F8F2",
            secondary: "#6272A4",
            button: "#44475A",
            border: "#44475A",
            input: "#21222C",
            primary: "#6D28D9",
            primaryHover: "#5B21B6",
            debug: "#BD93F9",
            error: "#FF5555",
            warning: "#F1FA8C",
            info: "#8BE9FD");

        // Monokai
        RegisterPalette(
            name: "Monokai",
            displayName: "Monokai",
            window: "#1E1F1A",
            sidebar: "#1E1F1A",
            toolbar: "#272822",
            editorBg: "#272822",
            editorFg: "#F8F8F2",
            lineNumbers: "#75715E",
            selection: "#49483E",
            text: "#F8F8F2",
            secondary: "#75715E",
            button: "#3E3D32",
            border: "#3E3D32",
            input: "#1E1F1A",
            primary: "#C2410C",
            primaryHover: "#9A3412",
            debug: "#A6E22E",
            error: "#F92672",
            warning: "#E6DB74",
            info: "#66D9EF");

        // Solarized Dark
        RegisterPalette(
            name: "SolarizedDark",
            displayName: "Solarized Dark",
            window: "#002B36",
            sidebar: "#073642",
            toolbar: "#073642",
            editorBg: "#002B36",
            editorFg: "#93A1A1",
            lineNumbers: "#586E75",
            selection: "#094959",
            text: "#93A1A1",
            secondary: "#839496",
            button: "#073642",
            border: "#0A4A58",
            input: "#073642",
            primary: "#1A6FA8",
            primaryHover: "#155C8C",
            debug: "#268BD2",
            error: "#DC322F",
            warning: "#B58900",
            info: "#2AA198");

        // Midnight — deep navy
        RegisterPalette(
            name: "Midnight",
            displayName: "Midnight",
            window: "#0B1220",
            sidebar: "#111827",
            toolbar: "#1F2937",
            editorBg: "#0F172A",
            editorFg: "#E5E7EB",
            lineNumbers: "#6B7280",
            selection: "#1E3A5F",
            text: "#E5E7EB",
            secondary: "#9CA3AF",
            button: "#1F2937",
            border: "#1F2937",
            input: "#111827",
            primary: "#2563EB",
            primaryHover: "#1D4ED8",
            debug: "#60A5FA",
            error: "#F87171",
            warning: "#FBBF24",
            info: "#38BDF8");

        // Forest — dark green
        RegisterPalette(
            name: "Forest",
            displayName: "Forest",
            window: "#0C1610",
            sidebar: "#122017",
            toolbar: "#1C2E24",
            editorBg: "#122017",
            editorFg: "#DDE8E0",
            lineNumbers: "#6B8574",
            selection: "#1F4A32",
            text: "#DDE8E0",
            secondary: "#8FA898",
            button: "#1C2E24",
            border: "#24382C",
            input: "#122017",
            primary: "#047857",
            primaryHover: "#065F46",
            debug: "#34D399",
            error: "#F87171",
            warning: "#FBBF24",
            info: "#6EE7B7");

        // Solarized Light
        RegisterPalette(
            name: "SolarizedLight",
            displayName: "Solarized Light",
            window: "#FDF6E3",
            sidebar: "#EEE8D5",
            toolbar: "#EEE8D5",
            editorBg: "#FDF6E3",
            editorFg: "#586E75",
            lineNumbers: "#93A1A1",
            selection: "#EEE8D5",
            text: "#586E75",
            secondary: "#657B83",
            button: "#EEE8D5",
            border: "#D6CDB8",
            input: "#FDF6E3",
            primary: "#1A6FA8",
            primaryHover: "#155C8C",
            debug: "#268BD2",
            error: "#DC322F",
            warning: "#B58900",
            info: "#2AA198");

        // Sepia — warm paper
        RegisterPalette(
            name: "Sepia",
            displayName: "Sepia",
            window: "#F4EDE1",
            sidebar: "#EDE4D4",
            toolbar: "#F7F1E6",
            editorBg: "#FBF6EE",
            editorFg: "#3F2F1E",
            lineNumbers: "#A08C74",
            selection: "#E6D3B3",
            text: "#3F2F1E",
            secondary: "#7A6854",
            button: "#EDE4D4",
            border: "#D9CBB6",
            input: "#FBF6EE",
            primary: "#9A3412",
            primaryHover: "#7C2D12",
            debug: "#B45309",
            error: "#B91C1C",
            warning: "#B45309",
            info: "#1D4ED8");
    }

    private void RegisterPalette(
        string name,
        string displayName,
        string window,
        string sidebar,
        string toolbar,
        string editorBg,
        string editorFg,
        string lineNumbers,
        string selection,
        string text,
        string secondary,
        string button,
        string border,
        string input,
        string primary,
        string primaryHover,
        string debug,
        string error,
        string warning,
        string info)
    {
        var windowColor = Hex(window);
        var sidebarColor = Hex(sidebar);
        var toolbarColor = Hex(toolbar);
        var editorBackground = Hex(editorBg);
        var editorForeground = Hex(editorFg);
        var textColor = Hex(text);
        var buttonColor = Hex(button);
        var borderColor = Hex(border);
        var inputColor = Hex(input);
        var primaryColor = Hex(primary);
        var primaryHoverColor = Hex(primaryHover);
        var hoverDelta = IsDarkLuminance(editorBackground) ? 18 : -16;

        _themes[name] = new Theme
        {
            Name = name,
            DisplayName = displayName,
            WindowBackground = windowColor,
            MainBackground = windowColor,
            SidebarBackground = sidebarColor,
            ToolbarBackground = toolbarColor,
            ToolbarBorder = borderColor,
            EditorBackground = editorBackground,
            EditorForeground = editorForeground,
            EditorLineNumbers = Hex(lineNumbers),
            EditorSelection = Hex(selection),
            TextForeground = textColor,
            TextSecondary = Hex(secondary),
            ButtonBackground = buttonColor,
            ButtonForeground = textColor,
            ButtonBorder = borderColor,
            ButtonHover = Shade(buttonColor, hoverDelta),
            ButtonHoverBorder = Shade(borderColor, hoverDelta),
            PrimaryButtonBackground = primaryColor,
            PrimaryButtonForeground = Colors.White,
            PrimaryButtonBorder = primaryHoverColor,
            PrimaryButtonHover = primaryHoverColor,
            PrimaryButtonHoverBorder = Shade(primaryHoverColor, -16),
            TabBackground = toolbarColor,
            TabActiveBackground = editorBackground,
            TabForeground = textColor,
            TabHover = Shade(toolbarColor, hoverDelta),
            InputBackground = inputColor,
            InputForeground = textColor,
            InputBorder = borderColor,
            BorderColor = borderColor,
            GridSplitterBackground = toolbarColor,
            ListBackground = inputColor,
            ListForeground = textColor,
            ListBorder = borderColor,
            DebugAccent = Hex(debug),
            ErrorColor = Hex(error),
            WarningColor = Hex(warning),
            InfoColor = Hex(info)
        };
    }

    private static bool IsDarkLuminance(Color color)
    {
        return ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0 < 0.5;
    }

    private static Color Shade(Color color, int delta)
    {
        return Color.FromRgb(
            (byte)Math.Clamp(color.R + delta, 0, 255),
            (byte)Math.Clamp(color.G + delta, 0, 255),
            (byte)Math.Clamp(color.B + delta, 0, 255));
    }

    private static Color Hex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        if (hex[0] == '#')
        {
            hex = hex[1..];
        }

        if (hex.Length != 6)
        {
            throw new ArgumentException($"Expected RRGGBB hex color, got '{hex}'.", nameof(hex));
        }

        return Color.FromRgb(
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }
}