// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

public class BlockStatement : Statement
{
    public List<Statement> Statements { get; }
    
    public BlockStatement(List<Statement> statements, int line = 0, int column = 0)
        : base(line, column)
    {
        Statements = statements;
    }
}