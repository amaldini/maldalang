// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

public class DestructuringVarDecl : Statement
{
    public DestructuringPattern Pattern { get; }
    public Expression Initializer { get; }
    public string? TypeHint { get; }
    
    public DestructuringVarDecl(DestructuringPattern pattern, Expression initializer, string? typeHint = null, int line = 0, int column = 0)
        : base(line, column)
    {
        Pattern = pattern;
        Initializer = initializer;
        TypeHint = typeHint;
    }
}
