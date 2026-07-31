// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

public class ForInStatement : Statement
{
    public string VariableName { get; }
    public Expression Collection { get; }
    public Statement Body { get; }
    
    public ForInStatement(string variableName, Expression collection, Statement body, int line = 0, int column = 0)
        : base(line, column)
    {
        VariableName = variableName;
        Collection = collection;
        Body = body;
    }
}
