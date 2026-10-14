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
    private void TranspileFunctionCall(FunctionCallExpression call)
    {
        // Variant constructor call: Ok(expr) or Result.Ok(expr)
        if (TryGetVariantConstructorCallName(call.Callee, out var variantCtorName))
        {
            _output.Append(EscapeIdentifier(variantCtorName));
            _output.Append("(");
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                _output.Append("RuntimeHelpers.ToRuntimeValue(");
                TranspileExpression(call.Arguments[i]);
                _output.Append(")");
            }
            _output.Append(")");
            return;
        }

        // Check if this is a member access call (like arr.append(x))
        if (call.Callee is MemberAccessExpression memberAccess)
        {
            // this.m(...) / Class.m(...) reaching a user method with a typed variant: a plain typed
            // call. Built-in member names never resolve here (see ResolveTypedMethodKey).
            if (!_transpileCallAsTask && TryGetTypedMethodCall(call, out var typedMethodSignature))
            {
                if (memberAccess.Object is ThisExpression)
                    _output.Append("this");
                else
                    _output.Append(EscapeIdentifier(((IdentifierExpression)memberAccess.Object!).Name));
                _output.Append(".");
                _output.Append(GetTypedTwinName(memberAccess.Member));
                _output.Append("(");
                EmitTypedMethodArguments(call.Arguments, typedMethodSignature);
                _output.Append(")");
                return;
            }

            var memberName = memberAccess.Member;
            if (memberName == "append")
            {
                var memberObjectType = ResolveExpressionType(memberAccess.Object);
                if (call.Arguments.Count > 0)
                {
                    if (memberObjectType == TranspiledClrType.DoubleArray)
                    {
                        _output.Append("RuntimeHelpers.ArrayAppendDouble(RuntimeHelpers.CoerceToDoubleList(");
                        TranspileExpression(memberAccess.Object);
                        _output.Append("), ");
                        TranspileExpression(call.Arguments[0]);
                        _output.Append(")");
                        return;
                    }
                    // Check if the argument is a function call that needs to be awaited
                    var argExpr = call.Arguments[0];
                    bool isAsyncCall = false;
                    string? funcParamName = null;
                    Expression? funcArg = null;
                    
                    if (argExpr is FunctionCallExpression funcCall)
                    {
                        // Check if this is a function call that returns a Task
                        // This includes function parameters and lambda calls
                        if (funcCall.Callee is IdentifierExpression argIdExpr)
                        {
                            // If it's not a known function name, it's likely a function parameter
                            if (!IsKnownUserFunction(argIdExpr.Name) && !IsBuiltInFunction(argIdExpr.Name))
                            {
                                // Function parameter call - needs to be awaited
                                isAsyncCall = true;
                                funcParamName = argIdExpr.Name;
                                funcArg = funcCall.Arguments.Count > 0 ? funcCall.Arguments[0] : null;
                            }
                            // If it's a known function, it's already async and returns Task<object>
                            else if (IsKnownUserFunction(argIdExpr.Name))
                            {
                                // User-defined function - also returns Task, needs async handling
                                isAsyncCall = true;
                                funcParamName = null; // Will handle differently
                                funcArg = null;
                            }
                        }
                        // Lambda expressions also return Task<object>
                        else if (funcCall.Callee is LambdaExpression)
                        {
                            isAsyncCall = true;
                            funcParamName = null;
                            funcArg = null;
                        }
                    }
                    
                    if (isAsyncCall)
                    {
                        // Function call that returns Task - use async version
                        _output.Append("await RuntimeHelpers.ArrayAppendAsync(RuntimeHelpers.GetArray(");
                        TranspileExpression(memberAccess.Object);
                        _output.Append("), ");
                        
                        if (funcParamName != null)
                        {
                            // Function parameter call
                            _output.Append("RuntimeHelpers.CallFunction(");
                            _output.Append(EscapeIdentifier(funcParamName));
                            _output.Append(", ");
                            if (funcArg != null)
                                TranspileExpression(funcArg);
                            else
                                _output.Append("null");
                            _output.Append(")");
                        }
                        else
                        {
                            // User-defined function or lambda - transpile the call which will include await
                            // We need to wrap it to get the Task without the await
                            _output.Append("(");
                            // Transpile without await - we'll get the Task
                            var funcCallExpr = (FunctionCallExpression)argExpr;
                            if (funcCallExpr.Callee is IdentifierExpression funcIdExpr && IsKnownUserFunction(funcIdExpr.Name))
                            {
                                // User-defined function - call directly to get Task
                                _output.Append(EscapeIdentifier(funcIdExpr.Name));
                                _output.Append("(");
                                for (int i = 0; i < funcCallExpr.Arguments.Count; i++)
                                {
                                    if (i > 0) _output.Append(", ");
                                    TranspileExpression(funcCallExpr.Arguments[i]);
                                }
                                _output.Append(")");
                            }
                            else
                            {
                                // For other cases, just transpile normally - ArrayAppendAsync will await it
                                TranspileExpression(argExpr);
                            }
                            _output.Append(")");
                        }
                        _output.Append(")");
                    }
                    else
                    {
                        // Regular argument - use synchronous version
                        _output.Append("RuntimeHelpers.ArrayAppend(RuntimeHelpers.GetArray(");
                        TranspileExpression(memberAccess.Object);
                        _output.Append("), ");
                        TranspileExpression(argExpr);
                        _output.Append(")");
                    }
                }
                else
                {
                    if (memberObjectType == TranspiledClrType.DoubleArray)
                    {
                        _output.Append("RuntimeHelpers.ArrayAppendDouble(RuntimeHelpers.CoerceToDoubleList(");
                        TranspileExpression(memberAccess.Object);
                        _output.Append("), 0d)");
                        return;
                    }
                    _output.Append("RuntimeHelpers.ArrayAppend(RuntimeHelpers.GetArray(");
                    TranspileExpression(memberAccess.Object);
                    _output.Append("), null)");
                }
                return;
            }
            else if (memberName == "pop")
            {
                _output.Append("RuntimeHelpers.ArrayPop(RuntimeHelpers.GetArray(");
                TranspileExpression(memberAccess.Object);
                _output.Append("))");
                return;
            }
            else if (memberName == "shift")
            {
                _output.Append("RuntimeHelpers.ArrayShift(RuntimeHelpers.GetArray(");
                TranspileExpression(memberAccess.Object);
                _output.Append("))");
                return;
            }
            else if (memberName == "concat")
            {
                _output.Append("RuntimeHelpers.ArrayConcat(RuntimeHelpers.GetArray(");
                TranspileExpression(memberAccess.Object);
                _output.Append("), RuntimeHelpers.GetArray(");
                if (call.Arguments.Count > 0)
                    TranspileExpression(call.Arguments[0]);
                else
                    _output.Append("new List<object>()");
                _output.Append("))");
                return;
            }
            else if (memberName == "except")
            {
                _output.Append("RuntimeHelpers.ArrayExcept(RuntimeHelpers.GetArray(");
                TranspileExpression(memberAccess.Object);
                _output.Append("), RuntimeHelpers.GetArray(");
                if (call.Arguments.Count > 0)
                    TranspileExpression(call.Arguments[0]);
                else
                    _output.Append("new List<object>()");
                _output.Append("))");
                return;
            }
            else if ((memberName == "sum" || memberName == "average" || memberName == "min" || memberName == "max") &&
                     call.Arguments.Count == 0)
            {
                TranspileBuiltInFunction(memberName, new List<Expression> { memberAccess.Object });
                return;
            }
            else if (memberAccess.Object is IdentifierExpression taIdExpr &&
                     taIdExpr.Name == "ta" &&
                     OptionalPackTranspilerBuiltIns.IsTimeseriesName(memberName))
            {
                TranspileBuiltInFunction(memberName, call.Arguments);
                return;
            }
        }

        // Check if this is a built-in function call
        if (call.Callee is IdentifierExpression identifier)
        {
            var funcName = identifier.Name;
            if (IsBuiltInFunction(funcName))
            {
                TranspileBuiltInFunction(funcName, call.Arguments);
                return;
            }
        }

        // Check if this is a method call on an ObjectInstance or actor reference (e.g., server.start(), actor.stop())
        if (call.Callee is MemberAccessExpression memberAccess2)
        {
            var methodName = memberAccess2.Member;

            // super.method(...) must become a direct C# call on the base class; `base` is not a
            // value, so the generic CallObjectMethod(reflection) path cannot be used here.
            if (memberAccess2.Object is SuperExpression)
            {
                TranspileSuperMethodCall(call, methodName);
                return;
            }

            // Special handling for .stop() that may target either an actor (ActorRef) or
            // a regular object (e.g., HttpServerInstance, MCPServerInstance).
            if (methodName == "stop" && call.Arguments.Count == 0)
            {
                _output.Append("RuntimeHelpers.CallActorOrVoidStop(");
                TranspileExpression(memberAccess2.Object);
                _output.Append(")");
                return;
            }

            // For HttpServerInstance, call public methods directly
            if (methodName == "start" || methodName == "stop" || methodName == "clearCache" || methodName == "getRoutes")
            {
                if (methodName == "getRoutes")
                {
                    // getRoutes returns a RuntimeValue that needs to be unwrapped
                    _output.Append("RuntimeHelpers.UnwrapRuntimeValue(");
                    _output.Append("((");
                    TranspileExpression(memberAccess2.Object);
                    _output.Append(") as MaldaLang.BuiltIns.HttpServerInstance)?.GetRoutes() ?? await RuntimeHelpers.CallObjectMethod(");
                    TranspileExpression(memberAccess2.Object);
                    _output.Append(", \"getRoutes\", new List<object>())");
                    _output.Append(")");
                }
                else
                {
                    // void methods - use helper
                    _output.Append("RuntimeHelpers.CallVoidMethod(");
                    TranspileExpression(memberAccess2.Object);
                    _output.Append(", \"");
                    _output.Append(methodName);
                    _output.Append("\")");
                }
                return;
            }

            if (TryTranspileNnStdLibCall(memberAccess2, call))
            {
                return;
            }

            // math.* / Math.* (deprecated alias) — map to existing built-ins
            if (memberAccess2.Object is IdentifierExpression mathIdExpr &&
                (mathIdExpr.Name == StdLibNamespaces.MathModule || mathIdExpr.Name == StdLibNamespaces.DeprecatedMathModuleAlias) &&
                StdLibNamespaces.MathMethodNames.Contains(memberAccess2.Member))
            {
                TranspileBuiltInFunction(memberAccess2.Member, call.Arguments);
                return;
            }
            if (memberAccess2.Object is IdentifierExpression strIdExpr &&
                strIdExpr.Name == StdLibNamespaces.StrModule &&
                StdLibNamespaces.StrMethodNames.Contains(memberAccess2.Member))
            {
                TranspileBuiltInFunction(memberAccess2.Member, call.Arguments);
                return;
            }
            if (memberAccess2.Object is IdentifierExpression ioIdExpr &&
                ioIdExpr.Name == StdLibNamespaces.IoModule &&
                StdLibNamespaces.IoMethodNames.Contains(memberAccess2.Member))
            {
                TranspileBuiltInFunction(memberAccess2.Member, call.Arguments);
                return;
            }
            if (memberAccess2.Object is IdentifierExpression pdfIdExpr &&
                pdfIdExpr.Name == StdLibNamespaces.PdfModule &&
                StdLibNamespaces.PdfMethodNames.Contains(memberAccess2.Member))
            {
                TranspileBuiltInFunction(
                    StdLibNamespaces.ResolvePdfBuiltInName(memberAccess2.Member),
                    call.Arguments);
                return;
            }
            if (memberAccess2.Object is IdentifierExpression docIdExpr &&
                docIdExpr.Name == StdLibNamespaces.DocModule &&
                StdLibNamespaces.DocMethodNames.Contains(memberAccess2.Member))
            {
                TranspileBuiltInFunction(
                    StdLibNamespaces.ResolveDocBuiltInName(memberAccess2.Member),
                    call.Arguments);
                return;
            }
            if (TryTranspileVariantStdLibCall(memberAccess2, call))
            {
                return;
            }
            if (TryTranspileGroundedStdLibCall(memberAccess2, call))
            {
                return;
            }
            if (TryTranspileCapStdLibCall(memberAccess2, call))
            {
                return;
            }
            if (TryTranspileAgentsStdLibCall(memberAccess2, call))
            {
                return;
            }
            if (TryTranspileTraceStdLibCall(memberAccess2, call))
            {
                return;
            }
            if (memberAccess2.Object is IdentifierExpression taIdExpr &&
                taIdExpr.Name == "ta" &&
                OptionalPackTranspilerBuiltIns.IsTimeseriesName(memberAccess2.Member))
            {
                TranspileBuiltInFunction(memberAccess2.Member, call.Arguments);
                return;
            }
            // Special handling for AnsiConsole methods
            if (memberAccess2.Object is IdentifierExpression ansiConsoleIdExpr && ansiConsoleIdExpr.Name == "AnsiConsole")
            {
                var ansiMethodName = memberAccess2.Member;
                if (ansiMethodName == "markup")
                {
                    // Sync method - call Spectre.Console directly
                    if (call.Arguments.Count != 1)
                        throw new Exception("AnsiConsole.markup() expects 1 argument");
                    _output.Append("Spectre.Console.AnsiConsole.Markup(RuntimeHelpers.CoerceToString(");
                    TranspileExpression(call.Arguments[0]);
                    _output.Append("))");
                    return;
                }
                else if (ansiMethodName == "markupLine")
                {
                    // Sync method - call Spectre.Console directly
                    if (call.Arguments.Count != 1)
                        throw new Exception("AnsiConsole.markupLine() expects 1 argument");
                    _output.Append("Spectre.Console.AnsiConsole.MarkupLine(RuntimeHelpers.CoerceToString(");
                    TranspileExpression(call.Arguments[0]);
                    _output.Append("))");
                    return;
                }
                else if (ansiMethodName == "table")
                {
                    // Call the built-in function directly
                    _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.BuiltInSpectreConsoleTable(new List<MaldaLang.Interpreter.RuntimeValue> { ");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        _output.Append("RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(call.Arguments[i]);
                        _output.Append(")");
                    }
                    _output.Append(" }))");
                    return;
                }
                else if (ansiMethodName == "panel")
                {
                    // Call the built-in function directly
                    _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.BuiltInSpectreConsolePanel(new List<MaldaLang.Interpreter.RuntimeValue> { ");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        _output.Append("RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(call.Arguments[i]);
                        _output.Append(")");
                    }
                    _output.Append(" }))");
                    return;
                }
                else if (ansiMethodName == "tree")
                {
                    // Call the built-in function directly
                    _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.BuiltInSpectreConsoleTree(new List<MaldaLang.Interpreter.RuntimeValue> { ");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        _output.Append("RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(call.Arguments[i]);
                        _output.Append(")");
                    }
                    _output.Append(" }))");
                    return;
                }
                else if (ansiMethodName == "status")
                {
                    // Async method with callback - transpile to Spectre.Console directly
                    if (call.Arguments.Count < 1)
                        throw new Exception("AnsiConsole.status() expects at least 1 argument");
                    
                    _output.Append("await Spectre.Console.AnsiConsole.Status().StartAsync(RuntimeHelpers.CoerceToString(");
                    TranspileExpression(call.Arguments[0]);
                    _output.Append("), async ctx => ");
                    
                    if (call.Arguments.Count > 1)
                    {
                        // Has callback - transpile it
                        var callbackArg = call.Arguments[1];
                        if (callbackArg is LambdaExpression lambda)
                        {
                            // Transpile lambda directly
                            _output.Append("{");
                            _indentLevel++;
                            _output.AppendLine();
                            WriteIndent();
                            
                            // Transpile lambda body
                            if (lambda.BlockBody != null)
                            {
                                foreach (var stmt in lambda.BlockBody.Statements)
                                {
                                    WriteIndent();
                                    TranspileStatement(stmt);
                                }
                            }
                            else if (lambda.ExpressionBody != null)
                            {
                                _output.Append("await ");
                                TranspileExpression(lambda.ExpressionBody);
                                _output.AppendLine(";");
                            }
                            
                            _indentLevel--;
                            WriteIndent();
                            _output.Append("}");
                        }
                        else
                        {
                            // Function reference - call it
                            _output.Append("{ await ");
                            TranspileExpression(callbackArg);
                            _output.Append("(null); }");
                        }
                    }
                    else
                    {
                        _output.Append("{ }");
                    }
                    _output.Append(")");
                    return;
                }
                else if (ansiMethodName == "prompt")
                {
                    // Prompt doesn't need interpreter - call built-in function directly
                    if (call.Arguments.Count < 1)
                        throw new Exception("AnsiConsole.prompt() expects at least 1 argument");
                    
                    _output.Append("await RuntimeHelpers.UnwrapRuntimeValueAsync(MaldaLang.BuiltIns.BuiltInFunctions.BuiltInSpectreConsolePromptAsync(new List<MaldaLang.Interpreter.RuntimeValue> { ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(call.Arguments[0]);
                    _output.Append(")");
                    _output.Append(" }, null))");
                    return;
                }
                else if (ansiMethodName == "progress")
                {
                    // Transpile progress to call Spectre.Console directly with callback support
                    if (call.Arguments.Count < 1)
                        throw new Exception("AnsiConsole.progress() expects at least 1 argument");
                    
                    // Check if the first argument is a lambda (new callback syntax)
                    if (call.Arguments[0] is LambdaExpression callbackLambda)
                    {
                        // New callback-based syntax: AnsiConsole.progress((ctx) => { ... })
                        _output.Append("await Spectre.Console.AnsiConsole.Progress().StartAsync(async ctx =>");
                        _output.AppendLine();
                        WriteIndent();
                        _output.Append("{");
                        _indentLevel++;
                        _output.AppendLine();
                        
                        // Create task dictionary to map task names to ProgressTask instances
                        WriteIndent();
                        _output.AppendLine("var __taskDict = new System.Collections.Generic.Dictionary<string, Spectre.Console.ProgressTask>();");
                        _output.AppendLine();
                        
                        // Create wrapper instance (shared by all methods)
                        WriteIndent();
                        _output.AppendLine("var __progressCtxWrapper = new MaldaLang.BuiltIns.ProgressContextWrapper(ctx, __taskDict);");
                        _output.AppendLine();
                        
                        // Create wrapper object with addTask, increment, and isFinished methods
                        WriteIndent();
                        _output.AppendLine("var __progressCtx = new MaldaLang.BuiltIns.JsonObject();");
                        _output.AppendLine();
                        
                        // Add addTask method
                        WriteIndent();
                        _output.AppendLine("__progressCtx.Set(\"addTask\", MaldaLang.Interpreter.RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue(null, null, false, null)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("BuiltInInstance = __progressCtxWrapper,");
                        WriteIndent();
                        _output.AppendLine("BuiltInMethod = \"addTask\"");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}));");
                        _output.AppendLine();
                        
                        // Add increment method
                        WriteIndent();
                        _output.AppendLine("__progressCtx.Set(\"increment\", MaldaLang.Interpreter.RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue(null, null, false, null)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("BuiltInInstance = __progressCtxWrapper,");
                        WriteIndent();
                        _output.AppendLine("BuiltInMethod = \"increment\"");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}));");
                        _output.AppendLine();
                        
                        // Add isFinished method
                        WriteIndent();
                        _output.AppendLine("__progressCtx.Set(\"isFinished\", MaldaLang.Interpreter.RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue(null, null, false, null)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("BuiltInInstance = __progressCtxWrapper,");
                        WriteIndent();
                        _output.AppendLine("BuiltInMethod = \"isFinished\"");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}));");
                        _output.AppendLine();
                        
                        // Transpile the lambda body with the wrapper context
                        WriteIndent();
                        _output.Append("// Execute callback with progress context wrapper");
                        _output.AppendLine();
                        if (callbackLambda.Parameters.Count > 0)
                        {
                            WriteIndent();
                            _output.Append("var ");
                            _output.Append(EscapeIdentifier(callbackLambda.Parameters[0]));
                            _output.Append(" = MaldaLang.Interpreter.RuntimeValue.Object(__progressCtx);");
                            _output.AppendLine();
                        }
                        
                        // Transpile lambda body
                        if (callbackLambda.BlockBody != null)
                        {
                            foreach (var stmt in callbackLambda.BlockBody.Statements)
                            {
                                WriteIndent();
                                TranspileStatement(stmt);
                            }
                        }
                        else if (callbackLambda.ExpressionBody != null)
                        {
                            WriteIndent();
                            _output.Append("await ");
                            TranspileExpression(callbackLambda.ExpressionBody);
                            _output.AppendLine(";");
                        }
                        
                        _indentLevel--;
                        WriteIndent();
                        _output.Append("}");
                        return;
                    }
                    
                    // Check if the first argument is an object literal with a lambda callback (old syntax)
                    LambdaExpression? actionLambda = null;
                    if (call.Arguments[0] is ObjectLiteralExpression objLit)
                    {
                        // Look for "action" property that is a lambda
                        foreach (var (key, value) in objLit.Properties)
                        {
                            if (key is LiteralExpression keyLit && keyLit.Value is string keyStr && keyStr == "action")
                            {
                                if (value is LambdaExpression lambda)
                                {
                                    actionLambda = lambda;
                                }
                                break;
                            }
                        }
                    }
                    
                    // The first argument is an object with "tasks" array and optional "action" callback
                    _output.Append("await Spectre.Console.AnsiConsole.Progress().StartAsync(async ctx =>");
                    _output.AppendLine();
                    WriteIndent();
                    _output.Append("{");
                    _indentLevel++;
                    _output.AppendLine();
                    
                    // Extract tasks array and action from the object
                    WriteIndent();
                    _output.Append("var __progressObj = RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(call.Arguments[0]);
                    _output.AppendLine(").AsObject() as MaldaLang.BuiltIns.JsonObject;");
                    
                    WriteIndent();
                    _output.AppendLine("var __tasksValue = __progressObj?.Get(\"tasks\", null);");
                    WriteIndent();
                    _output.AppendLine("var __tasksArray = __tasksValue != null && __tasksValue.Type == MaldaLang.Interpreter.ValueType.Array ? __tasksValue.AsArray() : new MaldaLang.Interpreter.ArrayInstance();");
                    WriteIndent();
                    _output.AppendLine("var __progressTasks = new System.Collections.Generic.List<Spectre.Console.ProgressTask>();");
                    _output.AppendLine();
                    
                    // Create progress tasks
                    WriteIndent();
                    _output.AppendLine("foreach (var __task in __tasksArray)");
                    WriteIndent();
                    _output.AppendLine("{");
                    _indentLevel++;
                    WriteIndent();
                    _output.AppendLine("if (__task.Type == MaldaLang.Interpreter.ValueType.Object && __task.AsObject() is MaldaLang.BuiltIns.JsonObject __taskObj)");
                    WriteIndent();
                    _output.AppendLine("{");
                    _indentLevel++;
                    WriteIndent();
                    _output.AppendLine("var __taskNameValue = __taskObj.Get(\"name\", null);");
                    WriteIndent();
                    _output.AppendLine("var __taskName = __taskNameValue != null && __taskNameValue.Type == MaldaLang.Interpreter.ValueType.String ? __taskNameValue.AsString() : \"\";");
                    WriteIndent();
                    _output.AppendLine("var __maxValueValue = __taskObj.Get(\"maxValue\", null);");
                    WriteIndent();
                    _output.AppendLine("var __maxValue = __maxValueValue != null && __maxValueValue.Type == MaldaLang.Interpreter.ValueType.Integer ? __maxValueValue.AsInteger() : (__maxValueValue != null && __maxValueValue.Type == MaldaLang.Interpreter.ValueType.Float ? (int)__maxValueValue.AsFloat() : 100);");
                    WriteIndent();
                    _output.AppendLine("var __progressTask = ctx.AddTask(__taskName, maxValue: __maxValue);");
                    WriteIndent();
                    _output.AppendLine("__progressTasks.Add(__progressTask);");
                    _indentLevel--;
                    WriteIndent();
                    _output.AppendLine("}");
                    _indentLevel--;
                    WriteIndent();
                    _output.AppendLine("}");
                    _output.AppendLine();
                    
                    // Execute action callback if provided
                    if (actionLambda != null)
                    {
                        // We have a lambda in the AST - transpile it directly
                        WriteIndent();
                        _output.AppendLine("// Create progress wrapper object for callback");
                        WriteIndent();
                        _output.AppendLine("var __progressWrapper = new MaldaLang.BuiltIns.JsonObject();");
                        WriteIndent();
                        _output.AppendLine("for (int __i = 0; __i < __progressTasks.Count; __i++)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("var __taskName = $\"Task{__i}\";");
                        WriteIndent();
                        _output.AppendLine("if (__i < __tasksArray.Count && __tasksArray[__i].Type == MaldaLang.Interpreter.ValueType.Object && __tasksArray[__i].AsObject() is MaldaLang.BuiltIns.JsonObject __t)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("var __nameValue = __t.Get(\"name\", null);");
                        WriteIndent();
                        _output.AppendLine("if (__nameValue != null && __nameValue.Type == MaldaLang.Interpreter.ValueType.String)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("__taskName = __nameValue.AsString();");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        WriteIndent();
                        _output.AppendLine("__progressWrapper.Set(__taskName, MaldaLang.Interpreter.RuntimeValue.Integer((int)__progressTasks[__i].Value));");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        _output.AppendLine();
                        
                        // Transpile the lambda body directly
                        WriteIndent();
                        _output.Append("// Call callback with progress object");
                        _output.AppendLine();
                        WriteIndent();
                        if (actionLambda.Parameters.Count > 0)
                        {
                            _output.Append("var ");
                            _output.Append(EscapeIdentifier(actionLambda.Parameters[0]));
                            _output.Append(" = MaldaLang.Interpreter.RuntimeValue.Object(__progressWrapper);");
                            _output.AppendLine();
                        }
                        
                        // Transpile lambda body
                        if (actionLambda.BlockBody != null)
                        {
                            foreach (var stmt in actionLambda.BlockBody.Statements)
                            {
                                WriteIndent();
                                TranspileStatement(stmt);
                            }
                        }
                        else if (actionLambda.ExpressionBody != null)
                        {
                            WriteIndent();
                            _output.Append("await ");
                            TranspileExpression(actionLambda.ExpressionBody);
                            _output.AppendLine(";");
                        }
                    }
                    else
                    {
                        // Fallback: extract action at runtime and call it
                        WriteIndent();
                        _output.AppendLine("var __actionValue = __progressObj?.Get(\"action\", null);");
                        WriteIndent();
                        _output.AppendLine("if (__actionValue != null && __actionValue.Type == MaldaLang.Interpreter.ValueType.Function)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("// Create progress wrapper object");
                        WriteIndent();
                        _output.AppendLine("var __progressWrapper = new MaldaLang.BuiltIns.JsonObject();");
                        WriteIndent();
                        _output.AppendLine("for (int __i = 0; __i < __progressTasks.Count; __i++)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("var __taskName = $\"Task{__i}\";");
                        WriteIndent();
                        _output.AppendLine("if (__i < __tasksArray.Count && __tasksArray[__i].Type == MaldaLang.Interpreter.ValueType.Object && __tasksArray[__i].AsObject() is MaldaLang.BuiltIns.JsonObject __t)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("var __nameValue = __t.Get(\"name\", null);");
                        WriteIndent();
                        _output.AppendLine("if (__nameValue != null && __nameValue.Type == MaldaLang.Interpreter.ValueType.String)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        _output.AppendLine("__taskName = __nameValue.AsString();");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        WriteIndent();
                        _output.AppendLine("__progressWrapper.Set(__taskName, MaldaLang.Interpreter.RuntimeValue.Integer((int)__progressTasks[__i].Value));");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                        WriteIndent();
                        _output.AppendLine("// Note: Function callbacks in transpiled code require runtime function calling");
                        WriteIndent();
                        _output.AppendLine("// This may not work for all cases - prefer using lambda expressions");
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                    }
                    
                    _indentLevel--;
                    WriteIndent();
                    _output.Append("}");
                    return;
                }
            }
            
            // Special handling for ui.* methods
            if (memberAccess2.Object is IdentifierExpression uiIdExpr && uiIdExpr.Name == "ui")
            {
                var uiMethodName = memberAccess2.Member;
                var builtInMethodName = "ui" + char.ToUpper(uiMethodName[0]) + uiMethodName.Substring(1);
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(");
                if (uiMethodName == "generate")
                {
                    if (_canAwait)
                        _output.Append("await MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltInAsync(\"");
                    else
                        _output.Append("MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltInAsync(\"");
                }
                else
                {
                    _output.Append("MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                }
                _output.Append(builtInMethodName);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { ");
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(call.Arguments[i]);
                    _output.Append(")");
                }
                if (uiMethodName == "generate" && !_canAwait)
                    _output.Append(" }, null).GetAwaiter().GetResult())");
                else
                    _output.Append(" }, null))");
                return;
            }
            
            // Extension-style string methods (receiver may be string primitive: s.upper(), s.substring(i, len), etc.)
            if (IsStringExtensionMethod(memberAccess2.Member))
            {
                TranspileStringExtensionCall(memberAccess2.Object, memberAccess2.Member, call.Arguments);
                return;
            }
            
            // For other ObjectInstance methods, use CallObjectMethod
            if (call.Callee is MemberAccessExpression memberAccess3 && memberAccess3.Object != null)
            {
                var directTargets = FindDirectMethodTargets(memberAccess3.Object, memberAccess3.Member, call.Arguments.Count);
                if (directTargets.Count > 0)
                {
                    // `recv is Vec __dmN ? (object)__dmN.m(args) : <reflective call>`: the
                    // receiver is a plain identifier or `this`, so testing it twice is safe.
                    // RuntimeHelpers.DirectCall only passes the value through, so the
                    // conditional is also valid as an expression statement (CS0201).
                    _output.Append("RuntimeHelpers.DirectCall(");
                    foreach (var target in directTargets)
                    {
                        var variable = $"__dm{_directMethodCallCounter++}";
                        _output.Append("((object)");
                        TranspileExpression(memberAccess3.Object);
                        _output.Append(") is ");
                        _output.Append(EscapeIdentifier(target));
                        _output.Append(" ");
                        _output.Append(variable);
                        if (_typedTwins.TryGetValue(target + "." + memberAccess3.Member, out var directSignature) &&
                            directSignature.Parameters.Length == call.Arguments.Count &&
                            TypedTwinArgumentsFit(directSignature, call.Arguments))
                        {
                            // The receiver's type is only known in this branch, so box the result.
                            _output.Append(directSignature.Return == TranspiledClrType.Int ? " ? RuntimeHelpers.BoxInt(" : " ? (object)(");
                            _output.Append(variable);
                            _output.Append(".");
                            _output.Append(GetTypedTwinName(memberAccess3.Member));
                            _output.Append("(");
                            EmitTypedMethodArguments(call.Arguments, directSignature);
                            _output.Append(")) : ");
                            continue;
                        }
                        _output.Append(" ? (object)");
                        _output.Append(variable);
                        _output.Append(".");
                        _output.Append(EscapeIdentifier(memberAccess3.Member));
                        _output.Append("(");
                        EmitObjectMethodArguments(call.Arguments);
                        _output.Append(") : ");
                    }
                }

                var callHelper = IsClassReference(memberAccess3.Object)
                    ? "RuntimeHelpers.CallStaticMethod"
                    : "RuntimeHelpers.CallObjectMethod";
                if (_canAwait)
                    _output.Append($"await {callHelper}(");
                else
                    _output.Append($"RuntimeHelpers.BlockOn({callHelper}(");
                TranspileReceiver(memberAccess3.Object);
                _output.Append(", \"");
                _output.Append(memberAccess3.Member);
                _output.Append("\", new List<object> { ");
                EmitObjectMethodArguments(call.Arguments);
                _output.Append(" }, ");
                EmitAccessingClassArg();
                _output.Append(")");
                if (!_canAwait)
                    _output.Append(")");
                if (directTargets.Count > 0)
                    _output.Append(")");
                return;
            }
        }
        
        // Regular function call
        // Check if this is a user-defined function vs built-in vs function parameter
        if (call.Callee is IdentifierExpression idExpr)
        {
            var funcName = idExpr.Name;
            
            // If it's a built-in, handle it
            if (IsBuiltInFunction(funcName))
            {
                TranspileBuiltInFunction(funcName, call.Arguments);
                return;
            }

            // A helper/handler of the enclosing actor: actor members are synchronous
            // instance methods returning object, so call them directly.
            if (_currentActorMethodNames != null && _currentActorMethodNames.Contains(funcName))
            {
                if (_transpileCallAsTask)
                    _output.Append("System.Threading.Tasks.Task.FromResult(RuntimeHelpers.ToRuntimeValue(");
                _output.Append(EscapeIdentifier(funcName));
                _output.Append("(");
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    TranspileExpression(call.Arguments[i]);
                }
                _output.Append(")");
                if (_transpileCallAsTask)
                    _output.Append("))");
                return;
            }

            // If it's a known prompt name, call it directly (prompts are synchronous)
            if (_promptNames.Contains(funcName))
            {
                _output.Append(EscapeIdentifier(funcName));
                _output.Append("(");
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    TranspileExpression(call.Arguments[i]);
                }
                _output.Append(")");
                return;
            }
            
            // If it's a known function name, call it directly
            if (IsKnownUserFunction(funcName))
            {
                TryGetKnownFunctionParameterTypes(funcName, out var typedParameters);
                if (_transpileCallAsTask)
                {
                    _output.Append("RuntimeHelpers.WrapObjectTaskAsRuntimeValueTask(");
                    _output.Append(EscapeIdentifier(funcName));
                    _output.Append("(");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        var paramType = (typedParameters != null && i < typedParameters.Count)
                            ? typedParameters[i]
                            : TranspiledClrType.Object;
                        EmitCallableArgument(call.Arguments[i], paramType);
                    }
                    _output.Append("))");
                }
                else if (TryGetTypedTwinCall(funcName, call.Arguments, out var typedSignature))
                {
                    _output.Append(GetTypedTwinName(funcName));
                    _output.Append("(");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        if (typedSignature.Parameters[i] is TranspiledClrType.Int or TranspiledClrType.Double)
                            TranspileExpression(call.Arguments[i]);
                        else
                            EmitCallableArgument(call.Arguments[i], typedSignature.Parameters[i]);
                    }
                    _output.Append(")");
                }
                else if (HasSyncTwin(funcName))
                {
                    _output.Append(GetSyncTwinName(funcName));
                    _output.Append("(");
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        var paramType = (typedParameters != null && i < typedParameters.Count)
                            ? typedParameters[i]
                            : TranspiledClrType.Object;
                        EmitCallableArgument(call.Arguments[i], paramType);
                    }
                    _output.Append(")");
                }
                else
                {
                    if (_canAwait)
                    {
                        _output.Append("await ");
                        _output.Append(EscapeIdentifier(funcName));
                        _output.Append("(");
                    }
                    else
                    {
                        _output.Append("RuntimeHelpers.BlockOn(");
                        _output.Append(EscapeIdentifier(funcName));
                        _output.Append("(");
                    }
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        var paramType = (typedParameters != null && i < typedParameters.Count)
                            ? typedParameters[i]
                            : TranspiledClrType.Object;
                        EmitCallableArgument(call.Arguments[i], paramType);
                    }
                    _output.Append(")");
                    if (!_canAwait)
                        _output.Append(")");
                }
                return;
            }
            
            // Otherwise, it's likely a function parameter - use helper
            // For single-parameter functions, use CallFunction helper
            if (call.Arguments.Count == 1)
            {
                if (_transpileCallAsTask)
                    _output.Append("RuntimeHelpers.WrapObjectTaskAsRuntimeValueTask(RuntimeHelpers.CallFunction(");
                else
                    _output.Append(_canAwait ? "await RuntimeHelpers.CallFunction(" : "RuntimeHelpers.BlockOn(RuntimeHelpers.CallFunction(");
                _output.Append(EscapeIdentifier(funcName));
                _output.Append(", ");
                TranspileExpression(call.Arguments[0]);
                _output.Append(_transpileCallAsTask ? "))" : ")");
                if (!_transpileCallAsTask && !_canAwait)
                    _output.Append(")");
                return;
            }
            
            // Zero or several arguments: f() passes null to the one-argument shape (zero-arg
            // lambdas ignore it); f(a, b, ...) forwards every argument through CallFunctionN.
            var helper = call.Arguments.Count == 0 ? "CallFunction" : "CallFunctionN";
            if (_transpileCallAsTask)
                _output.Append($"RuntimeHelpers.WrapObjectTaskAsRuntimeValueTask(RuntimeHelpers.{helper}(");
            else
                _output.Append(_canAwait ? $"await RuntimeHelpers.{helper}(" : $"RuntimeHelpers.BlockOn(RuntimeHelpers.{helper}(");
            _output.Append(EscapeIdentifier(funcName));
            _output.Append(", ");
            if (call.Arguments.Count == 0)
            {
                _output.Append("null");
            }
            else
            {
                _output.Append("new object?[] { ");
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    TranspileExpression(call.Arguments[i]);
                }
                _output.Append(" }");
            }
            _output.Append(_transpileCallAsTask ? "))" : ")");
            if (!_transpileCallAsTask && !_canAwait)
                _output.Append(")");
            return;
        }
        
        // Fallback for other expression types as callee (e.g., lambda expressions)
        // Other expression types (shouldn't happen often)
        if (_transpileCallAsTask)
            _output.Append("RuntimeHelpers.WrapObjectTaskAsRuntimeValueTask(");
        else
            _output.Append(_canAwait ? "await " : "RuntimeHelpers.BlockOn(");
        TranspileExpression(call.Callee);
        _output.Append("(");
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            TranspileExpression(call.Arguments[i]);
        }
        _output.Append(_transpileCallAsTask ? "))" : ")");
        if (!_transpileCallAsTask && !_canAwait)
            _output.Append(")");
    }

    private bool IsBuiltInFunction(string name)
    {
        return BuiltInRegistry.IsTranspilerBuiltIn(name)
            || OptionalPackTranspilerBuiltIns.IsName(name);
    }

    /// <summary>
    /// Unary user functions must be passed as delegates so ToRuntimeValue can wrap them.
    /// Method groups are not objects; lambdas already transpile as Func&lt;object, Task&lt;object&gt;&gt;.
    /// </summary>
    private void EmitToRuntimeValueArgument(Expression argument)
    {
        if (argument is IdentifierExpression id
            && IsKnownUserFunction(id.Name)
            && TryGetKnownFunctionParameterTypes(id.Name, out var paramTypes)
            && paramTypes.Count == 1)
        {
            _output.Append("RuntimeHelpers.ToRuntimeValue((System.Func<object, System.Threading.Tasks.Task<object>>)");
            _output.Append(EscapeIdentifier(id.Name));
            _output.Append(")");
            return;
        }

        _output.Append("RuntimeHelpers.ToRuntimeValue(");
        TranspileExpression(argument);
        _output.Append(")");
    }

    private static bool IsStringExtensionMethod(string name)
    {
        return name == "length" ||
               name == "upper" ||
               name == "lower" ||
               name == "trim" ||
               name == "substring" ||
               name == "indexOf" ||
               name == "replace" ||
               name == "split" ||
               name == "startsWith" ||
               name == "endsWith" ||
               name == "padStart" ||
               name == "padEnd" ||
               name == "repeat";
    }

    private bool ExpressionContainsAwaitingCall(Expression expression)
    {
        return expression switch
        {
            AwaitExpression => true,
            AsyncExpression => true,
            ReceiveExpression => true,
            SpawnExpression spawn => spawn.Arguments.Any(ExpressionContainsAwaitingCall),
            NewExpression created => created.Arguments.Any(ExpressionContainsAwaitingCall),
            FunctionCallExpression call => FunctionCallContainsAwait(call),
            BinaryExpression binary => ExpressionContainsAwaitingCall(binary.Left) || ExpressionContainsAwaitingCall(binary.Right),
            UnaryExpression unary => ExpressionContainsAwaitingCall(unary.Right),
            PostfixExpression postfix => ExpressionContainsAwaitingCall(postfix.Left),
            TernaryExpression ternary => ExpressionContainsAwaitingCall(ternary.Condition) || ExpressionContainsAwaitingCall(ternary.ThenBranch) || ExpressionContainsAwaitingCall(ternary.ElseBranch),
            ArrayLiteralExpression array => array.Elements.Any(ExpressionContainsAwaitingCall),
            ObjectLiteralExpression obj => obj.Properties.Any(pair => ExpressionContainsAwaitingCall(pair.Key) || ExpressionContainsAwaitingCall(pair.Value)),
            DictionaryLiteralExpression dictionary => dictionary.Entries.Any(pair => ExpressionContainsAwaitingCall(pair.Key) || ExpressionContainsAwaitingCall(pair.Value)),
            ArrayAccessExpression access => ExpressionContainsAwaitingCall(access.Array) || ExpressionContainsAwaitingCall(access.Index),
            MemberAccessExpression member => ExpressionContainsAwaitingCall(member.Object),
            InterpolatedStringExpression interpolated => interpolated.Segments.Any(segment => segment.IsExpression && segment.Expression != null && ExpressionContainsAwaitingCall(segment.Expression)),
            GraphLiteralExpression graph => (graph.NodesExpression != null && ExpressionContainsAwaitingCall(graph.NodesExpression)) || (graph.EdgesExpression != null && ExpressionContainsAwaitingCall(graph.EdgesExpression)),
            MatchExpression match => true,
            LambdaExpression => false,
            _ => false
        };
    }

    private bool FunctionCallContainsAwait(FunctionCallExpression call)
    {
        if (call.Callee is IdentifierExpression identifier)
        {
            var descriptor = BuiltInRegistry.GetDescriptor(identifier.Name);
            if (descriptor?.IsAlwaysSynchronousForCodegen == true)
            {
                return call.Arguments.Any(ExpressionContainsAwaitingCall);
            }
        }

        if (call.Callee is MemberAccessExpression memberAccess &&
            IsStringExtensionMethod(memberAccess.Member))
        {
            return ExpressionContainsAwaitingCall(memberAccess.Object) ||
                   call.Arguments.Any(ExpressionContainsAwaitingCall);
        }

        return true;
    }

    private void TranspileStringExtensionCall(Expression objectExpr, string methodName, List<Expression> arguments)
    {
        switch (methodName)
        {
            case "length":
                _output.Append("(object)(RuntimeHelpers.CoerceToString(");
                TranspileExpression(objectExpr);
                _output.Append(").Length)");
                break;
            case "upper":
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(objectExpr);
                _output.Append(").ToUpper()");
                break;
            case "lower":
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(objectExpr);
                _output.Append(").ToLower()");
                break;
            case "trim":
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(objectExpr);
                _output.Append(").Trim()");
                break;
            case "indexOf":
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(objectExpr);
                _output.Append(").IndexOf(RuntimeHelpers.CoerceToString(");
                if (arguments.Count > 0)
                    TranspileExpression(arguments[0]);
                _output.Append("))");
                break;
            case "substring":
            case "replace":
            case "split":
            case "startsWith":
            case "endsWith":
            case "padStart":
            case "padEnd":
            case "repeat":
                _output.Append("RuntimeHelpers.UnwrapRuntimeValue(MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"");
                _output.Append(methodName);
                _output.Append("\", new List<MaldaLang.Interpreter.RuntimeValue> { RuntimeHelpers.ToRuntimeValue(");
                TranspileExpression(objectExpr);
                _output.Append(")");
                for (int i = 0; i < arguments.Count; i++)
                {
                    _output.Append(", RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(arguments[i]);
                    _output.Append(")");
                }
                _output.Append(" }, null))");
                break;
            default:
                throw new Exception($"Unknown string extension method: {methodName}");
        }
    }
}
