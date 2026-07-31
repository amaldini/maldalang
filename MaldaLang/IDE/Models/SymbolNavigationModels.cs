// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Models;

public enum SymbolItemKind
{
    Class,
    Function,
    Method,
    Field,
    Variable,
    Actor,
    Prompt,
    Workflow,
    Step,
    Event,
    Object
}

public sealed class TextSpanInfo
{
    public int Line { get; set; }
    public int Column { get; set; }
    public int Length { get; set; }
}

public sealed class SymbolLocation
{
    public string? SourceKey { get; set; }
    public string Name { get; set; } = string.Empty;
    public TextSpanInfo Span { get; set; } = new();
}

public sealed class DocumentSymbolInfo
{
    public string Name { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public SymbolItemKind Kind { get; set; }
    public TextSpanInfo Span { get; set; } = new();
    public List<DocumentSymbolInfo> Children { get; set; } = new();
}

public sealed class WorkspaceDocumentInfo
{
    public string SourceKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed class WorkspaceSymbolInfo
{
    public string Name { get; set; } = string.Empty;
    public string? ContainerName { get; set; }
    public SymbolItemKind Kind { get; set; }
    public SymbolLocation Location { get; set; } = new();
}

public sealed class RenameTargetInfo
{
    public string Name { get; set; } = string.Empty;
    public TextSpanInfo Span { get; set; } = new();
}

public sealed class TextEditInfo
{
    public TextSpanInfo Span { get; set; } = new();
    public string NewText { get; set; } = string.Empty;
}

public sealed class WorkspaceTextEditInfo
{
    public string SourceKey { get; set; } = string.Empty;
    public TextSpanInfo Span { get; set; } = new();
    public string NewText { get; set; } = string.Empty;
}
