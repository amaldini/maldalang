// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Text;
using System.Text.Json;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// <c>stream(value)</c> — yields <c>{ text }</c> chunks or schema-aware partial objects.
/// </summary>
public static class StreamStdLib
{
    public static RuntimeValue Stream(List<RuntimeValue> args)
    {
        BuiltInArity.Require("stream", args, 1, 2, "value, schema?");
        var value = args[0];
        string? schemaName = null;
        if (args.Count > 1 && args[1].Type == ValueType.String)
            schemaName = args[1].AsString();

        var text = ExtractText(value);
        if (!string.IsNullOrEmpty(schemaName))
            return RuntimeValue.Array(IncrementalJson.ParsePrefixes(text, schemaName));

        return RuntimeValue.Array(TextChunks(text));
    }

    private static string ExtractText(RuntimeValue value)
    {
        if (value.Type == ValueType.String)
            return value.AsString();
        if (value.Type == ValueType.Object && value.AsObject() is JsonObject obj)
        {
            var content = obj.Get("content");
            if (content.Type == ValueType.String)
                return content.AsString();
            var text = obj.Get("text");
            if (text.Type == ValueType.String)
                return text.AsString();
        }

        return value.ToString();
    }

    private static List<RuntimeValue> TextChunks(string text)
    {
        var chunks = new List<RuntimeValue>();
        if (string.IsNullOrEmpty(text))
        {
            chunks.Add(Chunk(""));
            return chunks;
        }

        var acc = new StringBuilder();
        foreach (var ch in text)
        {
            acc.Append(ch);
            if (char.IsWhiteSpace(ch) || acc.Length % 24 == 0)
                chunks.Add(Chunk(acc.ToString()));
        }

        if (chunks.Count == 0 || chunks[^1].AsObject() is JsonObject last
            && last.Get("text").AsString() != text)
            chunks.Add(Chunk(text));
        return chunks;
    }

    internal static RuntimeValue Chunk(string text)
    {
        var obj = new JsonObject();
        obj.Set("text", RuntimeValue.String(text));
        return RuntimeValue.Object(obj);
    }
}

/// <summary>
/// Largest-valid-prefix incremental JSON parser driven by a registered schema name.
/// </summary>
public static class IncrementalJson
{
    public static List<RuntimeValue> ParsePrefixes(string text, string schemaName)
    {
        var results = new List<RuntimeValue>();
        if (string.IsNullOrEmpty(text))
            return results;

        for (var i = 1; i <= text.Length; i++)
        {
            var prefix = text[..i];
            if (!TryParsePartial(prefix, out var element))
                continue;

            var coerced = CassetteJsonFromElement(element);
            if (!SchemaRegistry.TryResolve(schemaName, out var schema))
            {
                results.Add(coerced);
                continue;
            }

            if (TypedPromptValidator.TryValidateReturnType(coerced, schema, out _))
                results.Add(coerced);
        }

        if (results.Count == 0)
            results.Add(StreamStdLib.Chunk(text));
        return results;
    }

    private static bool TryParsePartial(string text, out JsonElement element)
    {
        element = default;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return false;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            element = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            var closed = ClosePartialJson(trimmed);
            if (closed == null)
                return false;
            try
            {
                using var doc = JsonDocument.Parse(closed);
                element = doc.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    private static string? ClosePartialJson(string text)
    {
        var stack = new Stack<char>();
        var inString = false;
        var escape = false;
        foreach (var ch in text)
        {
            if (inString)
            {
                if (escape)
                    escape = false;
                else if (ch == '\\')
                    escape = true;
                else if (ch == '"')
                    inString = false;
                continue;
            }

            if (ch == '"')
                inString = true;
            else if (ch is '{' or '[')
                stack.Push(ch);
            else if (ch == '}' && stack.Count > 0 && stack.Peek() == '{')
                stack.Pop();
            else if (ch == ']' && stack.Count > 0 && stack.Peek() == '[')
                stack.Pop();
        }

        if (inString)
            text += "\"";
        var sb = new StringBuilder(text);
        while (stack.Count > 0)
        {
            var open = stack.Pop();
            sb.Append(open == '{' ? '}' : ']');
        }

        return sb.ToString();
    }

    private static RuntimeValue CassetteJsonFromElement(JsonElement element) =>
        MaldaLang.Runtime.LlmCassettes.CassetteJson.FromElement(element);
}
