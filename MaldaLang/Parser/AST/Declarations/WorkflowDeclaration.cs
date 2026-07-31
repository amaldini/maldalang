// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Statements;

public class WorkflowDeclaration : Statement
{
    public string Name { get; }
    public List<string> Parameters { get; }
    public BlockStatement Body { get; }

    public WorkflowDeclaration(string name, List<string> parameters, BlockStatement body, int line = 0, int column = 0)
        : base(line, column)
    {
        Name = name;
        Parameters = parameters;
        Body = body;
    }
}
