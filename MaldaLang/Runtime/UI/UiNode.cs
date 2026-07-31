// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.UI;

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

public sealed class UiNode
{
    public string Type { get; }
    public string? Key { get; }
    public Dictionary<string, RuntimeValue> Props { get; }
    public List<UiNode> Children { get; }

    public UiNode(string type, Dictionary<string, RuntimeValue>? props = null, List<UiNode>? children = null, string? key = null)
    {
        Type = type;
        Key = key;
        Props = props ?? new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
        Children = children ?? new List<UiNode>();
    }

    public static UiNode FromRuntimeValue(RuntimeValue value)
    {
        if (value.Type != ValueType.Object || value.AsObject() is not JsonObject obj)
        {
            throw new Exception("UI node must be an object.");
        }

        var type = obj.Get("type", null);
        if (type.Type != ValueType.String)
        {
            throw new Exception("UI node object must include string property 'type'.");
        }

        var keyValue = obj.Get("key", null);
        string? key = keyValue.Type == ValueType.String ? keyValue.AsString() : null;

        var props = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
        var propsValue = obj.Get("props", null);
        if (propsValue.Type == ValueType.Object && propsValue.AsObject() is JsonObject propsObject)
        {
            foreach (var kvp in propsObject.GetProperties())
            {
                var propName = kvp.Key;
                if (IsUnsafePropName(propName))
                {
                    continue;
                }

                if (kvp.Value.Type == ValueType.String && IsUnsafePropValue(propName, kvp.Value.AsString()))
                {
                    continue;
                }

                props[propName] = kvp.Value;
            }
        }

        var children = new List<UiNode>();
        var childrenValue = obj.Get("children", null);
        if (childrenValue.Type == ValueType.Array)
        {
            foreach (var child in childrenValue.AsArray())
            {
                children.Add(FromRuntimeValue(child));
            }
        }

        var node = new UiNode(type.AsString(), props, children, key);
        UiControlSpecRegistry.Validate(node);
        return node;
    }

    public RuntimeValue ToRuntimeValue()
    {
        var nodeObj = new JsonObject();
        nodeObj.Set("type", RuntimeValue.String(Type));

        if (!string.IsNullOrWhiteSpace(Key))
        {
            nodeObj.Set("key", RuntimeValue.String(Key));
        }

        var propsObj = new JsonObject();
        foreach (var kvp in Props)
        {
            propsObj.Set(kvp.Key, kvp.Value);
        }
        nodeObj.Set("props", RuntimeValue.Object(propsObj));

        var childrenList = new List<RuntimeValue>(Children.Count);
        foreach (var child in Children)
        {
            childrenList.Add(child.ToRuntimeValue());
        }
        nodeObj.Set("children", RuntimeValue.Array(childrenList));

        return RuntimeValue.Object(nodeObj);
    }

    private static bool IsUnsafePropName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        if (!name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name != "onClick" &&
               name != "onChange" &&
               name != "onInput" &&
               name != "onSubmit" &&
               name != "onClose" &&
               name != "onFocus" &&
               name != "onBlur" &&
               name != "onRowClick" &&
               name != "onSelectionChange" &&
               name != "onSort" &&
               name != "onFilter" &&
               name != "onPageChange" &&
               name != "onViewportChange" &&
               name != "onNodeSelect" &&
               name != "onNodeToggle" &&
               name != "onNodeExpand" &&
               name != "onNodeCollapse" &&
               name != "onNodeActivate" &&
               name != "onLoadChildren" &&
               name != "onDragStart" &&
               name != "onDragOver" &&
               name != "onDrop" &&
               name != "onDragEnd";
    }

    private static bool IsUnsafePropValue(string propName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (propName.Equals("href", StringComparison.OrdinalIgnoreCase) || propName.Equals("src", StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = value.Trim();
            if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return value.Contains("<script", StringComparison.OrdinalIgnoreCase);
    }
}
