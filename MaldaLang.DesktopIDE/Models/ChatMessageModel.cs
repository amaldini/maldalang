// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Models;

public class ChatMessageModel
{
    public bool IsUser { get; set; }
    public string Content { get; set; } = "";
    public string? CodeBlock { get; set; }
    public bool HasCodeBlock { get; set; }
    public DiffResult? DiffResult { get; set; }
    public bool IsError { get; set; }
    public DateTime Timestamp { get; set; }
}