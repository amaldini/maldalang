// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;

internal static class DecoratorArgs
{
    public static bool HasDecorator(FunctionDeclaration func, string name) =>
        HasDecorator(func.Decorators, name);

    public static bool HasDecorator(PromptDeclaration prompt, string name) =>
        HasDecorator(prompt.Decorators, name);

    public static bool HasDecorator(IReadOnlyList<Decorator> decorators, string name) =>
        decorators.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    public static Decorator? FindDecorator(FunctionDeclaration func, string name) =>
        FindDecoratorFromList(func.Decorators, name);

    public static Decorator? FindDecorator(PromptDeclaration prompt, string name) =>
        FindDecoratorFromList(prompt.Decorators, name);

    public static Decorator? FindDecoratorFromList(IReadOnlyList<Decorator> decorators, string name) =>
        decorators.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> ReadStringArguments(Decorator decorator)
    {
        var values = new List<string>();
        foreach (var arg in decorator.Arguments)
        {
            if (arg is LiteralExpression literal && literal.Value is string text)
                values.Add(text);
        }

        return values;
    }

    public static bool TryReadPositiveIntArgument(Decorator decorator, out int value)
    {
        value = 0;
        if (decorator.Arguments.Count != 1)
            return false;

        if (decorator.Arguments[0] is not LiteralExpression literal)
            return false;

        value = literal.Value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            _ => 0
        };

        return value > 0;
    }
}
