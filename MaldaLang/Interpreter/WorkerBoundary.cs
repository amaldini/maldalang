// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.BuiltIns;

/// <summary>
/// What crosses a <c>@worker</c> actor boundary (spawn arguments, message arguments, replies),
/// with the rules of the browser's structured clone so every backend behaves like a Web Worker:
/// null, bools, numbers and strings as they are; arrays, dicts, object literals and variants as
/// deep copies (shared and cyclic parts stay shared within one message); buffers copied, or moved
/// after <c>buf.transfer</c>. Anything else is an error at send time.
/// </summary>
public static class WorkerBoundary
{
    /// <summary>The error for a value that cannot cross, one wording on every backend.</summary>
    public static string CannotCrossMessage(string what, string actorName) =>
        $"{what} cannot cross the boundary of worker actor '{actorName}': only null, bools, numbers, strings, arrays, dicts, variants and buffers can.";

    /// <summary>What a worker actor writes to stderr when a message fails; it keeps running.</summary>
    public static string ErrorLine(string actorName, string message) => $"Worker actor '{actorName}' error: {message}";

    public const string Function = "A function";
    public const string ActorReference = "An actor reference";
    public const string ObjectWithMethods = "A class instance or host object";

    public static List<RuntimeValue> Cross(IEnumerable<RuntimeValue> values, string actorName)
    {
        var copies = new Dictionary<object, RuntimeValue>(ReferenceEqualityComparer.Instance);
        return values.Select(value => Cross(value, actorName, copies)).ToList();
    }

    public static RuntimeValue Cross(RuntimeValue value, string actorName) =>
        Cross(value, actorName, new Dictionary<object, RuntimeValue>(ReferenceEqualityComparer.Instance));

    private static RuntimeValue Cross(RuntimeValue value, string actorName, Dictionary<object, RuntimeValue> copies)
    {
        switch (value.Type)
        {
            case ValueType.Null:
            case ValueType.Boolean:
            case ValueType.Integer:
            case ValueType.Float:
            case ValueType.String:
                return value;
            case ValueType.Function:
            case ValueType.Prompt:
            case ValueType.Class:
            case ValueType.Task:
                throw new RuntimeException(CannotCrossMessage(Function, actorName));
            case ValueType.ActorReference:
            case ValueType.Actor:
                throw new RuntimeException(CannotCrossMessage(ActorReference, actorName));
        }

        var identity = value.Value!;
        if (copies.TryGetValue(identity, out var existing))
            return existing;

        switch (value.Type)
        {
            case ValueType.Array:
            {
                var items = new List<RuntimeValue>();
                var copy = RuntimeValue.Array(items);
                copies[identity] = copy;
                foreach (var item in value.AsArray())
                    items.Add(Cross(item, actorName, copies));
                return copy;
            }
            case ValueType.Variant:
            {
                var variant = value.AsVariant();
                var payload = new List<RuntimeValue>();
                var copy = RuntimeValue.Variant(variant.Tag, payload);
                copies[identity] = copy;
                foreach (var item in variant.Payload)
                    payload.Add(Cross(item, actorName, copies));
                return copy;
            }
        }

        switch (value.AsObject())
        {
            case ArrayInstance array:
            {
                var items = new List<RuntimeValue>();
                var copy = RuntimeValue.Array(items);
                copies[identity] = copy;
                foreach (var item in array.Elements)
                    items.Add(Cross(item, actorName, copies));
                return copy;
            }
            case TypedBufferInstance buffer:
            {
                var copy = RuntimeValue.Object(buffer.CrossWorkerBoundary());
                copies[identity] = copy;
                return copy;
            }
            case DictionaryInstance dict:
            {
                var entries = new Dictionary<string, RuntimeValue>();
                var copy = RuntimeValue.Object(new DictionaryInstance(entries));
                copies[identity] = copy;
                foreach (var (key, item) in dict.Entries.ToList())
                    entries[key] = Cross(item, actorName, copies);
                return copy;
            }
            case JsonObject literal:
            {
                var target = new JsonObject();
                var copy = RuntimeValue.Object(target);
                copies[identity] = copy;
                foreach (var (key, item) in literal.GetProperties().ToList())
                    target.Set(key, Cross(item, actorName, copies));
                return copy;
            }
            default:
                throw new RuntimeException(CannotCrossMessage(ObjectWithMethods, actorName));
        }
    }
}
