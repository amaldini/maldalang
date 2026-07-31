// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Reflection;
using MaldaLang.Interpreter;

/// <summary>
/// Wrapper around a System.Type so MALDA code can reference .NET types.
/// Used mainly for static method calls and as input to dotnetNew.
/// </summary>
public class DotNetTypeInstance : ObjectInstance
{
    public Type Type { get; }

    public DotNetTypeInstance(Type type) : base(null)
    {
        Type = type;
    }

    public RuntimeValue CallStaticMethod(string methodName, List<RuntimeValue> args)
    {
        return DotNetInteropHelpers.CallDotNetMethod(
            target: null,
            targetType: Type,
            methodName: methodName,
            args: args,
            isStatic: true
        );
    }

    public override string ToString()
    {
        return $"<type {Type.FullName}>";
    }
}
