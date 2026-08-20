// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

/// <summary>
/// Host-side statements for interpret debug of a fullstack file. Matches the
/// C# compile partition so <c>@client()</c> / <c>@javascript()</c> bodies are
/// not executed by the interpreter. Original line numbers are preserved.
/// </summary>
public static class HostDebugPartition
{
    public static List<Statement> KeepHostStatements(IReadOnlyList<Statement> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        var list = statements as List<Statement> ?? statements.ToList();
        return TargetPartitioner.Partition(list).CSharpStatements;
    }
}
