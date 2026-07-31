// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Statements;

public class ChainDeclaration : Statement
{
    public string Name { get; }
    public List<string> Parameters { get; }
    public string? ReturnType { get; }
    public BlockStatement Body { get; }

    public ChainDeclaration(
        string name,
        List<string> parameters,
        BlockStatement body,
        string? returnType = null,
        int line = 0,
        int column = 0)
        : base(line, column)
    {
        Name = name;
        Parameters = parameters;
        Body = body;
        ReturnType = returnType;
    }

    public FunctionDeclaration ToFunctionDeclaration() =>
        new(Name, Parameters, Body, null, null, null, ReturnType, false, Line, Column);
}
