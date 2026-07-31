// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

public class VarDeclStatement : Statement
{
    public string Name { get; }
    public Expression Initializer { get; }
    public string? TypeHint { get; }  // Optional type hint (informational only)
    public bool IsExported { get; }
    public bool IsConst { get; }

    public VarDeclStatement(
        string name,
        Expression initializer,
        string? typeHint = null,
        bool isExported = false,
        bool isConst = false,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        Name = name;
        IsExported = isExported;
        IsConst = isConst;
        Initializer = initializer;
        TypeHint = typeHint;
    }
}