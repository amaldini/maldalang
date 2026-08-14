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

    public static IReadOnlyList<NamedArgumentExpression> ReadNamedArguments(Decorator decorator)
    {
        var named = new List<NamedArgumentExpression>();
        foreach (var arg in decorator.Arguments)
        {
            if (arg is NamedArgumentExpression namedArg)
                named.Add(namedArg);
        }

        return named;
    }

    public static bool TryReadResourceBudget(Decorator decorator, out MaldaLang.Interpreter.ResourceBudget? budget)
    {
        budget = null;
        if (decorator.Arguments.Count == 0)
            return false;

        int? tokens = null;
        int? tools = null;
        double? cost = null;

        foreach (var arg in decorator.Arguments)
        {
            if (arg is not NamedArgumentExpression named)
                return false;

            if (!TryReadPositiveNumberLiteral(named.Value, out var number))
                return false;

            if (string.Equals(named.Name, "tokens", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.HasValue || number != Math.Floor(number))
                    return false;
                tokens = (int)number;
            }
            else if (string.Equals(named.Name, "tools", StringComparison.OrdinalIgnoreCase))
            {
                if (tools.HasValue || number != Math.Floor(number))
                    return false;
                tools = (int)number;
            }
            else if (string.Equals(named.Name, "cost", StringComparison.OrdinalIgnoreCase))
            {
                if (cost.HasValue)
                    return false;
                cost = number;
            }
            else
            {
                return false;
            }
        }

        budget = new MaldaLang.Interpreter.ResourceBudget(tokens, tools, cost);
        return budget.HasAnyBound;
    }

    public static bool TryReadPositiveNumberLiteral(Expression expression, out double value)
    {
        value = 0;
        if (expression is not LiteralExpression literal)
            return false;

        value = literal.Value switch
        {
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            _ => 0
        };

        return value > 0;
    }
}
