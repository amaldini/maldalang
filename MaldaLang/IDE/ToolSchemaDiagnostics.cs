// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Warn when <c>@Tool</c> / <c>@MCPTool</c> is registered from an untyped signature with no schema.
/// </summary>
public static class ToolSchemaDiagnostics
{
    public static void Validate(IEnumerable<Statement> statements, List<Diagnostic> diagnostics)
    {
        foreach (var stmt in statements)
        {
            if (stmt is not FunctionDeclaration fn || fn.Decorators == null)
                continue;
            var tool = fn.Decorators.FirstOrDefault(d =>
                d.Name is "Tool" or "MCPTool");
            if (tool == null)
                continue;
            var hasSchemaArg = tool.Arguments != null && tool.Arguments.Count >= 3;
            var hasHints = fn.ParameterTypeHints != null
                && fn.ParameterTypeHints.Any(h => !string.IsNullOrEmpty(h));
            if (!hasSchemaArg && !hasHints)
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Source = "malda-tools",
                    Message = $"@{tool.Name} '{fn.Name}' has no schema and no typed parameters; all arguments will be strings.",
                    Line = Math.Max(0, fn.Line - 1),
                    Column = Math.Max(0, fn.Column - 1),
                    SuggestedFix = "Add parameter types or a schema name as the third decorator argument."
                });
            }
        }
    }
}
