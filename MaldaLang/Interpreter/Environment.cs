// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

/// <summary>Outcome of <see cref="Environment.TryAssignMutable"/>.</summary>
public enum AssignResult
{
    NotFound,
    Assigned,
    Const
}

public class Environment
{
    private readonly Dictionary<string, RuntimeValue> _values = new();
    // Most scopes declare no const; the set is created on the first one.
    private HashSet<string>? _constNames;
    private readonly Environment? _enclosing;
    
    public Environment(Environment? enclosing = null)
    {
        _enclosing = enclosing;
    }
    
    public void Define(string name, RuntimeValue value, bool isConst = false)
    {
        _values[name] = value;
        if (isConst)
            (_constNames ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
    }

    private bool IsOwnConst(string name) => _constNames != null && _constNames.Contains(name);

    public bool IsConst(string name)
    {
        for (Environment? env = this; env != null; env = env._enclosing)
        {
            if (env._values.ContainsKey(name))
                return env.IsOwnConst(name);
        }
        return false;
    }
    
    public RuntimeValue Get(string name)
    {
        if (TryGet(name, out var value))
            return value;
        
        throw new RuntimeException($"Undefined variable '{name}'.");
    }
    
    public void Assign(string name, RuntimeValue value)
    {
        if (!TryAssign(name, value))
            throw new RuntimeException($"Undefined variable '{name}'.");
    }
    
    public Environment? GetEnclosing()
    {
        return _enclosing;
    }
    
    public bool Contains(string name)
    {
        for (Environment? env = this; env != null; env = env._enclosing)
        {
            if (env._values.ContainsKey(name))
                return true;
        }
        return false;
    }
    
    public bool TryGet(string name, out RuntimeValue value)
    {
        for (Environment? env = this; env != null; env = env._enclosing)
        {
            if (env._values.TryGetValue(name, out value!))
                return true;
        }
        
        value = default!;
        return false;
    }
    
    public bool TryAssign(string name, RuntimeValue value)
    {
        return TryAssignMutable(name, value) switch
        {
            AssignResult.Assigned => true,
            AssignResult.Const => throw new RuntimeException($"Cannot assign to const '{name}'."),
            _ => false
        };
    }

    /// <summary>
    /// Assigns the nearest binding of <paramref name="name"/> in one walk. A const binding is
    /// left unchanged and reported, so the caller can throw with its own source location.
    /// </summary>
    public AssignResult TryAssignMutable(string name, RuntimeValue value)
    {
        for (Environment? env = this; env != null; env = env._enclosing)
        {
            ref var slot = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(env._values, name);
            if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref slot))
                continue;
            if (env.IsOwnConst(name))
                return AssignResult.Const;
            slot = value;
            return AssignResult.Assigned;
        }
        return AssignResult.NotFound;
    }

    /// <summary>
    /// Look up <paramref name="name"/> on this frame and its ancestors, stopping before
    /// <paramref name="stopExclusive"/> (typically a function's closure / globals).
    /// </summary>
    public bool TryGetUntil(string name, Environment? stopExclusive, out RuntimeValue value)
    {
        Environment? env = this;
        while (env != null && !ReferenceEquals(env, stopExclusive))
        {
            if (env._values.TryGetValue(name, out value!))
                return true;
            env = env._enclosing;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Assign <paramref name="name"/> on this frame and its ancestors, stopping before
    /// <paramref name="stopExclusive"/>. Returns false if the name is not a local in that range.
    /// </summary>
    public bool TryAssignUntil(string name, RuntimeValue value, Environment? stopExclusive)
    {
        Environment? env = this;
        while (env != null && !ReferenceEquals(env, stopExclusive))
        {
            if (env._values.ContainsKey(name))
            {
                if (env.IsOwnConst(name))
                    throw new RuntimeException($"Cannot assign to const '{name}'.");
                env._values[name] = value;
                return true;
            }
            env = env._enclosing;
        }

        return false;
    }
    
    public Dictionary<string, RuntimeValue> GetAllVariables()
    {
        var variables = new Dictionary<string, RuntimeValue>();
        CollectVariables(variables);
        return variables;
    }

    /// <summary>
    /// Bindings defined in this frame only (no enclosing walk). Used when merging
    /// module exports so stdlib parents (<c>math</c>/<c>str</c>/<c>io</c>) are not re-exported.
    /// </summary>
    public Dictionary<string, RuntimeValue> GetOwnVariables()
    {
        return new Dictionary<string, RuntimeValue>(_values, StringComparer.Ordinal);
    }

    /// <summary>
    /// Removes a binding defined on this frame only. Returns false when the name
    /// is not present here (enclosing frames are not walked).
    /// </summary>
    public bool TryRemoveOwn(string name)
    {
        _constNames?.Remove(name);
        return _values.Remove(name);
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