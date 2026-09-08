// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Promotes silent gotchas from malda-gotchas.md into check diagnostics.
/// </summary>
public static class GotchaDiagnostics
{
    public static void Validate(IEnumerable<Statement> statements, List<Diagnostic> diagnostics)
    {
        foreach (var stmt in statements)
            Walk(stmt, diagnostics, inLoop: false);
    }

    private static void Walk(Statement stmt, List<Diagnostic> diagnostics, bool inLoop)
    {
        switch (stmt)
        {
            case WhileStatement loop:
                Walk(loop.Body, diagnostics, inLoop: true);
                break;
            case ForStatement loop:
                Walk(loop.Body, diagnostics, inLoop: true);
                break;
            case ForInStatement loop:
                Walk(loop.Body, diagnostics, inLoop: true);
                break;
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    Walk(inner, diagnostics, inLoop);
                break;
            case FunctionDeclaration fn:
                Walk(fn.Body, diagnostics, inLoop: false);
                break;
            case ExpressionStatement expr:
                CheckExpression(expr.Expression, diagnostics, inLoop, unusedValidate: true);
                break;
            case VarDeclStatement varDecl when varDecl.Initializer != null:
                CheckExpression(varDecl.Initializer, diagnostics, inLoop, unusedValidate: false);
                break;
        }
    }

    private static void CheckExpression(Expression expr, List<Diagnostic> diagnostics, bool inLoop, bool unusedValidate)
    {
        if (expr is FunctionCallExpression call)
        {
            var name = CalleeName(call);
            if (inLoop && (name == "input" || name == "io.input") && !HasQuitTestNearby(call))
            {
                diagnostics.Add(Warn(call, "io.input returns \"\" at EOF — a loop without a quit test never terminates.", "io.input()"));
            }

            if (name is "markup" or "AnsiConsole.markup")
            {
                diagnostics.Add(Warn(call, "AnsiConsole.markup does not add a newline; consecutive calls smear.", "AnsiConsole.markupLine(...)"));
            }

            if (unusedValidate && name == "validate")
            {
                diagnostics.Add(Warn(call, "validate() returns { ok, error } and does not throw; the result is unused.", "var checked = validate(...)"));
            }

            if (name == "arr.append" || (call.Callee is MemberAccessExpression arr && arr.Member == "append"
                && arr.Object is IdentifierExpression arrId && arrId.Name == "arr"))
            {
                diagnostics.Add(Warn(call, "There is no arr namespace. Did you mean items.append(x)?", "items.append(...)"));
            }

            if (name == "parseJson")
            {
                diagnostics.Add(Warn(call, "parseJson validates LLM JSON; parseJSON parses raw JSON. Prefer json.parseTyped.", "parseJSON(text) or json.parseTyped(text, schema)"));
            }

            foreach (var arg in call.Arguments)
                CheckExpression(arg, diagnostics, inLoop, unusedValidate: false);
        }

        if (expr is ObjectLiteralExpression obj)
        {
            foreach (var prop in obj.Properties)
            {
                if (prop.Key is LiteralExpression key && key.Value is string name
                    && name == "borderStyle" && prop.Value is LiteralExpression style
                    && style.Value is string styleName
                    && styleName is not ("square" or "rounded" or "heavy" or "double"))
                {
                    diagnostics.Add(Warn(prop.Value, $"panel borderStyle '{styleName}' silently degrades to square.", "\"square\""));
                }
            }
        }
    }

    private static bool HasQuitTestNearby(FunctionCallExpression _) => false;

    private static string CalleeName(FunctionCallExpression call) => call.Callee switch
    {
        IdentifierExpression id => id.Name,
        MemberAccessExpression member when member.Object is IdentifierExpression obj =>
            $"{obj.Name}.{member.Member}",
        MemberAccessExpression member => member.Member,
        _ => ""
    };

    private static Diagnostic Warn(Expression expr, string message, string didYouMean) =>
        new()
        {
            Severity = DiagnosticSeverity.Warning,
            Source = "malda-gotcha",
            Message = message,
            Line = expr.Line - 1,
            Column = Math.Max(0, expr.Column - 1),
            SuggestedFix = didYouMean
        };
}
