// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Runtime.UI;

public sealed class UiFrameworkInstance : ObjectInstance
{
    private static readonly HashSet<string> Methods = new(StringComparer.Ordinal)
    {
        "row", "column", "stack", "spacer", "panel",
        "text", "heading", "image", "icon",
        "button", "textField", "checkbox", "select", "slider", "datePicker",
        "list", "table",
        "alert", "progress", "modal",
        "form", "field", "textArea", "radioGroup", "switch",
        "tabs", "accordion", "breadcrumbs", "drawer",
        "dataGrid", "treeView", "paginator", "emptyState", "badge",
        "toast", "skeleton", "spinner", "errorBoundary",
        "slot", "withSlot", "when", "choose", "each",
        "template", "partial", "layout", "renderList", "crudModel", "crudControls", "crudSchema",
        "mount", "mountEnvelope", "render", "dispatchEvent", "pullEvent",
        "state", "getState", "setState", "pinState", "unpinState", "invalidate",
        "onInit", "onPreRender", "onLoad", "onDispose",
        "onMount", "onUpdate", "onUnmount", "onError",
        "configure", "snapshot", "resync", "sessionId", "redirectWithSession", "generate"
    };

    public UiFrameworkInstance() : base(null)
    {
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (!Methods.Contains(name))
        {
            throw new Exception($"Undefined property '{name}' on ui.");
        }

        var wrapper = new FunctionValue(null, null, false, null)
        {
            BuiltInInstance = this,
            BuiltInMethod = name
        };
        return RuntimeValue.Function(wrapper);
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        var builtInName = "ui" + char.ToUpperInvariant(methodName[0]) + methodName[1..];
        return BuiltInFunctions.CallBuiltIn(builtInName, args, null);
    }

    public async Task<RuntimeValue> CallMethodAsync(string methodName, List<RuntimeValue> args, Interpreter? interpreter)
    {
        var builtInName = "ui" + char.ToUpperInvariant(methodName[0]) + methodName[1..];
        return await BuiltInFunctions.CallBuiltInAsync(builtInName, args, interpreter);
    }

    public static RuntimeValue BuildNode(string type, List<RuntimeValue> args)
    {
        var props = new JsonObject();
        if (args.Count > 0 && args[0].Type == ValueType.Object)
        {
            if (args[0].AsObject() is JsonObject propsObj)
            {
                props = propsObj;
            }
            else if (args[0].AsObject() is DictionaryInstance dict)
            {
                foreach (var kvp in dict.GetEntries())
                {
                    props.Set(kvp.Key, kvp.Value);
                }
            }
        }
        var children = new List<RuntimeValue>();

        if (args.Count > 1 && args[1].Type == ValueType.Array)
        {
            foreach (var child in args[1].AsArray())
            {
                children.Add(CoerceChild(child));
            }
        }
        else
        {
            for (var i = 1; i < args.Count; i++)
            {
                children.Add(CoerceChild(args[i]));
            }
        }

        var node = new JsonObject();
        node.Set("type", RuntimeValue.String(type));
        node.Set("props", RuntimeValue.Object(props));
        node.Set("children", RuntimeValue.Array(children));

        var keyValue = props.Get("key", null);
        if (keyValue.Type == ValueType.String)
        {
            node.Set("key", keyValue);
        }

        return RuntimeValue.Object(node);
    }

    private static RuntimeValue CoerceChild(RuntimeValue child)
    {
        if (child.Type == ValueType.Object)
        {
            return child;
        }

        var node = new JsonObject();
        node.Set("type", RuntimeValue.String("text"));
        var props = new JsonObject();
        props.Set("value", RuntimeValue.String(child.Type == ValueType.String ? child.AsString() : child.ToString()));
        node.Set("props", RuntimeValue.Object(props));
        node.Set("children", RuntimeValue.Array(new List<RuntimeValue>()));
        return RuntimeValue.Object(node);
    }
}
