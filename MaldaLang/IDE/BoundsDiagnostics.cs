// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.IDE.Models;

/// <summary>
/// Phase 6.3: validate <c>@within(ms)</c> decorators on functions and prompts under <c>--strict-types</c>.
/// </summary>
public static class BoundsDiagnostics
{
    public static void Validate(
        IEnumerable<Statement> statements,
        StrictTypesOptions options,
        List<Diagnostic> diagnostics)
    {
        if (!options.StrictTypes)
            return;

        foreach (var func in CollectFunctions(statements))
            ValidateWithinDecorator(func.Name, func.Decorators, diagnostics);

        foreach (var prompt in CollectPrompts(statements))
            ValidateWithinDecorator(prompt.Name, prompt.Decorators, diagnostics);
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
