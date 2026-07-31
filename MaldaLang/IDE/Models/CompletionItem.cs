// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Models;

public class CompletionItem
{
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = "text";
    public string? Detail { get; set; }
    public string? Documentation { get; set; }
    public string? InsertText { get; set; }
    public int? SortText { get; set; }
}

public class SignatureHelpInfo
{
    public string SignatureLabel { get; set; } = string.Empty;
    public List<string> Parameters { get; set; } = new();
    public int ActiveParameter { get; set; }
}