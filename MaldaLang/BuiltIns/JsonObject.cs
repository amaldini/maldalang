// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Linq;
using MaldaLang.Interpreter;

public class JsonObject : ObjectInstance
{
    private readonly Dictionary<string, RuntimeValue> _properties = new();

    private static readonly string[] BuiltInMethodNames =
    [
        "get",
        "set",
        "remove",
        "containsKey",
        "keys",
        "values",
        "entries"
    ];
    
    public JsonObject() : base(null)
    {
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (_properties.ContainsKey(name))
            return _properties[name];
        if (BuiltInMethodNames.Contains(name))
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            };
            return RuntimeValue.Function(wrapper);
        }
        return RuntimeValue.Null();
    }

    public override bool TryGet(string name, out RuntimeValue? value, ClassDefinition? accessingClass = null)
    {
        if (_properties.TryGetValue(name, out var propertyValue))
        {
            value = propertyValue;
            return true;
        }

        if (BuiltInMethodNames.Contains(name))
        {
            value = RuntimeValue.Function(new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            });
            return true;
        }

        value = null;
        return false;
    }
    
    public override void Set(string name, RuntimeValue value)
    {
        _properties[name] = value;
    }
    
    public Dictionary<string, RuntimeValue> GetProperties()
    {
        return _properties;
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> arguments, Interpreter interpreter)
    {
        return methodName switch
        {
            "get" => CallGet(arguments),
            "set" => CallSet(arguments),
            "remove" => CallRemove(arguments),
            "containsKey" => CallContainsKey(arguments),
            "keys" => CallKeys(arguments),
            "values" => CallValues(arguments),
            "entries" => CallEntries(arguments),
            _ => throw new RuntimeException($"JsonObject has no method '{methodName}'.")
        };
    }
    
    public override IEnumerable<string> GetAllKeys()
    {
        return _properties.Keys;
    }

    private RuntimeValue CallGet(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("get() expects 1 argument");

        if (arguments[0].Type != ValueType.String)
            throw new RuntimeException("JsonObject keys must be strings.");

        var key = arguments[0].AsString();
        return _properties.TryGetValue(key, out var value) ? value : RuntimeValue.Null();
    }

    private RuntimeValue CallSet(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 2)
            throw new RuntimeException("set() expects 2 arguments");

        if (arguments[0].Type != ValueType.String)
            throw new RuntimeException("JsonObject keys must be strings.");

        _properties[arguments[0].AsString()] = arguments[1];
        return RuntimeValue.Object(this);
    }

    private RuntimeValue CallRemove(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("remove() expects 1 argument");

        if (arguments[0].Type != ValueType.String)
            throw new RuntimeException("JsonObject keys must be strings.");

        return RuntimeValue.Boolean(_properties.Remove(arguments[0].AsString()));
    }

    private RuntimeValue CallContainsKey(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("containsKey() expects 1 argument");

        if (arguments[0].Type != ValueType.String)
            throw new RuntimeException("JsonObject keys must be strings.");

        return RuntimeValue.Boolean(_properties.ContainsKey(arguments[0].AsString()));
    }

    private RuntimeValue CallKeys(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("keys() expects 0 arguments");

        return RuntimeValue.Array(_properties.Keys.Select(RuntimeValue.String).ToList());
    }

    private RuntimeValue CallValues(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("values() expects 0 arguments");

        return RuntimeValue.Array(_properties.Values.ToList());
    }

    private RuntimeValue CallEntries(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("entries() expects 0 arguments");

        var result = new List<RuntimeValue>();
        foreach (var kvp in _properties)
        {
            result.Add(RuntimeValue.Array(new List<RuntimeValue>
            {
                RuntimeValue.String(kvp.Key),
                kvp.Value
            }));
        }

        return RuntimeValue.Array(result);
    }
    
    public override string ToString()
    {
        return "<json object>";
    }
}