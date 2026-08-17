// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Models;

public sealed class OpenDocument
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
