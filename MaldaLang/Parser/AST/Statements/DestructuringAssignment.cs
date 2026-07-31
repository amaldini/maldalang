// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

public class DestructuringAssignment : Statement
{
    public DestructuringPattern Pattern { get; }
    public Expression Value { get; }
    
    public DestructuringAssignment(DestructuringPattern pattern, Expression value, int line = 0, int column = 0)
        : base(line, column)
    {
        Pattern = pattern;
        Value = value;
    }
}
