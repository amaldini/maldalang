// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

using System.Collections.Generic;

public class ArrayPattern : Pattern
{
    public List<Pattern> Elements { get; }
    public RestPattern? Rest { get; }
    
    public ArrayPattern(List<Pattern> elements, RestPattern? rest = null, int line = 0, int column = 0)
        : base(line, column)
    {
        Elements = elements;
        Rest = rest;
    }
}
