// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Declarations;

public class ClassDefinition
{
    public string Name { get; }
    public ClassDefinition? Superclass { get; }
    public Dictionary<string, ClassMember> Fields { get; }
    public Dictionary<string, FunctionValue> Methods { get; }
    public FunctionValue? Constructor { get; set; }
    public Dictionary<string, RuntimeValue> StaticFields { get; }
    public Dictionary<string, FunctionValue> StaticMethods { get; }
    public Dictionary<string, AccessModifier> MethodAccess { get; }
    public Dictionary<string, AccessModifier> StaticMethodAccess { get; }
    public Dictionary<string, AccessModifier> StaticFieldAccess { get; }
    
    public ClassDefinition(string name, ClassDefinition? superclass)
    {
        Name = name;
        Superclass = superclass;
        Fields = new Dictionary<string, ClassMember>();
        Methods = new Dictionary<string, FunctionValue>();
        StaticFields = new Dictionary<string, RuntimeValue>();
        StaticMethods = new Dictionary<string, FunctionValue>();
        MethodAccess = new Dictionary<string, AccessModifier>();
        StaticMethodAccess = new Dictionary<string, AccessModifier>();
        StaticFieldAccess = new Dictionary<string, AccessModifier>();
    }
    
    public FunctionValue? FindMethod(string name)
    {
        if (Methods.ContainsKey(name))
            return Methods[name];
        
        if (Superclass != null)
            return Superclass.FindMethod(name);
        
        return null;
    }
    
    public ClassMember? FindField(string name)
    {
        if (Fields.ContainsKey(name))
            return Fields[name];
        
        if (Superclass != null)
            return Superclass.FindField(name);
        
        return null;
    }

    /// <summary>
    /// Public (or default-access) instance fields from the root superclass down to this class.
    /// Used by <c>toJSON</c> so class instances dump data, not <c>{}</c>.
    /// </summary>
    public IEnumerable<ClassMember> EnumeratePublicInstanceFieldsFromBase()
    {
        if (Superclass != null)
        {
            foreach (var field in Superclass.EnumeratePublicInstanceFieldsFromBase())
                yield return field;
        }

        foreach (var field in Fields.Values)
        {
            if (field.IsStatic || field.Access == AccessModifier.Private)
                continue;
            yield return field;
        }
    }
}