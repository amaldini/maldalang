// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;
using MaldaLang.Parser.AST.Expressions;

namespace MaldaLang.Compiler.OptionalPack;

internal sealed class OptionalPackEmitContext
{
    public StringBuilder Output { get; }
    public Action<Expression> TranspileExpression { get; }

    public OptionalPackEmitContext(StringBuilder output, Action<Expression> transpileExpression)
    {
        Output = output;
        TranspileExpression = transpileExpression;
    }
}
