// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Expressions;

public class Decorator
{
    public string Name { get; }  // e.g., "GET", "POST", "PathParam"
    public List<Expression> Arguments { get; }  // e.g., ["/api/users"] or ["id"]
    public int Line { get; }
    public int Column { get; }
    
    public Decorator(string name, List<Expression> arguments, int line = 0, int column = 0)
    {
        Name = name;
        Arguments = arguments;
        Line = line;
        Column = column;
    }
}