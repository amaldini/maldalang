// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Models;

public class ChatResponse
{
    public string Content { get; set; } = "";
    public string? CodeBlock { get; set; }
    public bool HasCodeBlock => !string.IsNullOrEmpty(CodeBlock);
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
}