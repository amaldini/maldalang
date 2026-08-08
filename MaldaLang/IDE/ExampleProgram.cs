// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

public class ExampleProgram
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string AbsoluteFilePath { get; set; } = string.Empty;
    public string Track { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public int? Minutes { get; set; }
    public List<string> Concepts { get; set; } = new();
    public List<string> Prerequisites { get; set; } = new();
    /// <summary>
    /// Optional dependency tags from metadata (<c>offline</c>, <c>network</c>, <c>api-key</c>, <c>db</c>).
    /// Empty means treat as offline-friendly for the Web IDE playground filter.
    /// </summary>
    public List<string> Requires { get; set; } = new();
    public string LearningGoal { get; set; } = string.Empty;
    public string ExpectedOutput { get; set; } = string.Empty;
    public string Next { get; set; } = string.Empty;
    public string DocumentationPath { get; set; } = string.Empty;
    public string DocumentationTitle { get; set; } = string.Empty;
    public bool Featured { get; set; }
}