// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MaldaLang.Interpreter;

namespace MaldaLang.Compiler;

public static class RuntimeHelpers
{
    // Type coercion
    public static object CoerceToInt(object? value)
    {
        if (value == null) return 0;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s => int.TryParse(s, out var result) ? result : 0,
            bool b => b ? 1 : 0,
            _ => throw new InvalidOperationException($"Cannot coerce {value.GetType()} to int")
        };
    }

    public static object CoerceToFloat(object? value)
    {
        if (value == null) return 0.0;
        return value switch
        {
            int i => (double)i,
            long l => (double)l,
            double d => d,
            float f => (double)f,
            string s => double.TryParse(s, out var result) ? result : 0.0,
            bool b => b ? 1.0 : 0.0,
            _ => throw new InvalidOperationException($"Cannot coerce {value.GetType()} to float")
        };
    }

    /// <summary>Checked integer arithmetic for transpiled code; throws RuntimeException on overflow.</summary>
    public static int CheckedIntAdd(int a, int b)
    {
        try { return checked(a + b); }
        catch (OverflowException) { throw new RuntimeException("Integer overflow."); }
    }
    public static int CheckedIntSubtract(int a, int b)
    {
        try { return checked(a - b); }
        catch (OverflowException) { throw new RuntimeException("Integer overflow."); }
    }
    public static int CheckedIntMultiply(int a, int b)
    {
        try { return checked(a * b); }
        catch (OverflowException) { throw new RuntimeException("Integer overflow."); }
    }
    public static int CheckedIntMod(int a, int b)
    {
        try { return checked(a % b); }
        catch (OverflowException) { throw new RuntimeException("Integer overflow."); }
    }
    public static int CheckedIntNegate(int a)
    {
        try { return checked(-a); }
        catch (OverflowException) { throw new RuntimeException("Integer overflow."); }
    }
    public static int CheckedIntIncrement(int a)
    {
        try { return checked(a + 1); }
        catch (OverflowException) { throw new RuntimeException("Integer overflow."); }
    }
    public static int CheckedIntDecrement(int a)
    {
        try { return checked(a - 1); }
        catch (OverflowException) { throw new RuntimeException("Integer overflow."); }
    }

    private static bool IsPrimitiveForOperatorOverload(object? value)
    {
        return value is null or int or long or double or float or string or bool;
    }

    private static object? ConvertOperatorArgument(object? value, Type parameterType)
    {
        if (parameterType == typeof(object))
            return value;

        if (parameterType == typeof(RuntimeValue))
            return ToRuntimeValue(value);

        if (value == null)
            return null;

        if (parameterType.IsInstanceOfType(value))
            return value;

        return value;
    }

    private static bool TryInvokeOperatorBinary(object? left, object? right, string methodName, out object? result)
    {
        var receiver = UnwrapRuntimeValue(left);
        var argument = UnwrapRuntimeValue(right);

        if (receiver == null || IsPrimitiveForOperatorOverload(receiver))
        {
            result = null;
            return false;
        }

        var receiverType = receiver.GetType();
        var method = receiverType.GetMethod(methodName, new[] { typeof(object) });

        if (method == null)
        {
            method = receiverType
                .GetMethods()
                .FirstOrDefault(m =>
                {
                    if (m.Name != methodName) return false;
                    var parameters = m.GetParameters();
                    return parameters.Length == 1 && (parameters[0].ParameterType == typeof(object) || parameters[0].ParameterType == typeof(RuntimeValue));
                });
        }

        if (method == null)
        {
            result = null;
            return false;
        }

        var parameterType = method.GetParameters()[0].ParameterType;
        var convertedArgument = ConvertOperatorArgument(argument, parameterType);
        try
        {
            result = method.Invoke(receiver, new[] { convertedArgument });
            return true;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    private static bool TryInvokeOperatorBinaryReversed(object? left, object? right, string methodName, out object? result)
    {
        var receiver = UnwrapRuntimeValue(right);
        var argument = UnwrapRuntimeValue(left);

        if (receiver == null || IsPrimitiveForOperatorOverload(receiver))
        {
            result = null;
            return false;
        }

        var receiverType = receiver.GetType();
        var method = receiverType.GetMethod(methodName, new[] { typeof(object) });

        if (method == null)
        {
            method = receiverType
                .GetMethods()
                .FirstOrDefault(m =>
                {
                    if (m.Name != methodName) return false;
                    var parameters = m.GetParameters();
                    return parameters.Length == 1 && (parameters[0].ParameterType == typeof(object) || parameters[0].ParameterType == typeof(RuntimeValue));
                });
        }

        if (method == null)
        {
            result = null;
            return false;
        }

        var parameterType = method.GetParameters()[0].ParameterType;
        var convertedArgument = ConvertOperatorArgument(argument, parameterType);
        try
        {
            result = method.Invoke(receiver, new[] { convertedArgument });
            return true;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    private static bool TryInvokeOperatorUnary(object? operand, string methodName, out object? result)
    {
        var receiver = UnwrapRuntimeValue(operand);
        if (receiver == null || IsPrimitiveForOperatorOverload(receiver))
        {
            result = null;
            return false;
        }

        var method = receiver.GetType().GetMethod(methodName, Type.EmptyTypes);
        if (method == null)
        {
            result = null;
            return false;
        }

        try
        {
            result = method.Invoke(receiver, Array.Empty<object>());
            return true;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    public static object OperatorAdd(object? left, object? right)
    {
        if (TryInvokeOperatorBinary(left, right, "__add__", out var overloaded))
            return overloaded!;
        if (TryInvokeOperatorBinaryReversed(left, right, "__radd__", out overloaded))
            return overloaded!;

        var l = UnwrapRuntimeValue(left);
        var r = UnwrapRuntimeValue(right);
        if (IsString(l) || IsString(r))
            return CoerceToString(l) + CoerceToString(r);
        if (IsInt(l) && IsInt(r))
            return CheckedIntAdd((int)CoerceToInt(l), (int)CoerceToInt(r));
        if (IsNumber(l) && IsNumber(r))
            return (double)CoerceToFloat(l) + (double)CoerceToFloat(r);
        throw new InvalidOperationException("Operands must be numbers or strings.");
    }

    public static object OperatorSubtract(object? left, object? right)
    {
        if (TryInvokeOperatorBinary(left, right, "__sub__", out var overloaded))
            return overloaded!;
        if (TryInvokeOperatorBinaryReversed(left, right, "__rsub__", out overloaded))
            return overloaded!;

        var l = UnwrapRuntimeValue(left);
        var r = UnwrapRuntimeValue(right);
        CheckNumberOperands(l, r);
        if (IsInt(l) && IsInt(r))
            return CheckedIntSubtract((int)CoerceToInt(l), (int)CoerceToInt(r));
        return (double)CoerceToFloat(l) - (double)CoerceToFloat(r);
    }

    public static object OperatorMultiply(object? left, object? right)
    {
        if (TryInvokeOperatorBinary(left, right, "__mul__", out var overloaded))
            return overloaded!;
        if (TryInvokeOperatorBinaryReversed(left, right, "__rmul__", out overloaded))
            return overloaded!;

        var l = UnwrapRuntimeValue(left);
        var r = UnwrapRuntimeValue(right);
        if (IsString(l) && (IsInt(r) || IsFloat(r)))
        {
            var count = (int)CoerceToInt(r);
            return count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(CoerceToString(l), count));
        }
        if ((IsInt(l) || IsFloat(l)) && IsString(r))
        {
            var count = (int)CoerceToInt(l);
            return count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(CoerceToString(r), count));
        }
        CheckNumberOperands(l, r);
        if (IsInt(l) && IsInt(r))
            return CheckedIntMultiply((int)CoerceToInt(l), (int)CoerceToInt(r));
        return (double)CoerceToFloat(l) * (double)CoerceToFloat(r);
    }

    public static object OperatorDivide(object? left, object? right)
    {
        if (TryInvokeOperatorBinary(left, right, "__div__", out var overloaded))
            return overloaded!;
        if (TryInvokeOperatorBinaryReversed(left, right, "__rdiv__", out overloaded))
            return overloaded!;

        var l = UnwrapRuntimeValue(left);
        var r = UnwrapRuntimeValue(right);
        CheckNumberOperands(l, r);
        var divisor = (double)CoerceToFloat(r);
        if (divisor == 0)
            throw new RuntimeException("Division by zero.");
        return (double)CoerceToFloat(l) / divisor;
    }

    public static object OperatorModulo(object? left, object? right)
    {
        if (TryInvokeOperatorBinary(left, right, "__mod__", out var overloaded))
            return overloaded!;
        if (TryInvokeOperatorBinaryReversed(left, right, "__rmod__", out overloaded))
            return overloaded!;

        var l = UnwrapRuntimeValue(left);
        var r = UnwrapRuntimeValue(right);
        CheckNumberOperands(l, r);
        if (IsInt(l) && IsInt(r))
        {
            var divisor = (int)CoerceToInt(r);
            if (divisor == 0)
                throw new RuntimeException("Division by zero.");
            return CheckedIntMod((int)CoerceToInt(l), divisor);
        }
        var divisorFloat = (double)CoerceToFloat(r);
        if (divisorFloat == 0)
            throw new RuntimeException("Division by zero.");
        return (double)CoerceToFloat(l) % divisorFloat;
    }

    public static object OperatorNegate(object? operand)
    {
        if (TryInvokeOperatorUnary(operand, "__neg__", out var overloaded))
            return overloaded!;

        var value = UnwrapRuntimeValue(operand);
        if (IsInt(value))
            return CheckedIntNegate((int)CoerceToInt(value));
        if (IsFloat(value))
            return -(double)CoerceToFloat(value);
        throw new InvalidOperationException("Operand must be a number.");
    }

    public static bool OperatorEqual(object? left, object? right)
    {
        if (TryInvokeOperatorBinary(left, right, "__eq__", out var overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));
        if (TryInvokeOperatorBinaryReversed(left, right, "__req__", out overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));

        var l = UnwrapRuntimeValue(left);
        var r = UnwrapRuntimeValue(right);
        if (IsString(l) && IsString(r))
            return CoerceToString(l) == CoerceToString(r);
        return object.Equals(l, r);
    }

    public static bool OperatorNotEqual(object? left, object? right)
    {
        if (TryInvokeOperatorBinary(left, right, "__neq__", out var overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));
        if (TryInvokeOperatorBinaryReversed(left, right, "__rneq__", out overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));

        return !OperatorEqual(left, right);
    }

    public static bool OperatorLessThan(object? left, object? right)
    {
        if (TryInvokeOperatorBinary(left, right, "__lt__", out var overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));
        if (TryInvokeOperatorBinaryReversed(left, right, "__rlt__", out overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));

        var l = UnwrapRuntimeValue(left);
        var r = UnwrapRuntimeValue(right);
        if (IsString(l) && IsString(r))
            return string.Compare(CoerceToString(l), CoerceToString(r), StringComparison.Ordinal) < 0;
        if (IsNumber(l) && IsNumber(r))
            return (double)CoerceToFloat(l) < (double)CoerceToFloat(r);
        throw new InvalidOperationException("Operands must be both strings or both numbers.");
    }

    public static bool OperatorLessThanOrEqual(object? left, object? right)
    {
        if (TryInvokeOperatorBinary(left, right, "__le__", out var overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));
        if (TryInvokeOperatorBinaryReversed(left, right, "__rle__", out overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));

        var l = UnwrapRuntimeValue(left);
        var r = UnwrapRuntimeValue(right);
        if (IsString(l) && IsString(r))
            return string.Compare(CoerceToString(l), CoerceToString(r), StringComparison.Ordinal) <= 0;
        if (IsNumber(l) && IsNumber(r))
            return (double)CoerceToFloat(l) <= (double)CoerceToFloat(r);
        throw new InvalidOperationException("Operands must be both strings or both numbers.");
    }

    public static bool OperatorGreaterThan(object? left, object? right)
    {
        if (TryInvokeOperatorBinary(left, right, "__gt__", out var overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));
        if (TryInvokeOperatorBinaryReversed(left, right, "__rgt__", out overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));

        var l = UnwrapRuntimeValue(left);
        var r = UnwrapRuntimeValue(right);
        if (IsString(l) && IsString(r))
            return string.Compare(CoerceToString(l), CoerceToString(r), StringComparison.Ordinal) > 0;
        if (IsNumber(l) && IsNumber(r))
            return (double)CoerceToFloat(l) > (double)CoerceToFloat(r);
        throw new InvalidOperationException("Operands must be both strings or both numbers.");
    }

    public static bool OperatorGreaterThanOrEqual(object? left, object? right)
    {
        if (TryInvokeOperatorBinary(left, right, "__ge__", out var overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));
        if (TryInvokeOperatorBinaryReversed(left, right, "__rge__", out overloaded))
            return CoerceToBool(UnwrapRuntimeValue(overloaded));

        var l = UnwrapRuntimeValue(left);
        var r = UnwrapRuntimeValue(right);
        if (IsString(l) && IsString(r))
            return string.Compare(CoerceToString(l), CoerceToString(r), StringComparison.Ordinal) >= 0;
        if (IsNumber(l) && IsNumber(r))
            return (double)CoerceToFloat(l) >= (double)CoerceToFloat(r);
        throw new InvalidOperationException("Operands must be both strings or both numbers.");
    }

    public static string CoerceToString(object? value)
    {
        if (value == null) return "null";
        
        // Handle RuntimeValue types
        if (value is RuntimeValue rv)
        {
            return rv.Type switch
            {
                MaldaLang.Interpreter.ValueType.String => rv.AsString(),
                MaldaLang.Interpreter.ValueType.Integer => rv.AsInteger().ToString(),
                MaldaLang.Interpreter.ValueType.Float => rv.AsFloat().ToString(System.Globalization.CultureInfo.InvariantCulture),
                MaldaLang.Interpreter.ValueType.Boolean => rv.AsBoolean().ToString().ToLower(),
                MaldaLang.Interpreter.ValueType.Null => "null",
                MaldaLang.Interpreter.ValueType.Array => FormatArrayFromRuntimeValue(rv.AsArray()),
                MaldaLang.Interpreter.ValueType.Object => rv.AsObject().ToString(),
                MaldaLang.Interpreter.ValueType.Function => "<function>",
                MaldaLang.Interpreter.ValueType.Class => "<class>",
                _ => rv.ToString() ?? "null"
            };
        }
        
        // Handle List<object> (arrays from compiled code)
        if (value is List<object> list)
        {
            return FormatArray(list);
        }
        
        // Handle List<RuntimeValue> (arrays from interpreter)
        if (value is List<RuntimeValue> runtimeValueList)
        {
            return FormatArrayFromRuntimeValue(runtimeValueList);
        }
        
        // Handle raw C# bool values (from CallObjectMethod, etc.)
        if (value is bool boolValue)
        {
            return boolValue.ToString().ToLower();
        }
        
        return value.ToString() ?? "null";
    }
    
    private static string FormatArray(List<object> array)
    {
        var elements = new List<string>();
        foreach (var item in array)
        {
            elements.Add(CoerceToString(item));
        }
        return "[" + string.Join(", ", elements) + "]";
    }
    
    private static string FormatArrayFromRuntimeValue(List<RuntimeValue> array)
    {
        var elements = new List<string>();
        foreach (var item in array)
        {
            elements.Add(CoerceToString(item));
        }
        return "[" + string.Join(", ", elements) + "]";
    }

    public static bool CoerceToBool(object? value)
    {
        if (value == null) return false;
        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            double d => d != 0.0,
            float f => f != 0.0f,
            string s => !string.IsNullOrEmpty(s),
            List<object> list => list.Count > 0,
            _ => true
        };
    }

    /// <summary>True for C# null or a MALDA <see cref="RuntimeValue"/> tagged Null.</summary>
    public static bool IsMaldaNull(object? value)
    {
        if (value == null)
            return true;
        return value is RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Null;
    }

    /// <summary>Null-coalescing with a lazy right side (short-circuit).</summary>
    public static object? NullCoalesce(object? left, Func<object?> rightFactory)
    {
        return IsMaldaNull(left) ? rightFactory() : left;
    }

    // Type checking
    public static bool IsInt(object? value)
    {
        return value is int or long;
    }

    public static bool IsFloat(object? value)
    {
        return value is double or float;
    }

    public static bool IsString(object? value)
    {
        return value is string;
    }

    public static bool IsNumber(object? value)
    {
        return value is int or long or double or float;
    }

    public static void CheckNumberOperands(object? left, object? right)
    {
        if (!IsNumber(left) || !IsNumber(right))
            throw new InvalidOperationException("Operands must be numbers.");
    }

    public static bool IsArray(object? value)
    {
        return value is List<object> || value is List<RuntimeValue> || value is RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Array;
    }

    public static object? UnwrapRuntimeValue(object? value)
    {
        if (value is RuntimeValue rv)
        {
            return rv.Type switch
            {
                MaldaLang.Interpreter.ValueType.Integer => rv.AsInteger(),
                MaldaLang.Interpreter.ValueType.Float => rv.AsFloat(),
                MaldaLang.Interpreter.ValueType.String => rv.AsString(),
                MaldaLang.Interpreter.ValueType.Boolean => rv.AsBoolean(),
                // Stable List<object> so append / index-assign survive round-trips via ToRuntimeValue.
                MaldaLang.Interpreter.ValueType.Array => GetArray(rv.AsArray()),
                MaldaLang.Interpreter.ValueType.Object => rv.AsObject(),
                MaldaLang.Interpreter.ValueType.Variant => rv,
                MaldaLang.Interpreter.ValueType.Task => rv,
                MaldaLang.Interpreter.ValueType.Function => rv,
                MaldaLang.Interpreter.ValueType.Null => null,
                _ => rv
            };
        }
        return value;
    }

    /// <summary>Unwrap a RuntimeValue holding a Task and return the result as object. Used for MALDA await.</summary>
    public static async System.Threading.Tasks.Task<object?> UnwrapTaskAsync(object? value)
    {
        if (value is RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Task)
            return UnwrapRuntimeValue(await rv.AsTask());
        throw new InvalidOperationException("await requires a task value.");
    }

    // One stable List<object> per List<RuntimeValue> identity (matches generated RuntimeHelpers).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<List<RuntimeValue>, List<object>> RvListToObjectListCache = new();

    // Array and dictionary operations
    public static List<object> GetArray(object? value)
    {
        // Avoid UnwrapRuntimeValue here: for Array values it calls GetArray (bridge).
        if (value is RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Array)
            value = rv.AsArray();

        var unwrapped = value is RuntimeValue other ? UnwrapRuntimeValue(other) : value;
        if (unwrapped is List<object> list)
            return list;
        if (unwrapped is List<RuntimeValue> runtimeValueList)
        {
            return RvListToObjectListCache.GetValue(runtimeValueList, static source =>
            {
                var result = new List<object>(source.Count);
                foreach (var item in source)
                {
                    result.Add(item.Type switch
                    {
                        MaldaLang.Interpreter.ValueType.Integer => item.AsInteger(),
                        MaldaLang.Interpreter.ValueType.Float => item.AsFloat(),
                        MaldaLang.Interpreter.ValueType.String => item.AsString(),
                        MaldaLang.Interpreter.ValueType.Boolean => item.AsBoolean(),
                        MaldaLang.Interpreter.ValueType.Array => GetArray(item.AsArray()),
                        MaldaLang.Interpreter.ValueType.Object => item.AsObject(),
                        _ => null
                    });
                }
                return result;
            });
        }
        throw new InvalidOperationException($"Value is not an array: {value?.GetType()}");
    }

    public static object ArrayAppend(List<object> arr, object? item)
    {
        arr.Add(item ?? new object());
        return arr;
    }

    public static object ArrayPop(List<object> arr)
    {
        if (arr.Count == 0)
            throw new InvalidOperationException("Cannot pop from empty array");
        var lastIndex = arr.Count - 1;
        var last = arr[lastIndex];
        arr.RemoveAt(lastIndex);
        return last;
    }

    public static object ArrayShift(List<object> arr)
    {
        if (arr.Count == 0)
            throw new InvalidOperationException("Cannot shift from empty array");
        var first = arr[0];
        arr.RemoveAt(0);
        return first;
    }

    public static List<object> ArrayConcat(List<object> arr1, List<object> arr2)
    {
        var combined = new List<object>(arr1.Count + arr2.Count);
        combined.AddRange(arr1);
        combined.AddRange(arr2);
        return combined;
    }

    public static Dictionary<string, object?> GetDictionary(object? value)
    {
        // First unwrap RuntimeValue if needed
        var unwrapped = UnwrapRuntimeValue(value);
        if (unwrapped is Dictionary<string, object?> dict)
            return dict;
        if (unwrapped is MaldaLang.Interpreter.DictionaryInstance dictInstance)
        {
            // Convert to Dictionary<string, object?> using RuntimeValue conversion
            var result = new Dictionary<string, object?>();
            foreach (var kvp in dictInstance.GetEntries())
            {
                result[kvp.Key] = UnwrapRuntimeValue(kvp.Value);
            }
            return result;
        }
        throw new InvalidOperationException($"Value is not a dictionary: {value?.GetType()}");
    }

    public static object? DictionaryGet(object? dictValue, object? keyValue)
    {
        var dict = GetDictionary(dictValue);
        var key = CoerceToString(keyValue);
        return dict.TryGetValue(key, out var result) ? result : null;
    }

    public static void DictionarySet(object? dictValue, object? keyValue, object? newValue)
    {
        var dict = GetDictionary(dictValue);
        var key = CoerceToString(keyValue);
        dict[key] = newValue!;
    }

    public static object? GetIndexed(object? target, object? index)
    {
        var value = UnwrapRuntimeValue(target);

        // Arrays: integer index
        if (IsArray(value))
        {
            var arr = GetArray(value);
            int i = (int)CoerceToInt(index);
            if (i < 0 || i >= arr.Count)
                throw new InvalidOperationException("Array index out of bounds.");
            return arr[i];
        }

        // Dictionaries: string key
        if (value is Dictionary<string, object?> or MaldaLang.Interpreter.DictionaryInstance)
        {
            return DictionaryGet(value, index);
        }

        // ObjectInstance (e.g. JsonObject returned by getSymbols, getParseErrors)
        if (value is MaldaLang.Interpreter.ObjectInstance objInstance)
        {
            var key = CoerceToString(index);
            var rv = objInstance.Get(key, null);
            return UnwrapRuntimeValue(rv);
        }

        throw new InvalidOperationException("Only arrays and dictionaries can be indexed in compiled code.");
    }

    public static void SetIndexed(object? target, object? index, object? newValue)
    {
        var value = UnwrapRuntimeValue(target);

        // Arrays: integer index
        if (IsArray(value))
        {
            var arr = GetArray(value);
            int i = (int)CoerceToInt(index);
            if (i < 0 || i >= arr.Count)
                throw new InvalidOperationException("Array index out of bounds.");
            arr[i] = newValue!;
            return;
        }

        // Dictionaries: string key
        if (value is Dictionary<string, object?> or MaldaLang.Interpreter.DictionaryInstance)
        {
            DictionarySet(value, index, newValue);
            return;
        }

        // ObjectInstance (e.g. JsonObject)
        if (value is MaldaLang.Interpreter.ObjectInstance objInstance)
        {
            var key = CoerceToString(index);
            objInstance.Set(key, ToRuntimeValue(newValue));
            return;
        }

        throw new InvalidOperationException("Only arrays and dictionaries can be indexed in compiled code.");
    }

    public static GraphInstance GetGraph(object? value)
    {
        var unwrapped = UnwrapRuntimeValue(value);
        if (unwrapped is GraphInstance graph)
            return graph;
        if (unwrapped is RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Object && rv.Value is GraphInstance graphInstance)
            return graphInstance;
        throw new InvalidOperationException($"Expected GraphInstance, got {unwrapped?.GetType()}");
    }

    public static object? CallGraphMethod(object? graphValue, string methodName, object?[] args)
    {
        var graph = GetGraph(graphValue);
        var runtimeArgs = new List<RuntimeValue>();
        foreach (var arg in args)
        {
            runtimeArgs.Add(ToRuntimeValue(arg));
        }
        var result = graph.CallMethod(methodName, runtimeArgs, null);
        return FromRuntimeValue(result);
    }

    public static object? GetObjectProperty(object? objValue, string propertyName)
    {
        var unwrapped = UnwrapRuntimeValue(objValue);
        if (unwrapped is RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Object)
        {
            var obj = rv.AsObject();
            try
            {
                var propValue = obj.Get(propertyName, null);
                return FromRuntimeValue(propValue);
            }
            catch
            {
                return null;
            }
        }
        if (unwrapped is Dictionary<string, object?> dict)
        {
            return dict.TryGetValue(propertyName, out var value) ? value : null;
        }
        return null;
    }

    public static object? GetObject(object? value)
    {
        var unwrapped = UnwrapRuntimeValue(value);
        if (unwrapped is RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Object)
            return rv.Value;
        return unwrapped;
    }

    public static RuntimeValue ToRuntimeValue(object? value)
    {
        if (value is RuntimeValue rv)
            return rv;
        if (value is List<RuntimeValue> rvList)
        {
            if (RvListToObjectListCache.TryGetValue(rvList, out var bridged))
                return RuntimeValue.Array(bridged.Select(ToRuntimeValue).ToList());
            return RuntimeValue.Array(rvList.ToList());
        }

        return value switch
        {
            int i => RuntimeValue.Integer(i),
            long l => RuntimeValue.Integer((int)l),
            double d => RuntimeValue.Float(d),
            float f => RuntimeValue.Float(f),
            string s => RuntimeValue.String(s),
            bool b => RuntimeValue.Boolean(b),
            List<object> list => RuntimeValue.Array(list.Select(v => ToRuntimeValue(v)).ToList()),
            Dictionary<string, object?> dict => RuntimeValue.Object(
                new MaldaLang.Interpreter.DictionaryInstance(dict.ToDictionary(kvp => kvp.Key, kvp => ToRuntimeValue(kvp.Value)))),
            Func<object, Task<object>> fn => RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue { TranspiledDelegate = fn }),
            Delegate del => WrapTranspiledDelegate(del),
            MaldaLang.Interpreter.GraphInstance gi => RuntimeValue.Object(gi),
            MaldaLang.Interpreter.ObjectInstance oi => RuntimeValue.Object(oi),
            _ => RuntimeValue.Null()
        };
    }

    public static object? FromRuntimeValue(RuntimeValue value)
    {
        return value.Type switch
        {
            MaldaLang.Interpreter.ValueType.Integer => value.AsInteger(),
            MaldaLang.Interpreter.ValueType.Float => value.AsFloat(),
            MaldaLang.Interpreter.ValueType.String => value.AsString(),
            MaldaLang.Interpreter.ValueType.Boolean => value.AsBoolean(),
            MaldaLang.Interpreter.ValueType.Array => value.AsArray(),
            MaldaLang.Interpreter.ValueType.Object => value.AsObject(),
            MaldaLang.Interpreter.ValueType.Function => value.AsFunction(),
            MaldaLang.Interpreter.ValueType.Null => null,
            _ => null
        };
    }

    /// <summary>
    /// Helper for transpiled ui.* calls that return RuntimeValue envelopes.
    /// </summary>
    public static object? UnwrapUiEnvelope(RuntimeValue value)
    {
        return FromRuntimeValue(value);
    }

    /// <summary>
    /// Helper for transpiled ui.* calls when passing object payloads back to runtime.
    /// </summary>
    public static RuntimeValue ToUiRuntimeValue(object? value)
    {
        return ToRuntimeValue(value);
    }

    private static RuntimeValue WrapTranspiledDelegate(Delegate del)
    {
        if (del is Func<object, Task<object>> typed)
            return RuntimeValue.Function(new FunctionValue { TranspiledDelegate = typed });

        return RuntimeValue.Function(new FunctionValue
        {
            TranspiledDelegate = async arg =>
            {
                var result = del.DynamicInvoke(arg);
                if (result is Task<object> taskObj)
                    return await taskObj;
                if (result is Task task)
                {
                    await task;
                    var taskType = task.GetType();
                    if (taskType.IsGenericType)
                        return taskType.GetProperty("Result")?.GetValue(task);
                }
                return result;
            }
        });
    }
}