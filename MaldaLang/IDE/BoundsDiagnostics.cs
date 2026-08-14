// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.IDE.Models;

/// <summary>
/// Phase 6.3 / L3: validate <c>@within(ms)</c> and <c>@budget(...)</c> on functions and prompts
/// under <c>--strict-types</c>.
/// </summary>
public static class BoundsDiagnostics
{
    private static readonly HashSet<string> BudgetKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "tokens",
        "tools",
        "cost"
    };

    public static void Validate(
        IEnumerable<Statement> statements,
        StrictTypesOptions options,
        List<Diagnostic> diagnostics)
    {
        if (!options.StrictTypes)
            return;

        foreach (var func in CollectFunctions(statements))
        {
            ValidateWithinDecorator(func.Name, func.Decorators, diagnostics);
            ValidateBudgetDecorator(func.Name, func.Decorators, diagnostics);
        }

        foreach (var prompt in CollectPrompts(statements))
        {
            ValidateWithinDecorator(prompt.Name, prompt.Decorators, diagnostics);
            ValidateBudgetDecorator(prompt.Name, prompt.Decorators, diagnostics);
        }
    }

    private static void ValidateWithinDecorator(string name, IReadOnlyList<Decorator> decorators, List<Diagnostic> diagnostics)
    {
        var decorator = DecoratorArgs.FindDecoratorFromList(decorators, "within");
        if (decorator == null)
            return;

        if (DecoratorArgs.TryReadPositiveIntArgument(decorator, out _))
            return;

        diagnostics.Add(new Diagnostic
        {
            Line = decorator.Line,
            Column = decorator.Column,
            Severity = DiagnosticSeverity.Error,
            Source = "malda-bounds",
            Message = $"@within on '{name}' expects a single positive integer literal (milliseconds)."
        });
    }

    private static void ValidateBudgetDecorator(string name, IReadOnlyList<Decorator> decorators, List<Diagnostic> diagnostics)
    {
        var decorator = DecoratorArgs.FindDecoratorFromList(decorators, "budget");
        if (decorator == null)
            return;

        if (decorator.Arguments.Count == 0)
        {
            diagnostics.Add(BoundsError(decorator,
                $"@budget on '{name}' expects named keys tokens, tools, and/or cost (positive literals)."));
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hadError = false;
        foreach (var arg in decorator.Arguments)
        {
            if (arg is not NamedArgumentExpression named)
            {
                diagnostics.Add(BoundsError(decorator,
                    $"@budget on '{name}' does not accept positional arguments; use named keys (tokens, tools, cost)."));
                hadError = true;
                continue;
            }

            if (!BudgetKeys.Contains(named.Name))
            {
                diagnostics.Add(BoundsError(decorator,
                    $"@budget on '{name}' has unknown key '{named.Name}'. Allowed keys: tokens, tools, cost."));
                hadError = true;
                continue;
            }

            if (!seen.Add(named.Name))
            {
                diagnostics.Add(BoundsError(decorator,
                    $"@budget on '{name}' repeats key '{named.Name}'."));
                hadError = true;
                continue;
            }

            if (!DecoratorArgs.TryReadPositiveNumberLiteral(named.Value, out var number))
            {
                diagnostics.Add(BoundsError(decorator,
                    $"@budget on '{name}' key '{named.Name}' expects a positive numeric literal."));
                hadError = true;
                continue;
            }

            if (!string.Equals(named.Name, "cost", StringComparison.OrdinalIgnoreCase) &&
                number != Math.Floor(number))
            {
                diagnostics.Add(BoundsError(decorator,
                    $"@budget on '{name}' key '{named.Name}' expects a positive integer literal."));
                hadError = true;
            }
        }

        if (!hadError && !DecoratorArgs.TryReadResourceBudget(decorator, out _))
        {
            diagnostics.Add(BoundsError(decorator,
                $"@budget on '{name}' expects named keys tokens, tools, and/or cost (positive literals)."));
        }
    }

    private static Diagnostic BoundsError(Decorator decorator, string message) =>
        new()
        {
            Line = decorator.Line,
            Column = decorator.Column,
            Severity = DiagnosticSeverity.Error,
            Source = "malda-bounds",
            Message = message
        };

    private static IEnumerable<PromptDeclaration> CollectPrompts(IEnumerable<Statement> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is PromptDeclaration prompt)
                yield return prompt;
        }
    }

    private static IEnumerable<FunctionDeclaration> CollectFunctions(IEnumerable<Statement> statements)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case FunctionDeclaration func:
                    yield return func;
                    break;
                case ClassDeclaration classDecl:
                    foreach (var member in classDecl.Members)
                    {
                        if (member.Value is FunctionDeclaration method)
                            yield return method;
                    }
                    break;
            }
        }
    }
}
