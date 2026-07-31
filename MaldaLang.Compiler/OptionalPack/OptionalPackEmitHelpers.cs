// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;
using MaldaLang.Parser.AST.Expressions;

namespace MaldaLang.Compiler.OptionalPack;

internal static class OptionalPackEmitHelpers
{
    public static void EmitCommaSeparatedExpressions(StringBuilder output, OptionalPackEmitContext ctx, List<Expression> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (i > 0)
            {
                output.Append(", ");
            }

            ctx.TranspileExpression(arguments[i]);
        }
    }

    public static void EmitCommaSeparatedRuntimeValues(StringBuilder output, OptionalPackEmitContext ctx, List<Expression> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (i > 0)
            {
                output.Append(", ");
            }

            output.Append("RuntimeHelpers.ToRuntimeValue(");
            ctx.TranspileExpression(arguments[i]);
            output.Append(')');
        }
    }

    public static void EmitTimeseriesCallBuiltIn(StringBuilder output, OptionalPackEmitContext ctx, string name, List<Expression> arguments)
    {
        output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.Timeseries.TimeseriesFunctions.CallBuiltIn(\"");
        output.Append(name);
        output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
        EmitCommaSeparatedRuntimeValues(output, ctx, arguments);
        output.Append(" }))");
    }

    public static void EmitTradingCreateCall(
        StringBuilder output,
        OptionalPackEmitContext ctx,
        string typeName,
        string methodName,
        List<Expression> arguments)
    {
        output.Append("RuntimeHelpers.UnwrapRuntimeValue(");
        output.Append(typeName);
        output.Append('.');
        output.Append(methodName);
        output.Append("(new List<MaldaLang.Interpreter.RuntimeValue> { ");
        EmitCommaSeparatedRuntimeValues(output, ctx, arguments);
        output.Append(" }))");
    }
}
