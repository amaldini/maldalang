// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections;
using System.Reflection;
using MaldaLang.Interpreter;

/// <summary>
/// Wrapper around an arbitrary CLR object so MALDA can call its methods and access properties.
/// </summary>
public class DotNetObjectInstance : ObjectInstance
{
    public object Target { get; }
    public Type TargetType { get; }

    public DotNetObjectInstance(object target) : base(null)
    {
        Target = target;
        TargetType = target.GetType();
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        return DotNetInteropHelpers.CallDotNetMethod(
            target: Target,
            targetType: TargetType,
            methodName: methodName,
            args: args,
            isStatic: false
        );
    }

    public RuntimeValue GetProperty(string name)
    {
        var prop = TargetType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (prop == null)
            throw new Exception($"Property '{name}' not found on {TargetType.FullName}.");

        var value = prop.GetValue(Target);
        return DotNetInteropHelpers.ConvertFromClr(value);
    }

    public void SetProperty(string name, RuntimeValue value)
    {
        var prop = TargetType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (prop == null || !prop.CanWrite)
            throw new Exception($"Writable property '{name}' not found on {TargetType.FullName}.");

        var clrValue = DotNetInteropHelpers.ConvertToClr(value, prop.PropertyType);
        prop.SetValue(Target, clrValue);
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // First, try to get it as a property
        var prop = TargetType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (prop != null)
        {
            var value = prop.GetValue(Target);
            return DotNetInteropHelpers.ConvertFromClr(value);
        }
        
        // Then, check if it's a method
        var methods = TargetType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        if (methods.Count > 0)
        {
            // Create a wrapper function that will be called by CallBuiltInMethod
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Property or method '{name}' not found on {TargetType.FullName}.");
    }
    
    public override void Set(string name, RuntimeValue value)
    {
        var prop = TargetType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (prop == null || !prop.CanWrite)
            throw new Exception($"Writable property '{name}' not found on {TargetType.FullName}.");

        var clrValue = DotNetInteropHelpers.ConvertToClr(value, prop.PropertyType);
        prop.SetValue(Target, clrValue);
    }

    public override string ToString()
    {
        return $"<dotnet {TargetType.FullName}>";
    }
}

/// <summary>
/// Shared helpers for converting between MALDA RuntimeValue and CLR types and invoking methods.
/// </summary>
public static class DotNetInteropHelpers
{
    public static RuntimeValue CallDotNetMethod(object? target, Type targetType, string methodName, List<RuntimeValue> args, bool isStatic)
    {
        var flags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        var methods = targetType
            .GetMethods(flags)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (methods.Count == 0)
            throw new Exception($"Method '{methodName}' not found on {targetType.FullName}.");

        // Basic overload resolution: match by parameter count, then by simple assignability
        MethodInfo? chosen = null;
        object?[]? convertedArgs = null;

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (parameters.Length != args.Count)
                continue;

            try
            {
                var tempArgs = new object?[args.Count];
                for (int i = 0; i < args.Count; i++)
                {
                    tempArgs[i] = ConvertToClr(args[i], parameters[i].ParameterType);
                }

                chosen = method;
                convertedArgs = tempArgs;
                break;
            }
            catch
            {
                // This overload doesn't match, try next
                continue;
            }
        }

        // Fallback: first method with matching count, best-effort argument conversion
        if (chosen == null)
        {
            var fallback = methods.FirstOrDefault(m => m.GetParameters().Length == args.Count);
            if (fallback == null)
                throw new Exception($"No suitable overload of '{methodName}' found on {targetType.FullName}.");

            var parameters = fallback.GetParameters();
            convertedArgs = new object?[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                convertedArgs[i] = ConvertToClr(args[i], parameters[i].ParameterType);
            }
            chosen = fallback;
        }

        try
        {
            var result = chosen.Invoke(target, convertedArgs);
            return ConvertFromClr(result);
        }
        catch (TargetInvocationException tie)
        {
            // Unwrap inner exception for clearer error messages in MALDA
            throw new Exception(tie.InnerException?.Message ?? tie.Message);
        }
    }

    public static object? ConvertToClr(RuntimeValue value, Type? targetType)
    {
        switch (value.Type)
        {
            case ValueType.Integer:
                {
                    var i = value.AsInteger();
                    if (targetType == typeof(long) || targetType == typeof(long?)) return (long)i;
                    if (targetType == typeof(short) || targetType == typeof(short?)) return (short)i;
                    if (targetType == typeof(byte) || targetType == typeof(byte?)) return (byte)i;
                    if (targetType == typeof(uint) || targetType == typeof(uint?)) return (uint)i;
                    if (targetType == typeof(ushort) || targetType == typeof(ushort?)) return (ushort)i;
                    if (targetType == typeof(ulong) || targetType == typeof(ulong?)) return (ulong)i;
                    if (targetType == typeof(double) || targetType == typeof(double?)) return (double)i;
                    if (targetType == typeof(float) || targetType == typeof(float?)) return (float)i;
                    return i;
                }
            case ValueType.Float:
                {
                    var d = value.AsFloat();
                    if (targetType == typeof(float) || targetType == typeof(float?)) return (float)d;
                    if (targetType == typeof(decimal) || targetType == typeof(decimal?)) return (decimal)d;
                    return d;
                }
            case ValueType.Boolean:
                return value.AsBoolean();
            case ValueType.String:
                return value.AsString();
            case ValueType.Array:
                {
                    var list = value.AsArray();
                    // If target is an array type, convert to that element type
                    if (targetType != null && targetType.IsArray)
                    {
                        var elemType = targetType.GetElementType() ?? typeof(object);
                        var arr = Array.CreateInstance(elemType, list.Count);
                        for (int i = 0; i < list.Count; i++)
                        {
                            var elem = ConvertToClr(list[i], elemType);
                            arr.SetValue(elem, i);
                        }
                        return arr;
                    }

                    // Fallback to List<object?> so transpiled runtime helpers can
                    // keep using MALDA-style array operations on callback payloads.
                    var objects = new List<object?>(list.Count);
                    for (int i = 0; i < list.Count; i++)
                    {
                        objects.Add(ConvertNestedRuntimeValueToClr(list[i]));
                    }
                    return objects;
                }
            case ValueType.Object:
                {
                    var obj = value.AsObject();
                    if (targetType != null &&
                        targetType != typeof(object) &&
                        targetType.IsAssignableFrom(obj.GetType()))
                    {
                        return obj;
                    }
                    if (obj is DotNetObjectInstance dotNetObj)
                    {
                        return dotNetObj.Target;
                    }
                    if (obj is DictionaryInstance dictionary)
                    {
                        return dictionary.GetEntries().ToDictionary(
                            entry => entry.Key,
                            entry => ConvertNestedRuntimeValueToClr(entry.Value));
                    }
                    if (obj is ArrayInstance array)
                    {
                        return array.Elements.Select(ConvertNestedRuntimeValueToClr).ToList();
                    }
                    // For non-dotnet objects, just pass the wrapper through
                    return obj;
                }
            case ValueType.Null:
                return null;
            default:
                // For Function/Class or unknown, just pass the runtime value itself
                return value;
        }
    }

    public static RuntimeValue ConvertFromClr(object? result)
    {
        if (result == null)
            return RuntimeValue.Null();

        if (result is RuntimeValue runtimeValue)
            return runtimeValue;

        if (result is ObjectInstance objectInstance)
            return RuntimeValue.Object(objectInstance);

        if (result is IDictionary<string, object?> dictionary)
        {
            return RuntimeValue.Object(new DictionaryInstance(dictionary.ToDictionary(
                entry => entry.Key,
                entry => ConvertFromClr(entry.Value))));
        }

        switch (result)
        {
            case int i:
                return RuntimeValue.Integer(i);
            case long l:
                return RuntimeValue.Integer((int)l);
            case short s:
                return RuntimeValue.Integer(s);
            case byte b:
                return RuntimeValue.Integer(b);
            case double d:
                return RuntimeValue.Float(d);
            case float f:
                return RuntimeValue.Float(f);
            case decimal dec:
                return RuntimeValue.Float((double)dec);
            case bool bo:
                return RuntimeValue.Boolean(bo);
            case string str:
                return RuntimeValue.String(str);
        }

        // Handle IEnumerable (but not string which we already handled)
        if (result is IEnumerable enumerable && result is not string)
        {
            var list = new List<RuntimeValue>();
            foreach (var item in enumerable)
            {
                list.Add(ConvertFromClr(item));
            }
            return RuntimeValue.Array(list);
        }

        // Fallback: wrap as DotNetObjectInstance
        return RuntimeValue.Object(new DotNetObjectInstance(result));
    }

    private static object? ConvertNestedRuntimeValueToClr(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Integer => ConvertToClr(value, null),
            ValueType.Float => ConvertToClr(value, null),
            ValueType.Boolean => ConvertToClr(value, null),
            ValueType.String => ConvertToClr(value, null),
            ValueType.Null => null,
            ValueType.Array => value.AsArray().Select(ConvertNestedRuntimeValueToClr).ToList(),
            ValueType.Object => ConvertNestedObjectInstanceToClr(value.AsObject()),
            _ => ConvertToClr(value, null)
        };
    }

    private static object? ConvertNestedObjectInstanceToClr(ObjectInstance obj)
    {
        return obj switch
        {
            DictionaryInstance dictionary => dictionary.GetEntries().ToDictionary(
                entry => entry.Key,
                entry => ConvertNestedRuntimeValueToClr(entry.Value)),
            ArrayInstance array => array.Elements.Select(ConvertNestedRuntimeValueToClr).ToList(),
            _ => obj
        };
    }
}
