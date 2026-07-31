// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class InterpolatedStringSegment
{
    public bool IsExpression { get; }
    public string? Text { get; }
    public Expression? Expression { get; }
    
    public InterpolatedStringSegment(string text)
    {
        IsExpression = false;
        Text = text;
        Expression = null;
    }
    
    public InterpolatedStringSegment(Expression expression)
    {
        IsExpression = true;
        Text = null;
        Expression = expression;
    }
}

public class InterpolatedStringExpression : Expression
{
    public List<InterpolatedStringSegment> Segments { get; }
    
    public InterpolatedStringExpression(List<InterpolatedStringSegment> segments, int line = 0, int column = 0)
        : base(line, column)
    {
        Segments = segments;
    }
}