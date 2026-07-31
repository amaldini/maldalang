// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;
using MaldaLang;

public class AssignmentStatement : Statement
{
    public Expression Target { get; }
    public Expression Value { get; }
    public TokenType Operator { get; }
    
    public AssignmentStatement(Expression target, Expression value, TokenType op = TokenType.Assign, int line = 0, int column = 0)
        : base(line, column)
    {
        Target = target;
        Value = value;
        Operator = op;
    }
}