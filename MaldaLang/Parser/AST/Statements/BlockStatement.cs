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

    /// <summary>
    /// Interpreter cache: whether this block needs its own scope and defer frame
    /// (0 unknown, 1 no, 2 yes). See Interpreter.SyncEval.cs.
    /// </summary>
    internal byte ScopeShape;
}
