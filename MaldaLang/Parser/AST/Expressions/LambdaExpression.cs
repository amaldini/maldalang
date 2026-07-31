// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

using MaldaLang.Parser.AST.Statements;

public class LambdaExpression : Expression
{
    public List<string> Parameters { get; }
    public Expression? ExpressionBody { get; }  // For single expression
    public BlockStatement? BlockBody { get; }   // For block with statements
    
    public LambdaExpression(List<string> parameters, Expression? expressionBody, 
                           BlockStatement? blockBody, int line = 0, int column = 0)
        : base(line, column)
    {
        Parameters = parameters;
        ExpressionBody = expressionBody;
        BlockBody = blockBody;
    }
}
