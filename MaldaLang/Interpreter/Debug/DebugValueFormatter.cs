// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter.Debug;

using System.Collections.Generic;
using MaldaLang.BuiltIns;

/// <summary>
/// RuntimeValue → inspect preview / type / lazy children.
/// Arrays, dicts, objects, variants, and capability tokens expand on demand;
/// tasks, prompts, functions, classes, and actors stay leaves.
/// </summary>
public static class DebugValueFormatter
{
    private const int ShortArrayLimit = 8;

    public static string FormatPreview(RuntimeValue value)
    {
        switch (value.Type)
        {
            case ValueType.Array:
            {
                var items = value.AsArray();
                if (items.Count == 0)
                    return "[]";
                if (items.Count <= ShortArrayLimit)
                    return value.ToString();
                return $"[{items.Count} items]";
            }
            case ValueType.Object when value.Value is DictionaryInstance dict:
                return $"{{{dict.Entries.Count} keys}}";
            case ValueType.Object when value.Value is CapabilityToken cap:
                return cap.ToString();
            case ValueType.Prompt:
                return value.Value is PromptValue prompt ? prompt.ToString() : "<prompt>";
            case ValueType.Task:
                return "<task>";
            case ValueType.Actor:
                return value.Value is ActorDefinition actor ? $"<actor {actor.Name}>" : "<actor>";
            case ValueType.ActorReference:
                return value.Value?.ToString() ?? "<actor>";
            default:
                return value.ToString();
        }
    }

    public static string FormatType(RuntimeValue value)
    {
        if (value.Type == ValueType.Object && value.Value is DictionaryInstance)
            return "dict";
        if (value.Type == ValueType.Object && value.Value is CapabilityToken)
            return "cap";

        return value.Type switch
        {
            ValueType.Integer => "integer",
            ValueType.Float => "float",
            ValueType.String => "string",
            ValueType.Boolean => "boolean",
            ValueType.Null => "null",
            ValueType.Array => "array",
            ValueType.Object => "object",
            ValueType.Function => "function",
            ValueType.Prompt => "prompt",
            ValueType.Class => "class",
            ValueType.Actor => "actor",
            ValueType.ActorReference => "actor",
            ValueType.Variant => "variant",
            ValueType.Task => "task",
            _ => value.Type.ToString().ToLowerInvariant()
        };
    }

    public static bool HasChildren(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Array => value.AsArray().Count > 0,
            ValueType.Variant => value.AsVariant().Payload.Count > 0,
            ValueType.Object when value.Value is CapabilityToken => true,
            ValueType.Object when value.Value is DictionaryInstance dict => dict.Entries.Count > 0,
            ValueType.Object when value.Value is ObjectInstance obj => HasObjectFields(obj),
            ValueType.Task => false,
            ValueType.Prompt => false,
            ValueType.Function => false,
            ValueType.Class => false,
            ValueType.Actor => false,
            ValueType.ActorReference => false,
            _ => false
        };
    }

    public static IReadOnlyList<(string Name, RuntimeValue Value)> GetChildren(RuntimeValue value)
    {
        switch (value.Type)
        {
            case ValueType.Array:
            {
                var items = value.AsArray();
                var children = new List<(string, RuntimeValue)>(items.Count);
                for (var i = 0; i < items.Count; i++)
                    children.Add(($"[{i}]", items[i]));
                return children;
            }
            case ValueType.Variant:
            {
                var variant = value.AsVariant();
                var children = new List<(string, RuntimeValue)>(variant.Payload.Count);
                for (var i = 0; i < variant.Payload.Count; i++)
                    children.Add(($"[{i}]", variant.Payload[i]));
                return children;
            }
            case ValueType.Object when value.Value is CapabilityToken cap:
                return new List<(string, RuntimeValue)>
                {
                    ("kind", RuntimeValue.String(cap.Kind)),
                    ("path", RuntimeValue.String(cap.Path))
                };
            case ValueType.Object when value.Value is DictionaryInstance dict:
            {
                var children = new List<(string, RuntimeValue)>(dict.Entries.Count);
                foreach (var entry in dict.Entries)
                    children.Add((entry.Key, entry.Value));
                return children;
            }
            case ValueType.Object when value.Value is ObjectInstance obj:
            {
                var children = new List<(string, RuntimeValue)>();
                foreach (var key in obj.GetAllKeys())
                {
                    if (obj.TryGet(key, out var field, obj.Class) && field != null)
                        children.Add((key, field));
                }
                return children;
            }
            default:
                return Array.Empty<(string, RuntimeValue)>();
        }
    }

    private static bool HasObjectFields(ObjectInstance obj)
    {
        foreach (var _ in obj.GetAllKeys())
            return true;
        return false;
    }
}
