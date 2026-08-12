// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Statements;
using Environment = MaldaLang.Interpreter.Environment;

public sealed class ModuleLoadResult
{
    public Environment Environment { get; }
    public HashSet<string>? ExplicitExports { get; }
    public IReadOnlyList<Statement> Statements { get; }

    public ModuleLoadResult(
        Environment environment,
        HashSet<string>? explicitExports,
        IReadOnlyList<Statement>? statements = null)
    {
        Environment = environment;
        ExplicitExports = explicitExports;
        Statements = statements ?? System.Array.Empty<Statement>();
    }
}
