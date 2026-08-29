// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Reflection;
using MaldaLang.Interpreter;

/// <summary>
/// Wrapper around a loaded .NET Assembly so it can be passed around in MALDA as an object.
/// </summary>
public class DotNetAssemblyInstance : ObjectInstance
{
    public Assembly Assembly { get; }

    public DotNetAssemblyInstance(Assembly assembly) : base(null)
    {
        Assembly = assembly;
    }

    public override string ToString()
    {
        return $"<assembly {Assembly.FullName}>";
    }
}
