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
            PrimaryButtonBackground = Color.FromRgb(70, 130, 180),
            PrimaryButtonForeground = Color.FromRgb(255, 255, 255),
            PrimaryButtonBorder = Color.FromRgb(65, 105, 225),
            PrimaryButtonHover = Color.FromRgb(65, 105, 225),
            PrimaryButtonHoverBorder = Color.FromRgb(30, 144, 255),
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
    }
}