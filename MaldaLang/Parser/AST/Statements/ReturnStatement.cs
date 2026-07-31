// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

public class ReturnStatement : Statement
{
    public Expression? Value { get; }
    
    public ReturnStatement(Expression? value = null, int line = 0, int column = 0)
        : base(line, column)
    {
        Value = value;
    }
}