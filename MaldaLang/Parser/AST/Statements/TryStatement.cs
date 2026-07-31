// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

public class TryStatement : Statement
{
    public BlockStatement TryBlock { get; }
    public List<CatchClause> CatchClauses { get; }
    public BlockStatement? FinallyBlock { get; }
    
    public TryStatement(BlockStatement tryBlock, List<CatchClause> catchClauses, 
                       BlockStatement? finallyBlock = null, int line = 0, int column = 0)
        : base(line, column)
    {
        TryBlock = tryBlock;
        CatchClauses = catchClauses;
        FinallyBlock = finallyBlock;
    }
}
