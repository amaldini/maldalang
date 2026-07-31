// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.UI;

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

public enum UiPatchOperation
{
    ReplaceNode,
    SetProp,
    RemoveProp,
    InsertChild,
    RemoveChild
}

public sealed class UiPatch
{
    public UiPatchOperation Operation { get; }
    public string Path { get; }
    public string? PropertyName { get; }
    public RuntimeValue? Value { get; }
    public int? ChildIndex { get; }

    public UiPatch(UiPatchOperation operation, string path, string? propertyName = null, RuntimeValue? value = null, int? childIndex = null)
    {
        Operation = operation;
        Path = path;
        PropertyName = propertyName;
        Value = value;
        ChildIndex = childIndex;
    }

    public RuntimeValue ToRuntimeValue()
    {
        var obj = new JsonObject();
        obj.Set("op", RuntimeValue.String(Operation.ToString()));
        obj.Set("path", RuntimeValue.String(Path));

        if (!string.IsNullOrWhiteSpace(PropertyName))
        {
            obj.Set("prop", RuntimeValue.String(PropertyName));
        }

        if (Value != null)
        {
            obj.Set("value", Value);
        }

        if (ChildIndex.HasValue)
        {
            obj.Set("index", RuntimeValue.Integer(ChildIndex.Value));
        }

        return RuntimeValue.Object(obj);
    }
}
