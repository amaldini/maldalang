// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using System.Globalization;
using System.Linq;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

public class PropertyDeclaration : Statement
{
    public string Name { get; }
    public List<string> Parameters { get; }
    public BlockStatement Body { get; }
    public List<Decorator> Decorators { get; }

    public PropertyDeclaration(
        string name,
        List<string> parameters,
        BlockStatement body,
        List<Decorator>? decorators = null,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        Name = name;
        Parameters = parameters;
        Body = body;
        Decorators = decorators ?? new List<Decorator>();
    }

    public IReadOnlyList<string> GetRequiredCapabilities()
    {
        return GetDecoratorStringArguments("requires");
    }

    public IReadOnlyList<string> GetTargetModes()
    {
        return GetDecoratorStringArguments("targets");
    }

    private IReadOnlyList<string> GetDecoratorStringArguments(string decoratorName)
    {
        var decorator = Decorators.FirstOrDefault(d => string.Equals(d.Name, decoratorName, StringComparison.OrdinalIgnoreCase));
        if (decorator == null || decorator.Arguments.Count == 0)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>(decorator.Arguments.Count);
        foreach (var argument in decorator.Arguments)
        {
            if (TryGetArgumentString(argument, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return values;
    }

    private static bool TryGetArgumentString(Expression expression, out string? value)
    {
        switch (expression)
        {
            case LiteralExpression literal when literal.Value is string s:
                value = s;
                return true;
            case LiteralExpression literal when literal.Value is int i:
                value = i.ToString(CultureInfo.InvariantCulture);
                return true;
            case LiteralExpression literal when literal.Value is long l:
                value = l.ToString(CultureInfo.InvariantCulture);
                return true;
            case LiteralExpression literal when literal.Value is bool b:
                value = b ? "true" : "false";
                return true;
            case IdentifierExpression identifier:
                value = identifier.Name;
                return true;
            default:
                value = null;
                return false;
        }
    }
}
