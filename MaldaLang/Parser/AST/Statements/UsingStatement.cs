// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

public class UsingStatement : Statement
{
    public string PackageName { get; }
    public string? SubModule { get; }
    public string? Alias { get; }
    
    public UsingStatement(string packageName, string? subModule = null, string? alias = null, int line = 0, int column = 0)
        : base(line, column)
    {
        PackageName = packageName;
        SubModule = subModule;
        Alias = alias;
    }
}
