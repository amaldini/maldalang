// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Statements;

public abstract class ExecutionFrame
{
    public int StatementIndex { get; set; } = 0; // Which statement we're executing
    public Environment Environment { get; set; } = null!;
}

public class TopLevelFrame : ExecutionFrame
{
    public List<Statement> Statements { get; set; } = null!;
}

public class FunctionFrame : ExecutionFrame
{
    public FunctionValue Function { get; set; } = null!;
    public Environment? PreviousEnvironment { get; set; } // Environment to restore to when function completes
}

public class WhileLoopFrame : ExecutionFrame
{
    public WhileStatement Statement { get; set; } = null!;
    public BlockStatement? BodyBlock { get; set; } // If body is a block
    public int LoopIteration { get; set; } = 0; // Track which iteration we're on
    public bool ConditionEvaluated { get; set; } = false; // Whether condition was evaluated for current iteration
}

public class BlockFrame : ExecutionFrame
{
    public BlockStatement Statement { get; set; } = null!;
}