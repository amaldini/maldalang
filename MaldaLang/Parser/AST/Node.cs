// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST;

public abstract class Node
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string? SourceFile { get; set; }
    
    protected Node(int line = 0, int column = 0)
    {
        Line = line;
        Column = column;
    }
}