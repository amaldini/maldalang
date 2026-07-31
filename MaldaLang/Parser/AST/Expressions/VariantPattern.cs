// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

using System.Collections.Generic;

public class VariantPattern : Pattern
{
    public string Tag { get; }
    public List<Pattern> PayloadPatterns { get; }

    public VariantPattern(string tag, List<Pattern> payloadPatterns, int line = 0, int column = 0)
        : base(line, column)
    {
        Tag = tag;
        PayloadPatterns = payloadPatterns ?? new List<Pattern>();
    }
}
