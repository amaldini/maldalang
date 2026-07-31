// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class FunctionCallExpression : Expression
{
    public Expression Callee { get; }
    public List<Expression> Arguments { get; }
    
    public FunctionCallExpression(Expression callee, List<Expression> arguments, int line = 0, int column = 0)
        : base(line, column)
    {
        Callee = callee;
        Arguments = arguments;
    }
}