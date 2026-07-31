// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Models;

public class Breakpoint
{
    public int Line { get; set; }
    public int Column { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Condition { get; set; }
    public string FilePath { get; set; } = string.Empty;
}