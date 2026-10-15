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
    private async Task<RuntimeValue> EvaluateBinaryAsync(BinaryExpression expr)
    {
        // Handle short-circuiting operators first
        if (expr.Operator == TokenType.And)
        {
            var left = await EvaluateAsync(expr.Left);
            if (!left.IsTruthy())
            {
                return RuntimeValue.Boolean(false);
            }
            var right = await EvaluateAsync(expr.Right);
            return RuntimeValue.Boolean(right.IsTruthy());
        }
        
        if (expr.Operator == TokenType.Or)
        {
            var left = await EvaluateAsync(expr.Left);
            if (left.IsTruthy())
            {
                return RuntimeValue.Boolean(true);
            }
            var right = await EvaluateAsync(expr.Right);
            return RuntimeValue.Boolean(right.IsTruthy());
        }

        if (expr.Operator == TokenType.NullCoalesce)
        {
            var left = await EvaluateAsync(expr.Left);
            if (left.Type != ValueType.Null)
                return left;
            return await EvaluateAsync(expr.Right);
        }
        
        // For all other operators, evaluate both sides
        var leftVal = await EvaluateAsync(expr.Left);
        var rightVal = await EvaluateAsync(expr.Right);

        if (leftVal.Type == ValueType.Object)
        {
            var overloadedResult = await TryBinaryOperatorOverload(expr.Operator, leftVal, rightVal);
            if (overloadedResult != null)
                return overloadedResult;
        }

        if (rightVal.Type == ValueType.Object)
        {
            var reversedResult = await TryReversedBinaryOperatorOverload(expr.Operator, leftVal, rightVal);
            if (reversedResult != null)
                return reversedResult;
        }

        return ApplyBinaryOperator(expr, leftVal, rightVal);
    }

    /// <summary>Non-short-circuit binary operators on evaluated operands (no overloads).</summary>
    private RuntimeValue ApplyBinaryOperator(BinaryExpression expr, RuntimeValue leftVal, RuntimeValue rightVal)
    {
        switch (expr.Operator)
        {
            case TokenType.Plus:
                return EvaluatePlus(leftVal, rightVal, expr.Line);
            case TokenType.Minus:
                return EvaluateMinus(leftVal, rightVal, expr.Line);
            case TokenType.Multiply:
                return EvaluateMultiply(leftVal, rightVal, expr.Line);
            case TokenType.Divide:
                return EvaluateDivide(leftVal, rightVal, expr.Line);
            case TokenType.Modulo:
                return EvaluateModulo(leftVal, rightVal, expr.Line);
            case TokenType.Equal:
                return RuntimeValue.Boolean(IsEqual(leftVal, rightVal));
            case TokenType.NotEqual:
                return RuntimeValue.Boolean(!IsEqual(leftVal, rightVal));
            case TokenType.GreaterThan:
                return RuntimeValue.Boolean(EvaluateGreaterThan(leftVal, rightVal));
            case TokenType.GreaterThanOrEqual:
                return RuntimeValue.Boolean(EvaluateGreaterThanOrEqual(leftVal, rightVal));
            case TokenType.LessThan:
                return RuntimeValue.Boolean(EvaluateLessThan(leftVal, rightVal));
            case TokenType.LessThanOrEqual:
                return RuntimeValue.Boolean(EvaluateLessThanOrEqual(leftVal, rightVal));
            default:
                throw new RuntimeException($"Unknown binary operator: {expr.Operator}", expr.Line, _currentFile);
        }
    }

    private static string? GetBinaryOperatorMethodName(TokenType op)
    {
        return op switch
        {
            TokenType.Plus => "__add__",
            TokenType.Minus => "__sub__",
            TokenType.Multiply => "__mul__",
            TokenType.Divide => "__div__",
            TokenType.Modulo => "__mod__",
            TokenType.Equal => "__eq__",
            TokenType.NotEqual => "__neq__",
            TokenType.LessThan => "__lt__",
            TokenType.LessThanOrEqual => "__le__",
            TokenType.GreaterThan => "__gt__",
            TokenType.GreaterThanOrEqual => "__ge__",
            _ => null
        };
    }

    private static string? GetReversedBinaryOperatorMethodName(TokenType op)
    {
        return op switch
        {
            TokenType.Plus => "__radd__",
            TokenType.Minus => "__rsub__",
            TokenType.Multiply => "__rmul__",
            TokenType.Divide => "__rdiv__",
            TokenType.Modulo => "__rmod__",
            TokenType.Equal => "__req__",
            TokenType.NotEqual => "__rneq__",
            TokenType.LessThan => "__rlt__",
            TokenType.LessThanOrEqual => "__rle__",
            TokenType.GreaterThan => "__rgt__",
            TokenType.GreaterThanOrEqual => "__rge__",
            _ => null
        };
    }

    private async Task<RuntimeValue?> TryBinaryOperatorOverload(TokenType op, RuntimeValue leftVal, RuntimeValue rightVal)
    {
        var methodName = GetBinaryOperatorMethodName(op);
        if (methodName == null)
            return null;

        var receiver = leftVal.AsObject();
        if (!receiver.TryGet(methodName, out var method))
            return null;

        if (method == null || method.Type != ValueType.Function)
            return null;

        return await CallFunctionAsync(method.AsFunction(), new List<RuntimeValue> { rightVal }, receiver);
    }

    private async Task<RuntimeValue?> TryReversedBinaryOperatorOverload(TokenType op, RuntimeValue leftVal, RuntimeValue rightVal)
    {
        var methodName = GetReversedBinaryOperatorMethodName(op);
        if (methodName == null)
            return null;

        var receiver = rightVal.AsObject();
        if (!receiver.TryGet(methodName, out var method))
            return null;

        if (method == null || method.Type != ValueType.Function)
            return null;

        return await CallFunctionAsync(method.AsFunction(), new List<RuntimeValue> { leftVal }, receiver);
    }
    
    private RuntimeValue EvaluatePlus(RuntimeValue left, RuntimeValue right, int? line = null)
    {
        if (left.Type == ValueType.String || right.Type == ValueType.String)
        {
            return RuntimeValue.String(left.ToString() + right.ToString());
        }
        if (left.Type == ValueType.Integer && right.Type == ValueType.Integer)
        {
            return IntegerResult((long)left.AsInteger() + right.AsInteger(), line);
        }
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return RuntimeValue.Float(leftVal + rightVal);
    }
    
    private RuntimeValue EvaluateMinus(RuntimeValue left, RuntimeValue right, int? line = null)
    {
        CheckNumberOperands(left, right);
        if (left.Type == ValueType.Integer && right.Type == ValueType.Integer)
        {
            return IntegerResult((long)left.AsInteger() - right.AsInteger(), line);
        }
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return RuntimeValue.Float(leftVal - rightVal);
    }
    
    private RuntimeValue EvaluateMultiply(RuntimeValue left, RuntimeValue right, int? line = null)
    {
        // Handle string repetition: string * number or number * string
        if (left.Type == ValueType.String && (right.Type == ValueType.Integer || right.Type == ValueType.Float))
        {
            var count = right.Type == ValueType.Integer ? right.AsInteger() : (int)right.AsFloat();
            if (count <= 0)
                return RuntimeValue.String("");
            var str = left.AsString();
            return RuntimeValue.String(string.Concat(Enumerable.Repeat(str, count)));
        }
        if ((left.Type == ValueType.Integer || left.Type == ValueType.Float) && right.Type == ValueType.String)
        {
            var count = left.Type == ValueType.Integer ? left.AsInteger() : (int)left.AsFloat();
            if (count <= 0)
                return RuntimeValue.String("");
            var str = right.AsString();
            return RuntimeValue.String(string.Concat(Enumerable.Repeat(str, count)));
        }
        
        // Numeric multiplication
        CheckNumberOperands(left, right);
        if (left.Type == ValueType.Integer && right.Type == ValueType.Integer)
        {
            return IntegerResult((long)left.AsInteger() * right.AsInteger(), line);
        }
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return RuntimeValue.Float(leftVal * rightVal);
    }
    
    private RuntimeValue EvaluateDivide(RuntimeValue left, RuntimeValue right, int? line = null)
    {
        CheckNumberOperands(left, right);
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        if (rightVal == 0)
            throw new RuntimeException("Division by zero.", line, _currentFile);
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        return RuntimeValue.Float(leftVal / rightVal);
    }
    
    private RuntimeValue EvaluateModulo(RuntimeValue left, RuntimeValue right, int? line = null)
    {
        CheckNumberOperands(left, right);
        if (left.Type == ValueType.Integer && right.Type == ValueType.Integer)
        {
            if (right.AsInteger() == 0)
                throw new RuntimeException("Division by zero.", line, _currentFile);
            var dividend = left.AsInteger();
            var divisor = right.AsInteger();
            if (divisor == -1 && dividend == int.MinValue)
                throw new RuntimeException("Integer overflow.", line, _currentFile);
            return RuntimeValue.Integer(dividend % divisor);
        }
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        if (rightVal == 0)
            throw new RuntimeException("Division by zero.", line, _currentFile);
        return RuntimeValue.Float(leftVal % rightVal);
    }
    
    private bool EvaluateGreaterThan(RuntimeValue left, RuntimeValue right)
    {
        // Support string comparison
        if (left.Type == ValueType.String && right.Type == ValueType.String)
        {
            return string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal) > 0;
        }
        
        // Existing numeric comparison
        CheckNumberOperands(left, right);
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return leftVal > rightVal;
    }
    
    private bool EvaluateGreaterThanOrEqual(RuntimeValue left, RuntimeValue right)
    {
        // Support string comparison
        if (left.Type == ValueType.String && right.Type == ValueType.String)
        {
            return string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal) >= 0;
        }
        
        // Existing numeric comparison
        CheckNumberOperands(left, right);
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return leftVal >= rightVal;
    }
    
    private bool EvaluateLessThan(RuntimeValue left, RuntimeValue right)
    {
        // Support string comparison
        if (left.Type == ValueType.String && right.Type == ValueType.String)
        {
            return string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal) < 0;
        }
        
        // Existing numeric comparison
        CheckNumberOperands(left, right);
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return leftVal < rightVal;
    }
    
    private bool EvaluateLessThanOrEqual(RuntimeValue left, RuntimeValue right)
    {
        // Support string comparison
        if (left.Type == ValueType.String && right.Type == ValueType.String)
        {
            return string.Compare(left.AsString(), right.AsString(), StringComparison.Ordinal) <= 0;
        }
        
        // Existing numeric comparison
        CheckNumberOperands(left, right);
        var leftVal = left.Type == ValueType.Integer ? left.AsInteger() : left.AsFloat();
        var rightVal = right.Type == ValueType.Integer ? right.AsInteger() : right.AsFloat();
        return leftVal <= rightVal;
    }
    
    private bool IsEqual(RuntimeValue left, RuntimeValue right)
    {
        if (left.Type != right.Type) return false;
        if (left.Type == ValueType.Null) return true;
        return left.Value?.Equals(right.Value) ?? false;
    }
    
    private async Task<RuntimeValue> EvaluateUnaryAsync(UnaryExpression expr)
    {
        switch (expr.Operator)
        {
            case TokenType.Minus:
                var right = await EvaluateAsync(expr.Right);
                if (right.Type == ValueType.Object)
                {
                    var overloadedResult = await TryUnaryOperatorOverload(expr.Operator, right);
                    if (overloadedResult != null)
                        return overloadedResult;
                }
                return NegateValue(right, expr);
            case TokenType.Not:
                right = await EvaluateAsync(expr.Right);
                return RuntimeValue.Boolean(!right.IsTruthy());
            case TokenType.Increment:
            case TokenType.Decrement:
                return await EvaluatePrefixIncrementDecrementAsync(expr.Operator, expr.Right, expr.Line);
            default:
                throw new RuntimeException($"Unknown unary operator: {expr.Operator}", expr.Line, _currentFile);
        }
    }

    private RuntimeValue NegateValue(RuntimeValue right, UnaryExpression expr)
    {
        CheckNumberOperand(right);
        if (right.Type == ValueType.Integer)
            return IntegerResult(-(long)right.AsInteger(), expr.Line);
        return RuntimeValue.Float(-right.AsFloat());
    }

    private static string? GetUnaryOperatorMethodName(TokenType op)
    {
        return op switch
        {
            TokenType.Minus => "__neg__",
            _ => null
        };
    }

    private async Task<RuntimeValue?> TryUnaryOperatorOverload(TokenType op, RuntimeValue operand)
    {
        var methodName = GetUnaryOperatorMethodName(op);
        if (methodName == null || operand.Type != ValueType.Object)
            return null;

        var receiver = operand.AsObject();
        if (!receiver.TryGet(methodName, out var method))
            return null;

        if (method == null || method.Type != ValueType.Function)
            return null;

        return await CallFunctionAsync(method.AsFunction(), new List<RuntimeValue>(), receiver);
    }
}
