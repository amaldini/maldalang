// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Models;

public class Diagnostic
{
    public DiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public int Length { get; set; }
    public string? Source { get; set; }
    public AutoFixInfo? AutoFix { get; set; }
    public string LearningHint { get; set; } = string.Empty;
    public string SuggestedFix { get; set; } = string.Empty;
    public string RelatedExamplePath { get; set; } = string.Empty;
    public string RelatedExampleTitle { get; set; } = string.Empty;
    public string RelatedDocumentationPath { get; set; } = string.Empty;
    public string RelatedDocumentationTitle { get; set; } = string.Empty;
}

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}