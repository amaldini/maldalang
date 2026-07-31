// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

/// <summary>
/// Global io object: io.readFile, io.print, etc.
/// </summary>
public sealed class IoInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.IoMethodNames;
}
