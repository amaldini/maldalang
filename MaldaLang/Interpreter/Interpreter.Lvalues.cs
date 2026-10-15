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
    private async Task<RuntimeValue> EvaluatePostfixAsync(PostfixExpression expr)
    {
        return await EvaluatePostfixIncrementDecrementAsync(expr.Operator, expr.Left, expr.Line);
    }
    
    private async Task<RuntimeValue> EvaluatePrefixIncrementDecrementAsync(TokenType op, Expression target, int? line = null)
    {
        // Get current value
        var currentValue = await GetLvalueAsync(target);
        CheckNumberOperand(currentValue);
        
        // Calculate new value
        RuntimeValue newValue;
        if (op == TokenType.Increment)
        {
            if (currentValue.Type == ValueType.Integer)
                newValue = IntegerResult((long)currentValue.AsInteger() + 1, line);
            else
                newValue = RuntimeValue.Float(currentValue.AsFloat() + 1.0);
        }
        else // Decrement
        {
            if (currentValue.Type == ValueType.Integer)
                newValue = IntegerResult((long)currentValue.AsInteger() - 1, line);
            else
                newValue = RuntimeValue.Float(currentValue.AsFloat() - 1.0);
        }
        
        // Assign new value back
        await SetLvalueAsync(target, newValue);
        
        // Return new value (prefix returns the new value)
        return newValue;
    }
    
    private async Task<RuntimeValue> EvaluatePostfixIncrementDecrementAsync(TokenType op, Expression target, int? line = null)
    {
        // Get current value
        var currentValue = await GetLvalueAsync(target);
        CheckNumberOperand(currentValue);
        
        // Store original value to return (postfix returns the old value)
        RuntimeValue originalValue;
        if (currentValue.Type == ValueType.Integer)
            originalValue = RuntimeValue.Integer(currentValue.AsInteger());
        else
            originalValue = RuntimeValue.Float(currentValue.AsFloat());
        
        // Calculate new value
        RuntimeValue newValue;
        if (op == TokenType.Increment)
        {
            if (currentValue.Type == ValueType.Integer)
                newValue = IntegerResult((long)currentValue.AsInteger() + 1, line);
            else
                newValue = RuntimeValue.Float(currentValue.AsFloat() + 1.0);
        }
        else // Decrement
        {
            if (currentValue.Type == ValueType.Integer)
                newValue = IntegerResult((long)currentValue.AsInteger() - 1, line);
            else
                newValue = RuntimeValue.Float(currentValue.AsFloat() - 1.0);
        }
        
        // Assign new value back
        await SetLvalueAsync(target, newValue);
        
        // Return original value (postfix returns the old value)
        return originalValue;
    }
    
    /// <summary>Current value of a compound-assignment target (no built-in fallback, unlike <see cref="LookUpVariable"/>).</summary>
    private RuntimeValue ReadIdentifierLvalue(IdentifierExpression idExpr)
    {
        var memberFrame = CurrentClassMemberFrame();
        if (memberFrame != null)
        {
            if (TryGetFunctionLocal(idExpr.Name, memberFrame, out var local))
                return local;
            if (TryReadImplicitMember(idExpr.Name, out var member))
                return member;
        }

        if (_environment.TryGet(idExpr.Name, out var value))
            return value;

        if (memberFrame == null && TryReadImplicitMember(idExpr.Name, out var leakedMember))
            return leakedMember;

        throw new RuntimeException($"Undefined variable '{idExpr.Name}'.", idExpr.Line, _currentFile);
    }

    private async Task<RuntimeValue> GetLvalueAsync(Expression target)
    {
        if (target is IdentifierExpression idExpr)
        {
            return ReadIdentifierLvalue(idExpr);
        }
        else if (target is MemberAccessExpression memberExpr)
        {
            var obj = await EvaluateAsync(memberExpr.Object);
            if (obj.Type == ValueType.Object)
            {
                var instance = obj.AsObject();
                // Handle JsonObject specially (same as EvaluateMemberAccessAsync)
                if (instance is BuiltIns.JsonObject jsonObj)
                {
                    try
                    {
                        return jsonObj.Get(memberExpr.Member, null);
                    }
                    catch
                    {
                        return RuntimeValue.Null();
                    }
                }
                return instance.Get(memberExpr.Member);
            }
            else if (obj.Type == ValueType.Class)
            {
                var klass = obj.AsClass();
                if (klass.StaticFields.ContainsKey(memberExpr.Member))
                {
                    if (klass.StaticFieldAccess.TryGetValue(memberExpr.Member, out var fieldAccess)
                        && fieldAccess == AccessModifier.Private
                        && _currentClass != klass)
                    {
                        throw new RuntimeException($"Cannot access private static field '{memberExpr.Member}' from outside {klass.Name}.");
                    }
                    return klass.StaticFields[memberExpr.Member];
                }
                throw new RuntimeException($"Class {klass.Name} has no static field '{memberExpr.Member}'.");
            }
            throw new RuntimeException("Only objects and classes have properties.", memberExpr.Line, _currentFile);
        }
        else if (target is ArrayAccessExpression arrayExpr)
        {
            var array = await EvaluateAsync(arrayExpr.Array);
            var index = await EvaluateAsync(arrayExpr.Index);
            // Support object and dictionary indexing with string keys
            if (array.Type == ValueType.Object && index.Type == ValueType.String)
            {
                var obj = array.AsObject();
                var key = index.AsString();
                
                // Handle DictionaryInstance
                if (obj is DictionaryInstance dict)
                {
                    return dict.TryGetEntry(key, out var value) ? value : RuntimeValue.Null();
                }
                
                // Handle JsonObject (for JSON parsing results and query params)
                if (obj is BuiltIns.JsonObject jsonObj)
                {
                    try
                    {
                        return jsonObj.Get(key, null);
                    }
                    catch
                    {
                        return RuntimeValue.Null();
                    }
                }
                
                // For other object types, try to get the property
                try
                {
                    return obj.Get(key, null);
                }
                catch
                {
                    return RuntimeValue.Null();
                }
            }
            if (array.Type != ValueType.Array)
                throw new RuntimeException("Only arrays can be indexed.", arrayExpr.Line, _currentFile);
            if (!NumericCoercion.TryAsInteger(index, out var idx))
                throw new RuntimeException("Array index must be an integer.", arrayExpr.Line, _currentFile);
            var arr = array.AsArray();
            if (idx < 0 || idx >= arr.Count)
                throw new RuntimeException("Array index out of bounds.", arrayExpr.Line, _currentFile);
            return arr[idx];
        }
        else
        {
            throw new RuntimeException("Invalid lvalue for increment/decrement.", target?.Line, _currentFile);
        }
    }
    
    private void AssignIdentifier(IdentifierExpression idExpr, RuntimeValue value)
    {
        var memberFrame = CurrentClassMemberFrame();
        if (memberFrame != null)
        {
            if (TryAssignFunctionLocal(idExpr.Name, value, memberFrame))
                return;
            if (TryWriteImplicitMember(idExpr.Name, value, idExpr.Line))
                return;
        }

        switch (_environment.TryAssignMutable(idExpr.Name, value))
        {
            case AssignResult.Assigned:
                return;
            case AssignResult.Const:
                throw new RuntimeException($"Cannot assign to const '{idExpr.Name}'.", idExpr.Line, _currentFile);
        }

        if (memberFrame == null && TryWriteImplicitMember(idExpr.Name, value, idExpr.Line))
            return;

        throw new RuntimeException($"Undefined variable '{idExpr.Name}'.", idExpr.Line, _currentFile);
    }

    private async Task SetLvalueAsync(Expression target, RuntimeValue value)
    {
        if (target is IdentifierExpression idExpr)
        {
            AssignIdentifier(idExpr, value);
        }
        else if (target is MemberAccessExpression memberExpr)
        {
            var obj = await EvaluateAsync(memberExpr.Object);
            if (obj.Type == ValueType.Object)
            {
                obj.AsObject().Set(memberExpr.Member, value);
            }
            else if (obj.Type == ValueType.Class)
            {
                var klass = obj.AsClass();
                if (klass.StaticFields.ContainsKey(memberExpr.Member))
                {
                    if (klass.StaticFieldAccess.TryGetValue(memberExpr.Member, out var fieldAccess)
                        && fieldAccess == AccessModifier.Private
                        && _currentClass != klass)
                    {
                        throw new RuntimeException($"Cannot access private static field '{memberExpr.Member}' from outside {klass.Name}.");
                    }
                    klass.StaticFields[memberExpr.Member] = value;
                }
                else
                {
                    throw new RuntimeException($"Class {klass.Name} has no static field '{memberExpr.Member}'.");
                }
            }
            else
            {
                throw new RuntimeException("Only objects and classes have properties.", memberExpr.Line, _currentFile);
            }
        }
        else if (target is ArrayAccessExpression arrayExpr)
        {
            var array = await EvaluateAsync(arrayExpr.Array);
            var index = await EvaluateAsync(arrayExpr.Index);
            // Support object and dictionary indexing with string keys
            if (array.Type == ValueType.Object && index.Type == ValueType.String)
            {
                var obj = array.AsObject();
                var key = index.AsString();
                
                // Handle DictionaryInstance
                if (obj is DictionaryInstance dict)
                {
                    dict.SetEntry(key, value);
                    return;
                }
                
                // Handle JsonObject (for JSON parsing results and query params)
                if (obj is BuiltIns.JsonObject jsonObj)
                {
                    jsonObj.Set(key, value);
                    return;
                }
                
                // For other object types, try to set the property
                obj.Set(key, value);
                return;
            }
            if (array.Type != ValueType.Array)
                throw new RuntimeException("Only arrays can be indexed.", arrayExpr.Line, _currentFile);
            if (!NumericCoercion.TryAsInteger(index, out var idx))
                throw new RuntimeException("Array index must be an integer.", arrayExpr.Line, _currentFile);
            var arr = array.AsArray();
            if (idx < 0)
                throw new RuntimeException("Array index out of bounds.", arrayExpr.Line, _currentFile);
            
            // Arrays automatically grow when assigning to out-of-bounds indices
            // Extend array with null values if needed
            while (arr.Count <= idx)
            {
                arr.Add(RuntimeValue.Null());
            }
            
            arr[idx] = value;
        }
        else
        {
            throw new RuntimeException("Invalid lvalue for assignment.", target?.Line, _currentFile);
        }
    }
}
