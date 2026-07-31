// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Models;

public class DiffResult
{
    public List<DiffLine> Lines { get; set; } = new();
}

public class DiffLine
{
    public DiffLineType Type { get; set; }
    public int? OriginalLineNumber { get; set; }
    public int? NewLineNumber { get; set; }
    public string? OriginalContent { get; set; }
    public string? NewContent { get; set; }
}

public enum DiffLineType
{
    Unchanged,
    Added,
    Removed,
    Modified
}