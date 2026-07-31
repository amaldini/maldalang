// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using MaldaLang.Interpreter;

public delegate Task<object> NativeCallbackDelegate(object arg1, object arg2, object arg3, object arg4);

/// <summary>
/// Bridges a MALDA function into a CLR callback so native modules can call back
/// into interpreted or transpiled MALDA code without knowing runtime details.
/// </summary>
public sealed class NativeCallbackBridge
{
    private readonly NativeCallbackDelegate? _transpiledCallback;
    private readonly MaldaLang.Interpreter.Interpreter? _interpreter;
    private readonly FunctionValue? _function;

    public NativeCallbackBridge(NativeCallbackDelegate callback)
    {
        _transpiledCallback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    internal NativeCallbackBridge(FunctionValue function, MaldaLang.Interpreter.Interpreter interpreter)
    {
        _function = function ?? throw new ArgumentNullException(nameof(function));
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
    }

    public object? Invoke(object? arg1, object? arg2, object? arg3, object? arg4)
    {
        if (_transpiledCallback != null)
        {
            return _transpiledCallback(arg1, arg2, arg3, arg4).GetAwaiter().GetResult();
        }

        if (_function == null || _interpreter == null)
            throw new InvalidOperationException("Native callback bridge is not initialized.");

        var result = _interpreter.CallFunctionAsync(
            _function,
            new List<RuntimeValue>
            {
                ToRuntimeValue(arg1),
                ToRuntimeValue(arg2),
                ToRuntimeValue(arg3),
                ToRuntimeValue(arg4)
            }).GetAwaiter().GetResult();

        return UnwrapRuntimeValue(result);
    }

    private static RuntimeValue ToRuntimeValue(object? value)
    {
        return value switch
        {
            null => RuntimeValue.Null(),
            RuntimeValue runtimeValue => runtimeValue,
            ObjectInstance objectInstance => RuntimeValue.Object(objectInstance),
            int i => RuntimeValue.Integer(i),
            long l => RuntimeValue.Integer((int)l),
            short s => RuntimeValue.Integer(s),
            byte b => RuntimeValue.Integer(b),
            double d => RuntimeValue.Float(d),
            float f => RuntimeValue.Float(f),
            decimal dec => RuntimeValue.Float((double)dec),
            bool bo => RuntimeValue.Boolean(bo),
            string str => RuntimeValue.String(str),
            IDictionary<string, object?> dict => RuntimeValue.Object(new DictionaryInstance(dict.ToDictionary(
                entry => entry.Key,
                entry => ToRuntimeValue(entry.Value)))),
            IEnumerable enumerable when value is not string => RuntimeValue.Array(ToRuntimeArray(enumerable)),
            _ => RuntimeValue.Object(new DotNetObjectInstance(value))
        };
    }

    private static List<RuntimeValue> ToRuntimeArray(IEnumerable enumerable)
    {
        var items = new List<RuntimeValue>();
        foreach (var item in enumerable)
        {
            items.Add(ToRuntimeValue(item));
        }
        return items;
    }

    private static object? UnwrapRuntimeValue(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Null => null,
            ValueType.Integer => value.AsInteger(),
            ValueType.Float => value.AsFloat(),
            ValueType.Boolean => value.AsBoolean(),
            ValueType.String => value.AsString(),
            ValueType.Array => value.AsArray().Select(UnwrapRuntimeValue).ToList(),
            ValueType.Object => UnwrapObject(value.AsObject()),
            _ => value
        };
    }

    private static object? UnwrapObject(ObjectInstance obj)
    {
        return obj switch
        {
            DotNetObjectInstance dotNetObject => dotNetObject.Target,
            DictionaryInstance dictionary => dictionary.GetEntries().ToDictionary(
                entry => entry.Key,
                entry => UnwrapRuntimeValue(entry.Value)),
            ArrayInstance array => array.Elements.Select(UnwrapRuntimeValue).ToList(),
            _ => obj
        };
    }
}
