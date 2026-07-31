// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class LiteralPattern : Pattern
{
    public object? Value { get; }
    
    public LiteralPattern(object? value, int line = 0, int column = 0)
        : base(line, column)
    {
        Value = value;
    }
}
