// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

public class FunctionValue
{
    public FunctionDeclaration? Declaration { get; set; }
    public Environment? Closure { get; set; }
    public bool IsConstructor { get; set; }
    public string? ClassName { get; set; }
    public ObjectInstance? BuiltInInstance { get; set; }
    public string? BuiltInMethod { get; set; }
    /// <summary>When set with BoundBuiltInName, this function is a bound built-in (extension-style): call with [BoundReceiver, ...args].</summary>
    public RuntimeValue? BoundReceiver { get; set; }
    public string? BoundBuiltInName { get; set; }
    /// <summary>When set, this function is a variant constructor: calling it returns RuntimeValue.Variant(VariantConstructorTag, arguments).</summary>
    public string? VariantConstructorTag { get; set; }
    public int VariantConstructorArity { get; set; }
    public List<Decorator>? Decorators { get; set; }
    public List<Decorator>? ParameterDecorators { get; set; }
    /// <summary>When set, this function wraps a transpiled C# delegate (no interpreter required).</summary>
    public Func<object, Task<object>>? TranspiledDelegate { get; set; }
    
    public FunctionValue(FunctionDeclaration? declaration = null, Environment? closure = null, bool isConstructor = false, string? className = null)
    {
        Declaration = declaration;
        Closure = closure;
        IsConstructor = isConstructor;
        ClassName = className;
    }
    
    public override string ToString()
    {
        if (BuiltInInstance != null && BuiltInMethod != null)
            return $"<built-in method {BuiltInMethod}>";
        if (BoundReceiver != null && BoundBuiltInName != null)
            return $"<bound built-in {BoundBuiltInName}>";
        if (IsConstructor)
            return $"<constructor {ClassName}>";
        if (Declaration != null)
            return $"<function {Declaration.Name}>";
        return "<function>";
    }
}