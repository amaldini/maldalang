// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class RestPattern : Pattern
{
    public string? Name { get; }  // Name for the rest variable (e.g., "rest" in "...rest")
    
    public RestPattern(string? name = null, int line = 0, int column = 0)
        : base(line, column)
    {
        Name = name;
    }
}
