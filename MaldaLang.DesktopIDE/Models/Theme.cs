// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows.Media;

namespace MaldaLang.DesktopIDE.Models;

public class Theme
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    
    // Window and main backgrounds
    public Color WindowBackground { get; set; }
    public Color MainBackground { get; set; }
    public Color SidebarBackground { get; set; }
    public Color ToolbarBackground { get; set; }
    public Color ToolbarBorder { get; set; }
    
    // Editor colors
    public Color EditorBackground { get; set; }
    public Color EditorForeground { get; set; }
    public Color EditorLineNumbers { get; set; }
    public Color EditorSelection { get; set; }
    
    // Text colors
    public Color TextForeground { get; set; }
    public Color TextSecondary { get; set; }
    
    // Button colors
    public Color ButtonBackground { get; set; }
    public Color ButtonForeground { get; set; }
    public Color ButtonBorder { get; set; }
    public Color ButtonHover { get; set; }
    public Color ButtonHoverBorder { get; set; }
    
    // Primary button colors
    public Color PrimaryButtonBackground { get; set; }
    public Color PrimaryButtonForeground { get; set; }
    public Color PrimaryButtonBorder { get; set; }
    public Color PrimaryButtonHover { get; set; }
    public Color PrimaryButtonHoverBorder { get; set; }
    
    // Tab colors
    public Color TabBackground { get; set; }
    public Color TabActiveBackground { get; set; }
    public Color TabForeground { get; set; }
    public Color TabHover { get; set; }
    
    // Input controls
    public Color InputBackground { get; set; }
    public Color InputForeground { get; set; }
    public Color InputBorder { get; set; }
    
    // Borders and separators
    public Color BorderColor { get; set; }
    public Color GridSplitterBackground { get; set; }
    
    // List/ListBox colors
    public Color ListBackground { get; set; }
    public Color ListForeground { get; set; }
    public Color ListBorder { get; set; }
    
    // Special colors
    public Color DebugAccent { get; set; }
    public Color ErrorColor { get; set; }
    public Color WarningColor { get; set; }
    public Color InfoColor { get; set; }
}