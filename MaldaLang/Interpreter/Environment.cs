// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public class Environment
{
    private readonly Dictionary<string, RuntimeValue> _values = new();
    private readonly HashSet<string> _constNames = new(StringComparer.Ordinal);
    private readonly Environment? _enclosing;
    
    public Environment(Environment? enclosing = null)
    {
        _enclosing = enclosing;
    }
    
    public void Define(string name, RuntimeValue value, bool isConst = false)
    {
        _values[name] = value;
        if (isConst)
            _constNames.Add(name);
    }

    public bool IsConst(string name)
    {
        if (_values.ContainsKey(name))
            return _constNames.Contains(name);

        return _enclosing?.IsConst(name) ?? false;
    }
    
    public RuntimeValue Get(string name)
    {
        if (_values.ContainsKey(name))
        {
            return _values[name];
        }
        
        if (_enclosing != null)
        {
            return _enclosing.Get(name);
        }
        
        throw new RuntimeException($"Undefined variable '{name}'.");
    }
    
    public void Assign(string name, RuntimeValue value)
    {
        if (_values.ContainsKey(name))
        {
            if (_constNames.Contains(name))
                throw new RuntimeException($"Cannot assign to const '{name}'.");
            _values[name] = value;
            return;
        }
        
        if (_enclosing != null)
        {
            _enclosing.Assign(name, value);
            return;
        }
        
        throw new RuntimeException($"Undefined variable '{name}'.");
    }
    
    public Environment? GetEnclosing()
    {
        return _enclosing;
    }
    
    public bool Contains(string name)
    {
        if (_values.ContainsKey(name))
        {
            return true;
        }
        
        if (_enclosing != null)
        {
            return _enclosing.Contains(name);
        }
        
        return false;
    }
    
    public bool TryGet(string name, out RuntimeValue value)
    {
        if (_values.ContainsKey(name))
        {
            value = _values[name];
            return true;
        }
        
        if (_enclosing != null)
        {
            return _enclosing.TryGet(name, out value);
        }
        
        value = default;
        return false;
    }
    
    public bool TryAssign(string name, RuntimeValue value)
    {
        if (_values.ContainsKey(name))
        {
            if (_constNames.Contains(name))
                throw new RuntimeException($"Cannot assign to const '{name}'.");
            _values[name] = value;
            return true;
        }
        
        if (_enclosing != null)
        {
            return _enclosing.TryAssign(name, value);
        }
        
        return false;
    }
    
    public Dictionary<string, RuntimeValue> GetAllVariables()
    {
        var variables = new Dictionary<string, RuntimeValue>();
        CollectVariables(variables);
        return variables;
    }
    
    private void CollectVariables(Dictionary<string, RuntimeValue> variables)
    {
        foreach (var kvp in _values)
        {
            if (!variables.ContainsKey(kvp.Key))
            {
                variables[kvp.Key] = kvp.Value;
            }
        }
        
        if (_enclosing != null)
        {
            _enclosing.CollectVariables(variables);
        }
    }
}