// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class UnaryExpression : Expression
{
    public TokenType Operator { get; }
    public Expression Right { get; }
    
    public UnaryExpression(TokenType op, Expression right, int line = 0, int column = 0)
        : base(line, column)
    {
        Operator = op;
        Right = right;
    }
}