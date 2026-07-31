// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

public class ThrowStatement : Statement
{
    public Expression Exception { get; }
    
    public ThrowStatement(Expression exception, int line = 0, int column = 0)
        : base(line, column)
    {
        Exception = exception;
    }
}
