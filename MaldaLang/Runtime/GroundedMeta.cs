// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime;

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Propagating citation set attached to a <see cref="RuntimeValue"/>.
/// </summary>
public sealed class GroundedMeta
{
    public List<RuntimeValue> Citations { get; }
    public bool Partial { get; }
    public List<GroundedSpan>? UngroundedSpans { get; }

    public GroundedMeta(IEnumerable<RuntimeValue>? citations, bool partial = false, List<GroundedSpan>? ungroundedSpans = null)
    {
        Citations = citations?.ToList() ?? new List<RuntimeValue>();
        Partial = partial;
        UngroundedSpans = ungroundedSpans;
    }

    public bool HasCitations => Citations.Count > 0 && !Partial;

    public static GroundedMeta Union(GroundedMeta? a, GroundedMeta? b)
    {
        var citations = new List<RuntimeValue>();
        if (a != null)
            citations.AddRange(a.Citations);
        if (b != null)
            citations.AddRange(b.Citations);
        var partial = (a?.Partial ?? false) || (b?.Partial ?? false);
        return new GroundedMeta(citations, partial);
    }

    public static GroundedMeta Mix(GroundedMeta? grounded, string ungroundedText)
    {
        var spans = new List<GroundedSpan>();
        if (!string.IsNullOrEmpty(ungroundedText))
            spans.Add(new GroundedSpan(0, ungroundedText.Length, ungroundedText));
        return new GroundedMeta(grounded?.Citations, partial: true, spans);
    }

    public RuntimeValue ToRuntimeValue()
    {
        var obj = new JsonObject();
        obj.Set("citations", RuntimeValue.Array(Citations));
        obj.Set("sourced", RuntimeValue.Boolean(Citations.Count > 0 && !Partial));
        obj.Set("partial", RuntimeValue.Boolean(Partial));
        return RuntimeValue.Object(obj);
    }
}

public sealed record GroundedSpan(int Start, int Length, string Text);
