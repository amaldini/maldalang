// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

/// <summary>
/// Global str object: str.split, str.upper, etc.
/// </summary>
public sealed class StrInstance : StdLibModuleInstance
{
    protected override IReadOnlySet<string> ExportedMethods => StdLibNamespaces.StrMethodNames;
}
