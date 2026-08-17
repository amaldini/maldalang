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

    /// <summary>
    /// Bare constructor name in a pattern (<c>case Ok:</c> / <c>case None:</c>):
    /// same tag and arity as <c>Ok()</c> / <c>None()</c>, with implicit <c>_</c> payloads.
    /// </summary>
    public static VariantPattern WithImplicitWildcards(string tag, int arity, int line = 0, int column = 0)
    {
        var payloads = new List<Pattern>(arity);
        for (var i = 0; i < arity; i++)
            payloads.Add(new WildcardPattern(line, column));
        return new VariantPattern(tag, payloads, line, column);
    }
}
