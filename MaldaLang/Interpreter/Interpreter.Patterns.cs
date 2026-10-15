// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Declarations;
using System.Threading;
using MaldaLang.BuiltIns;
using MaldaLang.BuiltIns.LLMClientBridge.BackendAdapters;
using System.Reflection;
using System.IO;
using System;
using System.Globalization;
using System.Text.Json;
using System.Linq;
using MaldaLang.PackageManager;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Interpreter.Debug;

public partial class Interpreter
{
    private async Task<RuntimeValue> EvaluateMatchAsync(MatchExpression matchExpr)
    {
        var savedEnv = _environment;
        try
        {
            var value = await EvaluateAsync(matchExpr.Value);
            
            // Try each case in order
            foreach (var matchCase in matchExpr.Cases)
            {
                var bindings = MatchPattern(matchCase.Pattern, value);
                if (bindings != null)
                {
                    // Create a new environment scope for the case body with the bindings
                    var caseEnv = new Environment(_environment);
                    foreach (var binding in bindings)
                    {
                        caseEnv.Define(binding.Key, binding.Value);
                    }

                    var previousEnv = _environment;
                    try
                    {
                        _environment = caseEnv;
                        if (matchCase.Guard != null)
                        {
                            var guardValue = await EvaluateAsync(matchCase.Guard);
                            if (!guardValue.IsTruthy())
                                continue;
                        }

                        return await EvaluateMatchBodyAsync(matchCase.Body);
                    }
                    finally
                    {
                        _environment = previousEnv;
                    }
                }
            }
            
            // No case matched, execute default case if present
            if (matchExpr.DefaultCase != null)
            {
                return await EvaluateMatchBodyAsync(matchExpr.DefaultCase);
            }
            
            throw new RuntimeException("Match expression had no matching case and no default case.");
        }
        finally
        {
            _environment = savedEnv;
        }
    }
    
    /// <summary>
    /// Evaluates a match case body (or default) and returns its value.
    /// "Last expression wins": for blocks, the last statement's value is returned if it's an expression.
    /// </summary>
    private async Task<RuntimeValue> EvaluateMatchBodyAsync(Statement body)
    {
        if (body is ExpressionStatement exprStmt)
        {
            return await EvaluateAsync(exprStmt.Expression);
        }
        if (body is BlockStatement block)
        {
            if (block.Statements.Count == 0)
                return RuntimeValue.Null();
            for (int i = 0; i < block.Statements.Count - 1; i++)
            {
                await ExecuteAsync(block.Statements[i]);
            }
            var last = block.Statements[block.Statements.Count - 1];
            if (last is ExpressionStatement lastExpr)
                return await EvaluateAsync(lastExpr.Expression);
            await ExecuteAsync(last);
            return RuntimeValue.Null();
        }
        await ExecuteAsync(body);
        return RuntimeValue.Null();
    }
    
    private Dictionary<string, RuntimeValue>? MatchPattern(Pattern pattern, RuntimeValue value)
    {
        var bindings = new Dictionary<string, RuntimeValue>();
        
        switch (pattern)
        {
            case LiteralPattern literalPattern:
                return MatchLiteralPattern(literalPattern, value) ? bindings : null;
                
            case IdentifierPattern identifierPattern:
                if (TryGetVariantConstructor(identifierPattern.Name, out var ctorTag, out var ctorArity))
                {
                    var variantPattern = VariantPattern.WithImplicitWildcards(
                        ctorTag, ctorArity, identifierPattern.Line, identifierPattern.Column);
                    return MatchVariantPattern(variantPattern, value, bindings);
                }
                if (_environment.IsConst(identifierPattern.Name) &&
                    _environment.TryGet(identifierPattern.Name, out var constValue))
                {
                    return IsEqual(value, constValue) ? bindings : null;
                }
                bindings[identifierPattern.Name] = value;
                return bindings;
                
            case WildcardPattern:
                return bindings; // Always matches, no bindings
                
            case ArrayPattern arrayPattern:
                return MatchArrayPattern(arrayPattern, value, bindings);
                
            case ObjectPattern objectPattern:
                return MatchObjectPattern(objectPattern, value, bindings);

            case VariantPattern variantPattern:
                return MatchVariantPattern(variantPattern, value, bindings);

            case RestPattern:
                // Rest pattern only valid inside array pattern; cannot match a single value
                return null;

            default:
                throw new RuntimeException($"Unknown pattern type: {pattern.GetType()}");
        }
    }

    private bool TryGetVariantConstructor(string name, out string tag, out int arity)
    {
        tag = name;
        arity = 0;
        if (_environment.TryGet(name, out var bound) && bound.Type == ValueType.Function)
        {
            var fn = bound.AsFunction();
            if (fn.VariantConstructorTag != null)
            {
                tag = fn.VariantConstructorTag;
                arity = fn.VariantConstructorArity;
                return true;
            }
        }

        // After `import { Result }`, constructors are not flattened into the host.
        // `case Ok` still matches the tag when any registered sum type declares it.
        if (MaldaLang.BuiltIns.SumTypeRegistry.TryGetConstructorArity(name, out arity))
        {
            tag = name;
            return true;
        }

        return false;
    }

    private Dictionary<string, RuntimeValue>? MatchVariantPattern(VariantPattern pattern, RuntimeValue value, Dictionary<string, RuntimeValue> bindings)
    {
        if (value.Type != ValueType.Variant)
            return null;
        var v = value.AsVariant();
        if (v.Tag != pattern.Tag || v.Payload.Count != pattern.PayloadPatterns.Count)
            return null;
        for (int i = 0; i < pattern.PayloadPatterns.Count; i++)
        {
            var sub = MatchPattern(pattern.PayloadPatterns[i], v.Payload[i]);
            if (sub == null)
                return null;
            foreach (var kv in sub)
                bindings[kv.Key] = kv.Value;
        }
        return bindings;
    }

    private bool MatchLiteralPattern(LiteralPattern pattern, RuntimeValue value)
    {
        if (pattern.Value == null)
        {
            return value.Type == ValueType.Null;
        }
        
        return value.Type switch
        {
            ValueType.Integer => value.AsInteger() == (int)(pattern.Value ?? 0),
            ValueType.Float => Math.Abs(value.AsFloat() - (double)(pattern.Value ?? 0.0)) < 0.0001,
            ValueType.String => value.AsString() == (string)(pattern.Value ?? ""),
            ValueType.Boolean => value.AsBoolean() == (bool)(pattern.Value ?? false),
            ValueType.Null => pattern.Value == null,
            _ => false
        };
    }
    
    private Dictionary<string, RuntimeValue>? MatchArrayPattern(ArrayPattern pattern, RuntimeValue value, Dictionary<string, RuntimeValue> bindings)
    {
        if (value.Type != ValueType.Array)
        {
            return null;
        }
        
        var array = value.AsArray();
        var requiredElements = pattern.Elements.Count;
        var hasRest = pattern.Rest != null;
        
        // Check if we have enough elements (or exactly enough if no rest)
        if (!hasRest && array.Count != requiredElements)
        {
            return null;
        }
        
        if (hasRest && array.Count < requiredElements)
        {
            return null;
        }
        
        // Match each element pattern
        for (int i = 0; i < requiredElements; i++)
        {
            var elementPattern = pattern.Elements[i];
            var elementValue = array[i];
            var elementBindings = MatchPattern(elementPattern, elementValue);
            if (elementBindings == null)
            {
                return null; // Element didn't match
            }
            
            // Merge bindings
            foreach (var binding in elementBindings)
            {
                bindings[binding.Key] = binding.Value;
            }
        }
        
        // Handle rest pattern
        if (hasRest && pattern.Rest != null)
        {
            var restElements = array.Skip(requiredElements).ToList();
            var restArray = RuntimeValue.Array(restElements);
            if (pattern.Rest.Name != null)
            {
                bindings[pattern.Rest.Name] = restArray;
            }
        }
        
        return bindings;
    }
    
    private Dictionary<string, RuntimeValue>? MatchObjectPattern(ObjectPattern pattern, RuntimeValue value, Dictionary<string, RuntimeValue> bindings)
    {
        if (value.Type != ValueType.Object)
        {
            return null;
        }
        
        var obj = value.AsObject();
        var keySet = new HashSet<string>(obj.GetAllKeys());
        
        // Match each property pattern
        foreach (var prop in pattern.Properties)
        {
            if (!keySet.Contains(prop.Key))
                return null; // Required property missing
            RuntimeValue propValue;
            try
            {
                propValue = obj.Get(prop.Key, null);
            }
            catch
            {
                return null; // Property doesn't exist
            }
            
            if (prop.Pattern != null)
            {
                // Nested pattern matching
                var propBindings = MatchPattern(prop.Pattern, propValue);
                if (propBindings == null)
                {
                    return null; // Property pattern didn't match
                }
                
                // Merge bindings
                foreach (var binding in propBindings)
                {
                    bindings[binding.Key] = binding.Value;
                }
            }
            else if (prop.BindingName != null)
            {
                // Simple binding: { key } means bind obj.key to key
                bindings[prop.BindingName] = propValue;
            }
        }
        
        return bindings;
    }
    
    private async Task<RuntimeValue?> ExecuteDestructuringVarDeclAsync(DestructuringVarDecl stmt)
    {
        var value = await EvaluateAsync(stmt.Initializer);
        var bindings = MatchDestructuringPattern(stmt.Pattern, value);
        
        if (bindings == null)
        {
            throw new RuntimeException("Destructuring pattern did not match value.");
        }
        
        // Define all bound variables in the current environment
        foreach (var binding in bindings)
        {
            _environment.Define(binding.Key, binding.Value);
        }
        
        return null;
    }
    
    private async Task<RuntimeValue?> ExecuteDestructuringAssignmentAsync(DestructuringAssignment stmt)
    {
        var value = await EvaluateAsync(stmt.Value);
        var bindings = MatchDestructuringPattern(stmt.Pattern, value);
        
        if (bindings == null)
        {
            throw new RuntimeException("Destructuring pattern did not match value.");
        }
        
        // Assign all bound variables in the current environment
        foreach (var binding in bindings)
        {
            _environment.Assign(binding.Key, binding.Value);
        }
        
        return null;
    }
    
    private Dictionary<string, RuntimeValue>? MatchDestructuringPattern(DestructuringPattern pattern, RuntimeValue value)
    {
        var bindings = new Dictionary<string, RuntimeValue>();
        
        switch (pattern)
        {
            case ArrayDestructuringPattern arrayPattern:
                return MatchArrayDestructuringPattern(arrayPattern, value, bindings);
                
            case ObjectDestructuringPattern objectPattern:
                return MatchObjectDestructuringPattern(objectPattern, value, bindings);
                
            default:
                throw new RuntimeException($"Unknown destructuring pattern type: {pattern.GetType()}");
        }
    }
    
    private Dictionary<string, RuntimeValue>? MatchArrayDestructuringPattern(ArrayDestructuringPattern pattern, RuntimeValue value, Dictionary<string, RuntimeValue> bindings)
    {
        if (value.Type != ValueType.Array)
        {
            return null;
        }
        
        var array = value.AsArray();
        var requiredElements = pattern.Elements.Count;
        var hasRest = pattern.Rest != null;
        
        if (!hasRest && array.Count != requiredElements)
        {
            return null;
        }
        
        if (hasRest && array.Count < requiredElements)
        {
            return null;
        }
        
        // Extract each element
        for (int i = 0; i < requiredElements; i++)
        {
            var elementPattern = pattern.Elements[i];
            var elementValue = array[i];
            
            if (elementPattern is IdentifierPattern idPattern)
            {
                bindings[idPattern.Name] = elementValue;
            }
            else if (elementPattern is WildcardPattern)
            {
                // Ignore wildcard
            }
            else
            {
                // Nested pattern - recursively match
                var elementBindings = MatchPattern(elementPattern, elementValue);
                if (elementBindings == null)
                {
                    return null;
                }
                foreach (var binding in elementBindings)
                {
                    bindings[binding.Key] = binding.Value;
                }
            }
        }
        
        // Handle rest pattern
        if (hasRest && pattern.Rest != null)
        {
            var restElements = array.Skip(requiredElements).ToList();
            var restArray = RuntimeValue.Array(restElements);
            if (pattern.Rest.Name != null)
            {
                bindings[pattern.Rest.Name] = restArray;
            }
        }
        
        return bindings;
    }
    
    private Dictionary<string, RuntimeValue>? MatchObjectDestructuringPattern(ObjectDestructuringPattern pattern, RuntimeValue value, Dictionary<string, RuntimeValue> bindings)
    {
        if (value.Type != ValueType.Object)
        {
            return null;
        }
        
        var obj = value.AsObject();
        var keySet = new HashSet<string>(obj.GetAllKeys());
        
        foreach (var prop in pattern.Properties)
        {
            if (!keySet.Contains(prop.Key))
                return null; // Required property missing
            RuntimeValue propValue;
            try
            {
                propValue = obj.Get(prop.Key, null);
            }
            catch
            {
                return null; // Property doesn't exist
            }
            
            if (prop.Pattern != null)
            {
                // Nested pattern
                var propBindings = MatchPattern(prop.Pattern, propValue);
                if (propBindings == null)
                {
                    return null;
                }
                foreach (var binding in propBindings)
                {
                    bindings[binding.Key] = binding.Value;
                }
            }
            else if (prop.BindingName != null)
            {
                // Simple binding
                bindings[prop.BindingName] = propValue;
            }
        }
        
        return bindings;
    }
}
