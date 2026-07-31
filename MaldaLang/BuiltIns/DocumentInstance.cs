// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

public sealed class DocumentInstance : ObjectInstance
{
    private readonly Dictionary<string, RuntimeValue> _metadata;

    public string Content { get; }

    public IEnumerable<KeyValuePair<string, RuntimeValue>> MetadataEntries => _metadata;

    public DocumentInstance(string content, Dictionary<string, RuntimeValue>? metadata = null)
        : base(null)
    {
        Content = content;
        _metadata = metadata ?? new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
    }

    public string? GetMetadataString(string key)
    {
        if (!_metadata.TryGetValue(key, out var value) || value.Type != ValueType.String)
            return null;
        return value.AsString();
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        return name switch
        {
            "content" => RuntimeValue.String(Content),
            "metadata" => RuntimeValue.Object(new DictionaryInstance(_metadata.ToDictionary(
                static e => e.Key,
                static e => e.Value))),
            _ => throw new RuntimeException($"Undefined property '{name}' on Document.")
        };
    }

    public override string ToString()
    {
        var preview = Content.Length <= 40 ? Content : Content.Substring(0, 40) + "...";
        return $"<document \"{preview}\">";
    }
}
