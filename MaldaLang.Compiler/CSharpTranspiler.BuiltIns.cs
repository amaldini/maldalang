// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Parser;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.IDE;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;

using MaldaLang.Compiler.OptionalPack;

namespace MaldaLang.Compiler;

public partial class CSharpTranspiler
{
    private void TranspileBuiltInFunction(string name, List<Expression> arguments)
    {
        if (name == "sort" && arguments.Count == 2 && arguments[1] is LambdaExpression)
        {
            _output.Append("RuntimeHelpers.UnwrapRuntimeValue(RuntimeHelpers.ToRuntimeValue(RuntimeHelpers.ArraySortWithCompare(RuntimeHelpers.GetArray(");
            TranspileExpression(arguments[0]);
            _output.Append("), ");
            TranspileExpression(arguments[1]);
            _output.Append(")))");
            return;
        }

        if (OptionalPackTranspilerEmit.TryEmit(new OptionalPackEmitContext(_output, TranspileExpression), name, arguments))
        {
            return;
        }

        // Typed fast paths for hot numeric built-ins in transpiled code.
        if (TypedTranspileEnabled && name == "float" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            TranspileExpression(arguments[0]);
            return;
        }
        if (TypedTranspileEnabled && name == "abs" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Abs(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "sqrt" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Sqrt(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "floor" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Floor((double)(");
            TranspileExpression(arguments[0]);
            _output.Append("))");
            return;
        }
        if (TypedTranspileEnabled && name == "ceil" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Ceiling((double)(");
            TranspileExpression(arguments[0]);
            _output.Append("))");
            return;
        }
        if (TypedTranspileEnabled && name == "round" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Round((double)(");
            TranspileExpression(arguments[0]);
            _output.Append("))");
            return;
        }
        if (TypedTranspileEnabled && name == "trunc" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Truncate((double)(");
            TranspileExpression(arguments[0]);
            _output.Append("))");
            return;
        }
        if (TypedTranspileEnabled && name == "sign" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("(double)Math.Sign(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "exp" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Exp(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "log" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Log(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "log10" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Log10(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "log2" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Log(");
            TranspileExpression(arguments[0]);
            _output.Append(", 2.0)");
            return;
        }

        if (TypedTranspileEnabled && name == "sin" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Sin(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "cos" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Cos(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "tan" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Tan(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "asin" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Asin(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "acos" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Acos(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "atan" && arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Atan(");
            TranspileExpression(arguments[0]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "min" && arguments.Count == 2 &&
            ResolveExpressionType(arguments[0]) == TranspiledClrType.Double &&
            ResolveExpressionType(arguments[1]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Min(");
            TranspileExpression(arguments[0]);
            _output.Append(", ");
            TranspileExpression(arguments[1]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "max" && arguments.Count == 2 &&
            ResolveExpressionType(arguments[0]) == TranspiledClrType.Double &&
            ResolveExpressionType(arguments[1]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Max(");
            TranspileExpression(arguments[0]);
            _output.Append(", ");
            TranspileExpression(arguments[1]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "pow" && arguments.Count > 1 &&
            ResolveExpressionType(arguments[0]) == TranspiledClrType.Double &&
            ResolveExpressionType(arguments[1]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Pow(");
            TranspileExpression(arguments[0]);
            _output.Append(", ");
            TranspileExpression(arguments[1]);
            _output.Append(")");
            return;
        }
        if (TypedTranspileEnabled && name == "atan2" && arguments.Count > 1 &&
            ResolveExpressionType(arguments[0]) == TranspiledClrType.Double &&
            ResolveExpressionType(arguments[1]) == TranspiledClrType.Double)
        {
            _output.Append("Math.Atan2(");
            TranspileExpression(arguments[0]);
            _output.Append(", ");
            TranspileExpression(arguments[1]);
            _output.Append(")");
            return;
        }

        switch (name)
        {
            case "print":
                if (ProfilingEnabled)
                {
                    // async lambda so arguments may use await (sync Func<> would be CS4034)
                    _output.Append("await MaldaProfiler.ProfileBuiltInAsync(\"print\", async () => { Console.WriteLine(RuntimeHelpers.CoerceToString(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")); })");
                }
                else
                {
                    _output.Append("Console.WriteLine(RuntimeHelpers.CoerceToString(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                break;
            case "input":
                if (ProfilingEnabled)
                {
                    _output.Append("await MaldaProfiler.ProfileBuiltInAsync<string>(\"input\", async () => RuntimeHelpers.ReadLineWithPrompt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    else
                        _output.Append("null");
                    _output.Append("))");
                }
                else
                {
                    _output.Append("RuntimeHelpers.ReadLineWithPrompt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    else
                        _output.Append("null");
                    _output.Append(")");
                }
                break;
            case "int":
                var intArgumentNeedsAsync = arguments.Count > 0 && ExpressionContainsAwaitingCall(arguments[0]);
                if (ProfilingEnabled && !intArgumentNeedsAsync)
                {
                    _output.Append("MaldaProfiler.ProfileBuiltIn<object>(\"int\", () => RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else if (ProfilingEnabled)
                {
                    _output.Append("await MaldaProfiler.ProfileBuiltInAsync<object>(\"int\", async () => RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else
                {
                    _output.Append("RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")");
                }
                break;
            case "float":
                var floatArgumentNeedsAsync = arguments.Count > 0 && ExpressionContainsAwaitingCall(arguments[0]);
                if (ProfilingEnabled && !floatArgumentNeedsAsync)
                {
                    _output.Append("MaldaProfiler.ProfileBuiltIn<object>(\"float\", () => RuntimeHelpers.CoerceToFloat(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else if (ProfilingEnabled)
                {
                    _output.Append("await MaldaProfiler.ProfileBuiltInAsync<object>(\"float\", async () => RuntimeHelpers.CoerceToFloat(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else
                {
                    _output.Append("RuntimeHelpers.CoerceToFloat(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")");
                }
                break;
            case "string":
                var stringArgumentNeedsAsync = arguments.Count > 0 && ExpressionContainsAwaitingCall(arguments[0]);
                if (ProfilingEnabled && !stringArgumentNeedsAsync)
                {
                    _output.Append("MaldaProfiler.ProfileBuiltIn<object>(\"string\", () => RuntimeHelpers.CoerceToString(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else if (ProfilingEnabled)
                {
                    _output.Append("await MaldaProfiler.ProfileBuiltInAsync<object>(\"string\", async () => RuntimeHelpers.CoerceToString(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                else
                {
                    _output.Append("RuntimeHelpers.CoerceToString(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")");
                }
                break;
            case "formatNumber":
                // Call BuiltInFunctions.CallBuiltIn for formatNumber
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("formatNumber");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "getWorkflowStatus":
            case "getWorkflow":
            case "getWorkflowSteps":
            case "getWorkflowEvents":
            case "getWorkflowMetrics":
            case "listWorkflows":
            case "listWorkflowDeadLetters":
            case "requeueDeadLetter":
            case "cancelWorkflow":
            case "resumeWorkflow":
            case "retryWorkflow":
            case "approveWorkflowStep":
            case "signalWorkflow":
            case "runProperty":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "startWorkflow":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltInAsync(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null).GetAwaiter().GetResult())");
                break;
            case "runWorkflowInstance":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltInAsync(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null).GetAwaiter().GetResult())");
                break;
            case "getEnv":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("getEnv");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "getEnvOr":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("getEnvOr");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "text":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("text");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "trimText":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("trimText");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "getCommandLineArgs":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("getCommandLineArgs");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "hasEnv":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("hasEnv");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "getHostPlatform":
            case "reportRalphStatus":
            case "enableAgentVerboseLogging":
            case "setAgentVerbosePhase":
            case "setAgentStatusBanner":
            case "getFileName":
            case "getDirectoryName":
            case "gitStatus":
            case "gitAdd":
            case "gitCommit":
            case "gitDiff":
            case "gitLog":
            case "gitBranch":
            case "gitCheckout":
            case "gitPush":
            case "gitPull":
            case "loadAssembly":
            case "getDotNetType":
            case "dotnetNew":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            // abs keeps an int an int (interpreter BuiltInAbs); Math.Abs over a double would not.
            case "abs":
            case "sum":
            case "average":
            case "max":
            case "min":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "pow":
                _output.Append("Math.Pow((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("), (double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 1)
                    TranspileExpression(arguments[1]);
                _output.Append("))");
                break;
            case "sqrt":
                _output.Append("Math.Sqrt((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            // Extended math: rounding and sign
            case "floor":
                _output.Append("Math.Floor((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "ceil":
                _output.Append("Math.Ceiling((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "round":
                _output.Append("Math.Round((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "trunc":
                _output.Append("Math.Truncate((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "sign":
                _output.Append("(double)Math.Sign((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            // Extended math: exponential and logarithm
            case "exp":
                _output.Append("Math.Exp((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "log":
                _output.Append("Math.Log((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "log10":
                _output.Append("Math.Log10((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "log2":
                _output.Append("Math.Log((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("), 2.0)");
                break;
            // Extended math: trigonometry
            case "sin":
                _output.Append("Math.Sin((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "cos":
                _output.Append("Math.Cos((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "tan":
                _output.Append("Math.Tan((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "asin":
                _output.Append("Math.Asin((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "acos":
                _output.Append("Math.Acos((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "atan":
                _output.Append("Math.Atan((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "atan2":
                _output.Append("Math.Atan2((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]); // y
                _output.Append("), (double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 1)
                    TranspileExpression(arguments[1]); // x
                _output.Append("))");
                break;
            // Extended math: utility
            case "hypot":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("hypot");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "clamp":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("clamp");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "degToRad":
                _output.Append("((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(") * Math.PI / 180.0)");
                break;
            case "radToDeg":
                _output.Append("((double)RuntimeHelpers.CoerceToFloat(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(") * 180.0 / Math.PI)");
                break;
            case "reply":
                _output.Append("ActorsRuntime.Reply(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                else
                    _output.Append("null");
                _output.Append(")");
                break;
            case "length":
                // length() on arrays must use element count, not stringified array length
                _output.Append("(object)RuntimeHelpers.BuiltInLength(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                else
                    _output.Append("null");
                _output.Append(")");
                break;
            case "upper":
                _output.Append("RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(").ToUpper()");
                break;
            case "lower":
                _output.Append("RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(").ToLower()");
                break;
            case "trim":
                _output.Append("RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(").Trim()");
                break;
            case "substring":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append("substring");
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            case "indexOf":
                _output.Append("RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append(").IndexOf(RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 1)
                    TranspileExpression(arguments[1]);
                _output.Append("))");
                break;
            case "sleep":
                // In actor handlers (void methods), use .Wait() instead of await
                if (_isInActorHandler)
                {
                    _output.Append("Task.Delay((int)RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")).Wait()");
                }
                else if (_transpileCallAsTask)
                {
                    _output.Append("System.Threading.Tasks.Task.Delay((int)RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append(")).ContinueWith(_ => MaldaLang.Interpreter.RuntimeValue.Null())");
                }
                else
                {
                    _output.Append("await Task.Delay((int)RuntimeHelpers.CoerceToInt(");
                    if (arguments.Count > 0)
                        TranspileExpression(arguments[0]);
                    _output.Append("))");
                }
                break;
            case "typeOf":
                _output.Append("RuntimeHelpers.TypeOfValue(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                else
                    _output.Append("null");
                _output.Append(")");
                break;
            case "isTag":
                _output.Append("RuntimeHelpers.IsTag(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                else
                    _output.Append("null");
                _output.Append(", ");
                if (arguments.Count > 1)
                    TranspileExpression(arguments[1]);
                else
                    _output.Append("null");
                _output.Append(")");
                break;
            case "createNativeCallback":
                if (arguments.Count != 1)
                    throw new InvalidOperationException("createNativeCallback() expects exactly 1 argument.");
                _output.Append("new MaldaLang.BuiltIns.DotNetObjectInstance(new MaldaLang.BuiltIns.NativeCallbackBridge(");
                TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "runCommand":
            case "loadNativeModule":
            case "createRunCommandTool":
            case "createWebSearchTool":
            case "createWebFetchTool":
            case "webFetch":
            case "createReadFileTool":
            case "createWriteFileTool":
            case "createReplaceInFileTool":
            case "createListDirectoryTool":
            case "createDeleteFileTool":
            case "createCopyFileTool":
            case "createEnsureDirTool":
            case "createAskUserTool":
            case "createGrepTool":
            case "createGlobTool":
            case "createInsertAtLineTool":
            case "createEditFileTool":
            case "createGitStatusTool":
            case "createGitAddTool":
            case "createGitCommitTool":
            case "createGitLogTool":
            case "createGitDiffTool":
            case "createGitBranchTool":
            case "createGitCheckoutTool":
            case "createGitPushTool":
            case "createGitPullTool":
            case "uiGenerate":
            case "runMALDA":
            case "createRunMALDATool":
            case "compileMALDA":
            case "createCompileMALDATool":
            case "getSymbols":
            case "createGetSymbolsTool":
            case "getParseErrors":
            case "createGetParseErrorsTool":
            case "checkMalda":
            case "createCheckMaldaTool":
            case "createValidateJsonTool":
            case "createTestMaldaTool":
            case "createMcpAgentScript":
            case "createCreateMcpAgentScriptTool":
            case "createSubmitPlanTool":
            case "createUpdatePlanTool":
            case "createMarkStepTool":
            case "executePlan":
            case "runProgram":
            case "decomposeTask":
            case "extractHTML":
            case "markdownToHtml":
            case "extractPdfText":
            case "extractDocxText":
            case "renderTemplate":
            case "componentFragment":
            case "componentLiveEmit":
            case "componentStateGet":
            case "componentStateSet":
            case "componentStateObject":
            case "componentStateClear":
            case "componentStateConfigure":
            case "componentStatePin":
            case "componentStateUnpin":
            case "onAgentProgress":
            case "clearAgentProgress":
            case "cancelThink":
            case "uiRow":
            case "uiColumn":
            case "uiStack":
            case "uiSpacer":
            case "uiPanel":
            case "uiText":
            case "uiHeading":
            case "uiImage":
            case "uiIcon":
            case "uiButton":
            case "uiTextField":
            case "uiCheckbox":
            case "uiSelect":
            case "uiSlider":
            case "uiDatePicker":
            case "uiList":
            case "uiTable":
            case "uiAlert":
            case "uiProgress":
            case "uiModal":
            case "uiForm":
            case "uiField":
            case "uiTextArea":
            case "uiRadioGroup":
            case "uiSwitch":
            case "uiTabs":
            case "uiAccordion":
            case "uiBreadcrumbs":
            case "uiDrawer":
            case "uiDataGrid":
            case "uiTreeView":
            case "uiPaginator":
            case "uiEmptyState":
            case "uiBadge":
            case "uiToast":
            case "uiSkeleton":
            case "uiSpinner":
            case "uiErrorBoundary":
            case "uiSlot":
            case "uiWithSlot":
            case "uiWhen":
            case "uiChoose":
            case "uiEach":
            case "uiTemplate":
            case "uiPartial":
            case "uiLayout":
            case "uiRenderList":
            case "uiCrudModel":
            case "uiCrudControls":
            case "uiCrudSchema":
            case "uiMount":
            case "uiMountEnvelope":
            case "uiRender":
            case "uiDispatchEvent":
            case "uiPullEvent":
            case "uiState":
            case "uiGetState":
            case "uiSetState":
            case "uiPinState":
            case "uiUnpinState":
            case "uiInvalidate":
            case "uiOnInit":
            case "uiOnPreRender":
            case "uiOnLoad":
            case "uiOnDispose":
            case "uiOnMount":
            case "uiOnUpdate":
            case "uiOnUnmount":
            case "uiOnError":
            case "uiConfigure":
            case "uiSnapshot":
            case "uiResync":
            case "uiSessionId":
            case "uiRedirectWithSession":
            case "redirect":
            case "RedirectTo":
            case "httpGet":
            case "httpPost":
            case "httpPut":
            case "httpDelete":
            case "httpPatch":
            case "httpBearerToken":
            case "httpCookieToken":
            case "httpAuthToken":
            case "webSearch":
            // Date/Time functions
            case "now":
            case "formatDate":
            case "parseDate":
            case "addDays":
            case "addHours":
            // Random functions
            case "random":
            case "randomInt":
            case "randomFloat":
            case "zeros":
            // LLM-oriented math helpers
            case "rsqrt":
            case "randn":
            case "argmax":
            case "argmin":
            case "logSumExp":
            case "softmax":
            case "randomChoiceWeighted":
            case "seed":
            case "anneal":
            // Type checking functions
            case "isNumber":
            case "isString":
            case "isArray":
            case "isObject":
            // Array utilities
            case "join":
            case "split":
            case "normalizeText":
            case "tokenize":
            case "tokenOverlap":
            case "similarity":
            case "extractNumbers":
            case "replace":
            case "regexMatch":
            case "regexReplace":
            case "regexFind":
            case "reverse":
            case "sort":
            case "includes":
            // Encoding/Decoding
            case "base64Encode":
            case "base64Decode":
            case "urlEncode":
            case "urlDecode":
            // Hash functions
            case "md5":
            case "sha256":
            case "hashPassword":
            case "verifyPassword":
            case "createJwt":
            case "verifyJwt":
            case "generateCsrfToken":
            case "verifyCsrfToken":
            case "csrfField":
            case "formErrors":
            case "bindForm":
            case "pageLayout":
            case "createSecureCookie":
            case "readSecureCookie":
            case "enqueueJob":
            case "claimJob":
            case "completeJob":
            case "failJob":
            case "getJob":
            case "listJobs":
            // Path manipulation
            case "pathJoin":
            case "pathNormalize":
            case "pathExists":
            case "pathGetExtension":
            case "isPathUnder":
            // Range generation
            case "range":
            // Error handling
            case "exit":
            case "error":
            case "assert":
            // Additional string utilities
            case "startsWith":
            case "endsWith":
            case "padStart":
            case "padEnd":
            case "repeat":
            case "toIntOr":
            case "toIntOrNull":
            case "toCsv":
            case "getMaldaHome":
            case "getProgramDirectory":
            case "getMaldaConfig":
            case "getAssistantMemory":
            case "getSkillNames":
            case "loadSkill":
            case "loadSkillsFromDir":
            case "parseJSON":
            case "toJSON":
            case "readFile":
            case "readTextFileLines":
            case "writeFile":
            case "copyFile":
            case "writeFileBase64":
            case "readFileBase64":
            case "writePng":
            case "readPng":
            case "hasFile":
            case "deleteFile":
            case "hasDirectory":
            case "listDirectory":
            case "hasEmbeddedFolder":
            case "embeddedFolderRoot":
            case "glob":
            case "grep":
            case "replaceInFile":
            case "editFile":
            case "insertAtLine":
            case "ensureDir":
                // These built-ins need to call BuiltInFunctions.CallBuiltIn
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append(name);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    EmitToRuntimeValueArgument(arguments[i]);
                }
                _output.Append(" }, null))");
                break;
            case "all":
                // all(...) returns a Task RuntimeValue composed from its arguments.
                // In normal expressions, we want the RuntimeValue itself.
                // When transpiling as a Task (inside async expr), return the underlying Task<RuntimeValue>.
                _output.Append("MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"all\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null)");
                if (_transpileCallAsTask)
                {
                    _output.Append(".AsTask()");
                }
                break;
            default:
                if (BuiltInRegistry.IsTranspilerBuiltIn(name))
                {
                    var builtInDescriptor = BuiltInRegistry.GetDescriptor(name);
                    var useAsyncDispatch = builtInDescriptor != null && !builtInDescriptor.IsAlwaysSynchronousForCodegen;
                    _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.");
                    _output.Append(useAsyncDispatch ? "CallBuiltInAsync(\"" : "CallBuiltIn(\"");
                    _output.Append(name);
                    _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                    for (int i = 0; i < arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        _output.Append("RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(arguments[i]);
                        _output.Append(")");
                    }
                    _output.Append(" }, null)");
                    if (useAsyncDispatch)
                    {
                        _output.Append(".GetAwaiter().GetResult()");
                    }
                    _output.Append(")");
                    break;
                }

                // Fallback for non-built-in identifiers (should not happen for vetted built-ins).
                _output.Append(name);
                _output.Append("(");
                for (int i = 0; i < arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    TranspileExpression(arguments[i]);
                }
                _output.Append(")");
                break;
        }
    }

    private bool TryTranspileVariantStdLibCall(MemberAccessExpression memberAccess, FunctionCallExpression call)
    {
        if (memberAccess.Object is not IdentifierExpression moduleId)
            return false;

        string? staticMethod = (moduleId.Name, memberAccess.Member) switch
        {
            (StdLibNamespaces.ResultModule, "ok") => nameof(VariantStdLib.ResultOk),
            (StdLibNamespaces.ResultModule, "err") => nameof(VariantStdLib.ResultErr),
            (StdLibNamespaces.ResultModule, "map") => nameof(VariantStdLib.ResultMap),
            (StdLibNamespaces.ResultModule, "andThen") => nameof(VariantStdLib.ResultAndThen),
            (StdLibNamespaces.ResultModule, "unwrapOr") => nameof(VariantStdLib.ResultUnwrapOr),
            (StdLibNamespaces.ResultModule, "isOk") => nameof(VariantStdLib.ResultIsOk),
            (StdLibNamespaces.ResultModule, "isErr") => nameof(VariantStdLib.ResultIsErr),
            (StdLibNamespaces.OptionModule, "some") => nameof(VariantStdLib.OptionSome),
            (StdLibNamespaces.OptionModule, "none") => nameof(VariantStdLib.OptionNone),
            (StdLibNamespaces.OptionModule, "map") => nameof(VariantStdLib.OptionMap),
            (StdLibNamespaces.OptionModule, "andThen") => nameof(VariantStdLib.OptionAndThen),
            (StdLibNamespaces.OptionModule, "unwrapOr") => nameof(VariantStdLib.OptionUnwrapOr),
            (StdLibNamespaces.OptionModule, "isSome") => nameof(VariantStdLib.OptionIsSome),
            (StdLibNamespaces.OptionModule, "isNone") => nameof(VariantStdLib.OptionIsNone),
            _ => null
        };

        if (staticMethod == null)
            return false;

        if (memberAccess.Member is "map" or "andThen" && call.Arguments.Count == 2)
        {
            var (successTag, failureTag) = moduleId.Name == StdLibNamespaces.ResultModule
                ? ("Ok", "Err")
                : ("Some", "None");
            if (memberAccess.Member == "andThen")
            {
                _output.Append("RuntimeHelpers.AndThenVariantWithDelegate(RuntimeHelpers.ToRuntimeValue(");
                TranspileExpression(call.Arguments[0]);
                _output.Append("), ");
                TranspileExpression(call.Arguments[1]);
                _output.Append(", \"");
                _output.Append(successTag);
                _output.Append("\", \"");
                _output.Append(failureTag);
                _output.Append("\", \"");
                _output.Append(moduleId.Name);
                _output.Append("\")");
                return true;
            }

            _output.Append("RuntimeHelpers.MapVariantWithDelegate(RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(call.Arguments[0]);
            _output.Append("), ");
            TranspileExpression(call.Arguments[1]);
            _output.Append(", \"");
            _output.Append(successTag);
            _output.Append("\", \"");
            _output.Append(failureTag);
            _output.Append("\")");
            return true;
        }

        _output.Append("MaldaLang.BuiltIns.VariantStdLib.");
        _output.Append(staticMethod);
        _output.Append("(new List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0)
                _output.Append(", ");
            _output.Append("RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(call.Arguments[i]);
            _output.Append(")");
        }

        _output.Append(" })");
        return true;
    }

    private bool TryTranspileGroundedStdLibCall(MemberAccessExpression memberAccess, FunctionCallExpression call)
    {
        if (memberAccess.Object is not IdentifierExpression moduleId)
            return false;
        if (moduleId.Name != StdLibNamespaces.GroundedModule || memberAccess.Member != "wrap")
            return false;

        _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.GroundedStdLib.Wrap(new List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0)
                _output.Append(", ");
            _output.Append("RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(call.Arguments[i]);
            _output.Append(")");
        }

        _output.Append(" }))");
        return true;
    }

    private bool TryTranspileCapStdLibCall(MemberAccessExpression memberAccess, FunctionCallExpression call)
    {
        if (memberAccess.Object is not IdentifierExpression moduleId)
            return false;
        if (moduleId.Name != StdLibNamespaces.CapModule
            || !StdLibNamespaces.CapMethodNames.Contains(memberAccess.Member))
            return false;

        var method = memberAccess.Member switch
        {
            "fileRead" => nameof(CapStdLib.FileRead),
            "fileWrite" => nameof(CapStdLib.FileWrite),
            "dirList" => nameof(CapStdLib.DirList),
            "httpGet" => nameof(CapStdLib.HttpGet),
            "mcpCall" => nameof(CapStdLib.McpCall),
            "shell" => nameof(CapStdLib.Shell),
            "is" => nameof(CapStdLib.Is),
            "confine" => nameof(CapStdLib.Confine),
            "read" => nameof(CapStdLib.Read),
            "write" => nameof(CapStdLib.Write),
            "list" => nameof(CapStdLib.List),
            "fetch" => nameof(CapStdLib.Fetch),
            "invoke" => nameof(CapStdLib.Invoke),
            "run" => nameof(CapStdLib.Run),
            _ => null
        };
        if (method == null)
            return false;

        var needsInterpreter = method is nameof(CapStdLib.Read) or nameof(CapStdLib.Write) or nameof(CapStdLib.List)
            or nameof(CapStdLib.Fetch) or nameof(CapStdLib.Run);
        _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.CapStdLib.");
        _output.Append(method);
        _output.Append("(new List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0)
                _output.Append(", ");
            _output.Append("RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(call.Arguments[i]);
            _output.Append(")");
        }

        _output.Append(needsInterpreter ? " }, null))" : " }))");
        return true;
    }

    private bool TryTranspileAgentsStdLibCall(MemberAccessExpression memberAccess, FunctionCallExpression call)
    {
        if (memberAccess.Object is not IdentifierExpression moduleId)
            return false;
        if (moduleId.Name != StdLibNamespaces.AgentsModule
            || !StdLibNamespaces.AgentsMethodNames.Contains(memberAccess.Member))
            return false;

        var method = memberAccess.Member switch
        {
            "define" => nameof(AgentsStdLib.Define),
            "team" => nameof(AgentsStdLib.Team),
            _ => null
        };
        if (method == null)
            return false;

        _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.AgentsStdLib.");
        _output.Append(method);
        _output.Append("(new List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0)
                _output.Append(", ");
            _output.Append("RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(call.Arguments[i]);
            _output.Append(")");
        }

        _output.Append(" }, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter()))");
        return true;
    }

    private bool TryTranspileNnStdLibCall(MemberAccessExpression memberAccess, FunctionCallExpression call)
    {
        if (memberAccess.Object is not IdentifierExpression moduleId)
            return false;
        if (moduleId.Name != StdLibNamespaces.NnModule
            || !StdLibNamespaces.NnMethodNames.Contains(memberAccess.Member))
            return false;

        _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.NnStdLib.Call(\"");
        _output.Append(memberAccess.Member);
        _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0)
                _output.Append(", ");
            _output.Append("RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(call.Arguments[i]);
            _output.Append(")");
        }

        _output.Append(" }))");
        return true;
    }

    /// <summary>
    /// <c>buf.*</c>: <see cref="BufStdLib"/> on RuntimeValues. A buffer is a
    /// <c>TypedBufferInstance</c> object on both sides, so it crosses unchanged (no copy) and
    /// <c>buf.writeAt</c> changes the buffer the program holds.
    /// </summary>
    private bool TryTranspileBufStdLibCall(MemberAccessExpression memberAccess, FunctionCallExpression call)
    {
        if (memberAccess.Object is not IdentifierExpression moduleId)
            return false;
        if (moduleId.Name != StdLibNamespaces.BufModule
            || !StdLibNamespaces.BufMethodNames.Contains(memberAccess.Member))
            return false;

        _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BufStdLib.Call(\"");
        _output.Append(memberAccess.Member);
        _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0)
                _output.Append(", ");
            _output.Append("RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(call.Arguments[i]);
            _output.Append(")");
        }

        _output.Append(" }))");
        return true;
    }

    private bool TryTranspileTraceStdLibCall(MemberAccessExpression memberAccess, FunctionCallExpression call)
    {
        if (memberAccess.Object is not IdentifierExpression moduleId)
            return false;
        if (moduleId.Name != StdLibNamespaces.TraceModule
            || !StdLibNamespaces.TraceMethodNames.Contains(memberAccess.Member))
            return false;

        var method = memberAccess.Member switch
        {
            "span" => nameof(TraceStdLib.Span),
            "journal" => nameof(TraceStdLib.Journal),
            "lastUsage" => nameof(TraceStdLib.LastUsage),
            _ => null
        };
        if (method == null)
            return false;

        _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.TraceStdLib.");
        _output.Append(method);
        _output.Append("(new List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0)
                _output.Append(", ");
            _output.Append("RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(call.Arguments[i]);
            _output.Append(")");
        }

        if (method == nameof(TraceStdLib.Span))
            _output.Append(" }, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter()))");
        else
            _output.Append(" }))");
        return true;
    }
}
