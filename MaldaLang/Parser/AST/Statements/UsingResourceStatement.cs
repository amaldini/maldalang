// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST.Expressions;

namespace MaldaLang.Parser.AST.Statements;

public class UsingResourceStatement : Statement
{
    public string VariableName { get; }
    public Expression Initializer { get; }
    public BlockStatement Body { get; }

    public UsingResourceStatement(
        string variableName,
        Expression initializer,
        BlockStatement body,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        VariableName = variableName;
        Initializer = initializer;
        Body = body;
    }
}
