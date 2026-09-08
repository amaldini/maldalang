// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

public sealed class EvalCaseDeclaration : Statement
{
    public string Title { get; }
    public BlockStatement Body { get; }
    public List<Decorator> Decorators { get; }

    public EvalCaseDeclaration(
        string title,
        BlockStatement body,
        List<Decorator>? decorators = null,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        Title = title;
        Body = body;
        Decorators = decorators ?? new List<Decorator>();
    }

    public int Samples => IntDecorator("samples", 1);
    public double Threshold => DoubleDecorator("threshold", 1.0);
    public Expression? JudgePrompt => Decorators.FirstOrDefault(d => d.Name == "judge")?.Arguments?.FirstOrDefault();

    private int IntDecorator(string name, int fallback)
    {
        var dec = Decorators.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal));
        if (dec?.Arguments == null || dec.Arguments.Count == 0)
            return fallback;
        if (dec.Arguments[0] is LiteralExpression lit && lit.Value is int i)
            return i;
        return fallback;
    }

    private double DoubleDecorator(string name, double fallback)
    {
        var dec = Decorators.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal));
        if (dec?.Arguments == null || dec.Arguments.Count == 0)
            return fallback;
        if (dec.Arguments[0] is LiteralExpression lit)
        {
            if (lit.Value is int i)
                return i;
            if (lit.Value is double d)
                return d;
        }

        return fallback;
    }
}

/// <summary>
/// <c>suite "name" { case "…" { … } }</c>
/// </summary>
public sealed class SuiteDeclaration : Statement
{
    public string Title { get; }
    public List<EvalCaseDeclaration> Cases { get; }

    public SuiteDeclaration(string title, List<EvalCaseDeclaration> cases, int line = 0, int column = 0)
        : base(line, column)
    {
        Title = title;
        Cases = cases ?? new List<EvalCaseDeclaration>();
    }
}
