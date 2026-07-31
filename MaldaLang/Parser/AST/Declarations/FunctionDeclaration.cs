// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Statements;

public class FunctionDeclaration : Statement
{
    public string Name { get; }
    public List<string> Parameters { get; }
    public List<string?>? ParameterTypeHints { get; }  // Optional type hint per parameter; same order as Parameters
    public string? ReturnType { get; }  // Optional return type hint (informational only)
    public List<Decorator> ParameterDecorators { get; }  // Decorators for each parameter, indexed by parameter position
    public BlockStatement Body { get; }
    public List<Decorator> Decorators { get; }
    public bool IsExported { get; }
    
    public FunctionDeclaration(string name, List<string> parameters, BlockStatement body, List<Decorator>? decorators = null, List<Decorator>? parameterDecorators = null, List<string?>? parameterTypeHints = null, string? returnType = null, bool isExported = false, int line = 0, int column = 0)
        : base(line, column)
    {
        Name = name;
        IsExported = isExported;
        Parameters = parameters;
        ParameterTypeHints = parameterTypeHints;
        ReturnType = returnType;
        ParameterDecorators = parameterDecorators ?? new List<Decorator>();
        Body = body;
        Decorators = decorators ?? new List<Decorator>();
    }
}

public class ParameterInfo
{
    public string Name { get; }
    public List<Decorator> Decorators { get; }
    
    public ParameterInfo(string name, List<Decorator>? decorators = null)
    {
        Name = name;
        Decorators = decorators ?? new List<Decorator>();
    }
}