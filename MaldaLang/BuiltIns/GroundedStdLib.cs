// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// L5 grounded values: wrap a payload with citation provenance. Not a match-visible
/// value kind — that is v2 if authors need <c>match</c> / <c>validate</c> to see it.
/// </summary>
public static class GroundedStdLib
{
    public const string DefaultSource = "graph-memory";

    public static RuntimeValue Wrap(List<RuntimeValue> args)
    {
        BuiltInArity.Require("wrap", args, 1, 2, "value, citations?");
        var citations = args.Count >= 2 ? args[1] : RuntimeValue.Array(new List<RuntimeValue>());
        return Wrap(args[0], citations);
    }

    public static RuntimeValue Wrap(RuntimeValue value, RuntimeValue citations)
    {
        var normalized = NormalizeCitations(citations);
        var obj = new JsonObject();
        obj.Set("value", value);
        obj.Set("citations", RuntimeValue.Array(normalized));
        obj.Set("sourced", RuntimeValue.Boolean(normalized.Count > 0));
        return RuntimeValue.Object(obj);
    }

    /// <summary>
    /// Build a grounded wrapper from GraphMemory query hits. <c>value</c> stays the hit
    /// array so callers can still iterate nodes; <c>citations</c> is <c>{ source, id?, span? }</c>.
    /// </summary>
    public static RuntimeValue WrapMemoryHits(RuntimeValue hits)
    {
        if (hits.Type != ValueType.Array)
            return Wrap(hits, RuntimeValue.Array(new List<RuntimeValue>()));

        var citations = new List<RuntimeValue>();
        foreach (var hit in hits.AsArray())
        {
            var citation = CitationFromMemoryNode(hit);
            if (citation != null)
                citations.Add(citation);
        }

        return Wrap(hits, RuntimeValue.Array(citations));
    }

    public static bool IsGroundedValue(RuntimeValue value)
    {
        if (value.Type != ValueType.Object || value.AsObject() is not JsonObject obj)
            return false;
        var citations = obj.Get("citations", null);
        var sourced = obj.Get("sourced", null);
        return obj.Get("value", null) != null
            && citations != null
            && sourced != null
            && citations.Type == ValueType.Array
            && sourced.Type == ValueType.Boolean;
    }

    public static RuntimeValue EnsureMemoryHitsWrapped(RuntimeValue result) =>
        IsGroundedValue(result) ? result : WrapMemoryHits(result);

    internal static List<RuntimeValue> NormalizeCitations(RuntimeValue citations)
    {
        var result = new List<RuntimeValue>();
        if (citations.Type == ValueType.Null)
            return result;

        if (citations.Type == ValueType.Array)
        {
            foreach (var item in citations.AsArray())
            {
                var citation = NormalizeCitation(item);
                if (citation != null)
                    result.Add(citation);
            }
            return result;
        }

        var single = NormalizeCitation(citations);
        if (single != null)
            result.Add(single);
        return result;
    }

    internal static RuntimeValue? NormalizeCitation(RuntimeValue item)
    {
        if (item.Type == ValueType.String)
        {
            var source = item.AsString();
            if (string.IsNullOrWhiteSpace(source))
                return null;
            var obj = new JsonObject();
            obj.Set("source", RuntimeValue.String(source.Trim()));
            return RuntimeValue.Object(obj);
        }

        if (item.Type != ValueType.Object)
            return null;

        var raw = item.AsObject();
        JsonObject? src = raw as JsonObject;
        if (src == null && raw is DictionaryInstance dict)
        {
            src = new JsonObject();
            foreach (var kvp in dict.GetEntries())
                src.Set(kvp.Key, kvp.Value);
        }

        if (src == null)
            return null;

        var sourceText = FirstNonEmptyString(src, "source", "filePath") ?? "";
        if (string.IsNullOrWhiteSpace(sourceText))
            sourceText = DefaultSource;

        var citation = new JsonObject();
        citation.Set("source", RuntimeValue.String(sourceText));
        var id = FirstNonEmptyString(src, "id", "nodeId");
        if (id != null)
            citation.Set("id", RuntimeValue.String(id));
        CopySpan(src, citation);
        return RuntimeValue.Object(citation);
    }

    internal static RuntimeValue? CitationFromMemoryNode(RuntimeValue node)
    {
        if (node.Type != ValueType.Object)
            return null;

        var raw = node.AsObject();
        JsonObject? obj = raw as JsonObject;
        if (obj == null && raw is DictionaryInstance dict)
        {
            obj = new JsonObject();
            foreach (var kvp in dict.GetEntries())
                obj.Set(kvp.Key, kvp.Value);
        }

        if (obj == null)
            return null;

        var source = FirstNonEmptyString(obj, "filePath")
            ?? FirstNonEmptyString(obj, "source")
            ?? DefaultSource;
        var citation = new JsonObject();
        citation.Set("source", RuntimeValue.String(source));
        var id = FirstNonEmptyString(obj, "nodeId", "id");
        if (id != null)
            citation.Set("id", RuntimeValue.String(id));
        CopySpan(obj, citation);
        return RuntimeValue.Object(citation);
    }

    private static void CopySpan(JsonObject src, JsonObject citation)
    {
        var span = src.Get("span", null);
        if (span != null && span.Type != ValueType.Null)
        {
            citation.Set("span", span);
            return;
        }

        var context = src.Get("context", null);
        if (context != null && context.Type == ValueType.String)
        {
            var text = context.AsString();
            var marker = text.IndexOf("#chunk-", StringComparison.Ordinal);
            if (marker >= 0)
                citation.Set("span", RuntimeValue.String(text[marker..]));
        }
    }

    private static string? FirstNonEmptyString(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            var val = obj.Get(key, null);
            if (val != null && val.Type == ValueType.String)
            {
                var text = val.AsString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }
        }

        return null;
    }
}
