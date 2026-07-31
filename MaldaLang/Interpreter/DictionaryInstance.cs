// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public class DictionaryInstance : ObjectInstance
{
    private readonly Dictionary<string, RuntimeValue> _entries;
    
    public DictionaryInstance()
        : this(new Dictionary<string, RuntimeValue>())
    {
    }
    
    public DictionaryInstance(Dictionary<string, RuntimeValue> entries)
        : base(DictionaryClassDefinition.Instance)
    {
        _entries = entries;
    }
    
    public IReadOnlyDictionary<string, RuntimeValue> Entries => _entries;
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // First check if it's a dictionary entry (allows property access on dictionaries)
        if (_entries.TryGetValue(name, out var entryValue))
        {
            return entryValue;
        }
        
        // Built-in dictionary methods are provided via the built-in method dispatch pipeline.
        if (name is "get" or "set" or "remove" or "containsKey" or "keys" or "values" or "entries")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            };
            return RuntimeValue.Function(wrapper);
        }
        
        // Preserve object-like semantics for optional keys while still honoring
        // dictionary methods and any declared class members.
        try
        {
            return base.Get(name, accessingClass);
        }
        catch (RuntimeException)
        {
            return RuntimeValue.Null();
        }
    }
    
    public bool TryGetEntry(string key, out RuntimeValue value) => _entries.TryGetValue(key, out value);
    
    public void SetEntry(string key, RuntimeValue value) => _entries[key] = value;

    public override bool TryGet(string name, out RuntimeValue? value, ClassDefinition? accessingClass = null)
    {
        if (_entries.TryGetValue(name, out var entryValue))
        {
            value = entryValue;
            return true;
        }

        if (name is "get" or "set" or "remove" or "containsKey" or "keys" or "values" or "entries")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            };
            value = RuntimeValue.Function(wrapper);
            return true;
        }

        return base.TryGet(name, out value, accessingClass);
    }

    public override void Set(string name, RuntimeValue value)
    {
        _entries[name] = value;
    }
    
    public bool RemoveEntry(string key) => _entries.Remove(key);
    
    public bool ContainsEntry(string key) => _entries.ContainsKey(key);
    
    public IEnumerable<string> GetKeys() => _entries.Keys;
    
    public IEnumerable<RuntimeValue> GetValues() => _entries.Values;
    
    public IEnumerable<KeyValuePair<string, RuntimeValue>> GetEntries() => _entries;

    public override IEnumerable<string> GetAllKeys() => _entries.Keys;
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> arguments, Interpreter interpreter)
    {
        switch (methodName)
        {
            case "get":
                return CallGet(arguments);
            case "set":
                return CallSet(arguments);
            case "remove":
                return CallRemove(arguments);
            case "containsKey":
                return CallContainsKey(arguments);
            case "keys":
                return CallKeys(arguments);
            case "values":
                return CallValues(arguments);
            case "entries":
                return CallEntries(arguments);
            default:
                throw new RuntimeException($"Dictionary has no method '{methodName}'.");
        }
    }
    
    private RuntimeValue CallGet(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("get() expects 1 argument");
        
        var keyValue = arguments[0];
        if (keyValue.Type != ValueType.String)
            throw new RuntimeException("Dictionary keys must be strings.");
        
        var key = keyValue.AsString();
        return _entries.TryGetValue(key, out var value) ? value : RuntimeValue.Null();
    }
    
    private RuntimeValue CallSet(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 2)
            throw new RuntimeException("set() expects 2 arguments");
        
        var keyValue = arguments[0];
        if (keyValue.Type != ValueType.String)
            throw new RuntimeException("Dictionary keys must be strings.");
        
        var key = keyValue.AsString();
        var value = arguments[1];
        _entries[key] = value;
        return RuntimeValue.Object(this);
    }
    
    private RuntimeValue CallRemove(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("remove() expects 1 argument");
        
        var keyValue = arguments[0];
        if (keyValue.Type != ValueType.String)
            throw new RuntimeException("Dictionary keys must be strings.");
        
        var key = keyValue.AsString();
        var removed = _entries.Remove(key);
        return RuntimeValue.Boolean(removed);
    }
    
    private RuntimeValue CallContainsKey(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("containsKey() expects 1 argument");
        
        var keyValue = arguments[0];
        if (keyValue.Type != ValueType.String)
            throw new RuntimeException("Dictionary keys must be strings.");
        
        var key = keyValue.AsString();
        return RuntimeValue.Boolean(_entries.ContainsKey(key));
    }
    
    private RuntimeValue CallKeys(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("keys() expects 0 arguments");
        
        var values = _entries.Keys.Select(RuntimeValue.String).ToList();
        return RuntimeValue.Array(values);
    }
    
    private RuntimeValue CallValues(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("values() expects 0 arguments");
        
        var values = _entries.Values.ToList();
        return RuntimeValue.Array(values);
    }
    
    private RuntimeValue CallEntries(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("entries() expects 0 arguments");
        
        var result = new List<RuntimeValue>();
        foreach (var kvp in _entries)
        {
            var pairArray = new List<RuntimeValue>
            {
                RuntimeValue.String(kvp.Key),
                kvp.Value
            };
            result.Add(RuntimeValue.Array(pairArray));
        }
        return RuntimeValue.Array(result);
    }
}

