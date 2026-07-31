// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Expressions;

public class SpawnExpression : Expression
{
    public string ActorName { get; }
    public List<Expression> Arguments { get; }
    
    public SpawnExpression(string actorName, List<Expression> arguments, int line = 0, int column = 0)
        : base(line, column)
    {
        ActorName = actorName;
        Arguments = arguments;
    }
}
