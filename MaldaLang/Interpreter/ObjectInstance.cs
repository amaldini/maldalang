// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Declarations;

public class ObjectInstance
{
    public ClassDefinition? Class { get; }
    private readonly Dictionary<string, RuntimeValue> _fields = new();
    
    public ObjectInstance(ClassDefinition? klass)
    {
        Class = klass;
    }
    
    public virtual RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // When Class is null (e.g. namespace or skill module object), use _fields only
        if (Class == null)
        {
            if (_fields.TryGetValue(name, out var v))
                return v;
            throw new RuntimeException($"Undefined property '{name}' on object.");
        }
        // Check if it's a field
        var field = Class?.FindField(name);
        if (field != null)
        {
            if (field.Access == AccessModifier.Private && accessingClass != Class)
                throw new RuntimeException($"Cannot access private field '{name}' from outside {Class.Name}.");
            
            if (_fields.ContainsKey(name))
                return _fields[name];
            // Field not initialized yet, return null
            return RuntimeValue.Null();
        }
        
        // Check if it's a method
        var method = Class?.FindMethod(name);
        if (method != null)
        {
            return RuntimeValue.Function(method);
        }
        
        throw new RuntimeException($"Undefined property '{name}' on {Class?.Name ?? "object"}.");
    }

    public virtual bool TryGet(string name, out RuntimeValue? value, ClassDefinition? accessingClass = null)
    {
        // When Class is null (e.g. namespace or skill module object), use _fields only
        if (Class == null)
        {
            if (_fields.TryGetValue(name, out var fieldValue))
            {
                value = fieldValue;
                return true;
            }

            value = null;
            return false;
        }

        // Check if it's a field
        var field = Class?.FindField(name);
        if (field != null)
        {
            if (field.Access == AccessModifier.Private && accessingClass != Class)
                throw new RuntimeException($"Cannot access private field '{name}' from outside {Class.Name}.");

            value = _fields.TryGetValue(name, out var fieldValue) ? fieldValue : RuntimeValue.Null();
            return true;
        }

        // Check if it's a method
        var method = Class?.FindMethod(name);
        if (method != null)
        {
            value = RuntimeValue.Function(method);
            return true;
        }

        value = null;
        return false;
    }
    
    public virtual void Set(string name, RuntimeValue value)
    {
        _fields[name] = value;
    }
    
    public virtual IEnumerable<string> GetAllKeys()
    {
        return _fields.Keys;
    }
    
    public override string ToString()
    {
        if (Class == null)
        {
            return "<object instance (no class)>";
        }
        return $"<{Class.Name} instance>";
    }
}