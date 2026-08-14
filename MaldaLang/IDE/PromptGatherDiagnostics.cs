// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// L2: <c>gather:</c> requires <c>-&gt; Type</c> and must not be combined with <c>tools:</c> (Mode B).
/// </summary>
public static class PromptGatherDiagnostics
{
    public static void Validate(IEnumerable<Statement> statements, List<Diagnostic> diagnostics)
    {
        foreach (var stmt in statements)
        {
            if (stmt is PromptDeclaration prompt)
                ValidatePrompt(prompt, diagnostics);
        }
    }

    private static void ValidatePrompt(PromptDeclaration prompt, List<Diagnostic> diagnostics)
    {
        var hasGather = false;
        var hasTools = false;
        var gatherLine = prompt.Line;
        var gatherColumn = prompt.Column;

        if (prompt.BodyType == PromptBodyType.Statements && prompt.StatementBody != null)
        {
            foreach (var stmt in prompt.StatementBody)
            {
                if (stmt is not PromptBodyStatement body)
                    continue;
                if (body.Keyword == "gather")
                {
                    hasGather = true;
                    gatherLine = body.Line;
                    gatherColumn = body.Column;
                }
                else if (body.Keyword == "tools")
                {
                    hasTools = true;
                }
            }
        }
        else if (prompt.BodyType == PromptBodyType.ObjectLiteral && prompt.ObjectBody != null)
        {
            foreach (var (key, _) in prompt.ObjectBody.Properties)
            {
                var name = TryGetKeyName(key);
                if (name == "gather")
                {
                    hasGather = true;
                    gatherLine = key.Line;
                    gatherColumn = key.Column;
                }
                else if (name == "tools")
                {
                    hasTools = true;
                }
            }
        }

        if (!hasGather)
            return;

        if (string.IsNullOrWhiteSpace(prompt.ReturnType))
        {
            diagnostics.Add(new Diagnostic
            {
                Line = gatherLine,
                Column = gatherColumn,
                Severity = DiagnosticSeverity.Error,
                Source = "malda-prompt",
                Message = $"Prompt '{prompt.Name}' uses gather: which requires a -> Type extract target (schema, sum type, or program(Api)).",
                RelatedExamplePath = "Prompts/prompt_tools_then_structured.malda",
                RelatedExampleTitle = "Gather-then-extract prompt"
            });
        }

        if (hasTools)
        {
            diagnostics.Add(new Diagnostic
            {
                Line = gatherLine,
                Column = gatherColumn,
                Severity = DiagnosticSeverity.Error,
                Source = "malda-prompt",
                Message = $"Prompt '{prompt.Name}' cannot list both gather: and tools:. Use gather: with -> Type for two-phase extract, or tools: for Mode B.",
                RelatedExamplePath = "Prompts/prompt_tools_then_structured.malda",
                RelatedExampleTitle = "Gather-then-extract prompt"
            });
        }
    }

    private static string? TryGetKeyName(Expression key)
    {
        if (key is LiteralExpression literal && literal.Value is string name)
            return name;
        return null;
    }
}
