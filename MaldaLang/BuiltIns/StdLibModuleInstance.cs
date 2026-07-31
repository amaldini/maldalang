// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Object module exposing built-in functions as methods (e.g. math.sqrt).
/// </summary>
public abstract class StdLibModuleInstance : ObjectInstance
{
    protected abstract IReadOnlySet<string> ExportedMethods { get; }

    protected StdLibModuleInstance() : base(null)
    {
    }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (ExportedMethods.Contains(name))
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }

        return base.Get(name, accessingClass);
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        BuiltInFunctions.CallBuiltIn(methodName, args, interpreter);

    /// <summary>
    /// A few built-ins exist only on the async path (io.input, sleep). Without this the
    /// namespaced spelling the language server recommends would fail where the flat alias
    /// works.
    /// </summary>
    public Task<RuntimeValue> CallMethodAsync(string methodName, List<RuntimeValue> args, Interpreter interpreter) =>
        BuiltInFunctions.CallBuiltInAsync(methodName, args, interpreter);

    public static bool RequiresAsyncCall(string methodName)
    {
        var descriptor = BuiltInRegistry.GetDescriptor(methodName);
        return descriptor != null && !descriptor.SupportsSync;
    }
}
