// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class PostfixExpression : Expression
{
    public Expression Left { get; }
    public TokenType Operator { get; }
    
    public PostfixExpression(Expression left, TokenType op, int line = 0, int column = 0)
        : base(line, column)
    {
        Left = left;
        Operator = op;
    }
}