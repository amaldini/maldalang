// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Models;

public sealed class SyntaxSnippet
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Label { get; init; }
    public required string Description { get; init; }
    public required string TemplateText { get; init; }
    public required string Preview { get; init; }
}
