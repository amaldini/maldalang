// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class GraphLiteralExpression : Expression
{
    public bool IsDirected { get; }
    public Expression? NodesExpression { get; }
    public Expression? EdgesExpression { get; }
    
    public GraphLiteralExpression(bool isDirected, Expression? nodesExpression, Expression? edgesExpression, int line = 0, int column = 0)
        : base(line, column)
    {
        IsDirected = isDirected;
        NodesExpression = nodesExpression;
        EdgesExpression = edgesExpression;
    }
}
