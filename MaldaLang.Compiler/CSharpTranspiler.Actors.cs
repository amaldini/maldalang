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
    private void TranspileActor(ActorDeclaration actorDecl)
    {
        // Generate a concrete C# actor class implementing IActor.
        WriteIndent();
        _output.Append("public class ");
        _output.Append(EscapeIdentifier(actorDecl.Name));
        _output.Append(" : IActor");
        if (actorDecl.Messages != null && actorDecl.Messages.Count > 0)
        {
            _output.Append(", IActorMessageMetadata");
        }
        _output.AppendLine();
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(TranspileActor) + " (open)");
        _output.AppendLine();
        _indentLevel++;

        // Emit actor message declarations (actor sugar) as a comment for documentation.
        if (actorDecl.Messages != null && actorDecl.Messages.Count > 0)
        {
            WriteIndent();
            _output.Append("// Messages: ");
            for (int i = 0; i < actorDecl.Messages.Count; i++)
            {
                if (i > 0)
                {
                    _output.Append(", ");
                }

                var msg = actorDecl.Messages[i];
                _output.Append(msg.Name);
                _output.Append("(");
                for (int p = 0; p < msg.ParameterNames.Count; p++)
                {
                    if (p > 0) _output.Append(", ");
                    _output.Append(msg.ParameterNames[p]);
                }
                _output.Append(")");
                if (!string.IsNullOrEmpty(msg.ReturnType))
                {
                    _output.Append(" -> ");
                    _output.Append(msg.ReturnType);
                }
            }
            _output.Append(";");
            _output.AppendLine();

            WriteIndent();
            _output.Append("public bool IsDeclaredMessage(string name) => ");
            for (int i = 0; i < actorDecl.Messages.Count; i++)
            {
                if (i > 0)
                {
                    _output.Append(" || ");
                }

                _output.Append("string.Equals(name, \"");
                _output.Append(actorDecl.Messages[i].Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
                _output.Append("\", StringComparison.Ordinal)");
            }
            _output.Append(";");
            _output.AppendLine();
        }

        var previousActorMethodNames = _currentActorMethodNames;
        _currentActorMethodNames = actorDecl.Members
            .Where(m => m.Type == MemberType.Method && !m.IsStatic)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var member in actorDecl.Members)
        {
            TranspileActorMember(member);
        }
        _currentActorMethodNames = previousActorMethodNames;

        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(TranspileActor) + " (close)");
        _output.AppendLine();
    }

    private void TranspileActorMember(ClassMember member)
    {
        WriteIndent();

        // Access modifier. MALDA's default is public; emit it explicitly so reflection and
        // inherited/instance member helpers see the same visibility as the interpreter.
        if (member.Access == AccessModifier.Private)
            _output.Append("private ");
        else
            _output.Append("public ");

        if (member.IsStatic)
            _output.Append("static ");

        switch (member.Type)
        {
            case MemberType.Field:
                var fieldType = ResolveTranspiledTypeHint(member.TypeHint);
                _output.Append(GetClrTypeName(fieldType));
                _output.Append(" ");
                _output.Append(member.Name);
                if (member.Value != null && member.Value is Expression expr)
                {
                    _output.Append(" = ");
                    _output.Append(GetCoercionExpressionPrefix(fieldType));
                    TranspileExpression(expr);
                    _output.Append(GetCoercionExpressionSuffix(fieldType));
                }
                _output.AppendLine(";");
                break;

            case MemberType.Method:
                if (member.Value is FunctionDeclaration func)
                {
                    // Transpile method decorators
                    if (func.Decorators != null && func.Decorators.Count > 0)
                    {
                        foreach (var decorator in func.Decorators)
                        {
                            WriteIndent();
                            TranspileDecorator(decorator);
                            _output.AppendLine();
                        }
                    }

                    WriteIndent();
                    // Handlers are fire-and-forget (the runtime ignores the result), but the same
                    // members double as helpers called from other handlers (`count = double(count)`),
                    // so they return object like the interpreter's FunctionValue does.
                    _output.Append("object ");
                    _output.Append(EscapeIdentifier(member.Name));
                    _output.Append("(");
                    PushTypedScope();
                    for (int i = 0; i < func.Parameters.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");

                        // Parameter decorators
                        if (func.ParameterDecorators != null && i < func.ParameterDecorators.Count)
                        {
                            var decorator = func.ParameterDecorators[i];
                            if (decorator != null)
                            {
                                TranspileDecorator(decorator);
                                _output.Append(" ");
                            }
                        }

                        var parameterType = (func.ParameterTypeHints != null && i < func.ParameterTypeHints.Count)
                            ? ResolveTranspiledTypeHint(func.ParameterTypeHints[i])
                            : TranspiledClrType.Object;
                        RegisterTypedVariable(func.Parameters[i], parameterType);
                        _output.Append(GetClrTypeName(parameterType));
                        _output.Append(" ");
                        _output.Append(EscapeIdentifier(func.Parameters[i]));
                    }
                    _output.AppendLine(")");
                    _output.AppendLine();
                    WriteIndent();
                    _output.AppendLine("{");
                    _indentLevel++;
                    
                    // Set flag to indicate we're in an actor handler
                    var previousInActorHandler = _isInActorHandler;
                    _isInActorHandler = true;
                    _currentFunctionReturnType.Push(TranspiledClrType.Object);
                    TranspileBlock(func.Body);
                    WriteIndent();
                    _output.AppendLine("return null;");
                    _currentFunctionReturnType.Pop();
                    PopTypedScope();
                    // Restore flag
                    _isInActorHandler = previousInActorHandler;
                    
                    _indentLevel--;
                    WriteIndent();
                    _output.AppendLine("}");
                }
                break;

            case MemberType.Constructor:
                if (member.Value is FunctionDeclaration ctor)
                {
                    // Transpile constructor decorators
                    if (ctor.Decorators != null && ctor.Decorators.Count > 0)
                    {
                        foreach (var decorator in ctor.Decorators)
                        {
                            WriteIndent();
                            TranspileDecorator(decorator);
                            _output.AppendLine();
                        }
                    }

                    WriteIndent();
                    // Visibility was already emitted above (public unless explicitly private);
                    // public is required for actor spawn.
                    _output.Append(EscapeIdentifier(member.Name));
                    _output.Append("(");
                    PushTypedScope();
                    for (int i = 0; i < ctor.Parameters.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");

                        // Parameter decorators
                        if (ctor.ParameterDecorators != null && i < ctor.ParameterDecorators.Count)
                        {
                            var decorator = ctor.ParameterDecorators[i];
                            if (decorator != null)
                            {
                                TranspileDecorator(decorator);
                                _output.Append(" ");
                            }
                        }

                        var parameterType = (ctor.ParameterTypeHints != null && i < ctor.ParameterTypeHints.Count)
                            ? ResolveTranspiledTypeHint(ctor.ParameterTypeHints[i])
                            : TranspiledClrType.Object;
                        RegisterTypedVariable(ctor.Parameters[i], parameterType);
                        _output.Append(GetClrTypeName(parameterType));
                        _output.Append(" ");
                        _output.Append(EscapeIdentifier(ctor.Parameters[i]));
                    }
                    _output.AppendLine(")");
                    // `self` in the constructor (lambdas included) must be the actor being
                    // built, as in the interpreter, which runs the constructor inside the actor.
                    WriteIndent();
                    _output.AppendLine("{");
                    _indentLevel++;
                    WriteIndent();
                    _output.AppendLine("var __previousSelf = ActorsRuntime.EnterConstruction(this);");
                    WriteIndent();
                    _output.AppendLine("try");
                    _currentFunctionReturnType.Push(TranspiledClrType.Object);
                    TranspileFunctionBlock(ctor.Body, member.Name + ".ctor", ctor.Line);
                    _currentFunctionReturnType.Pop();
                    WriteIndent();
                    _output.AppendLine("finally { ActorsRuntime.ExitConstruction(__previousSelf); }");
                    _indentLevel--;
                    WriteIndent();
                    _output.AppendLine("}");
                    PopTypedScope();
                }
                break;
        }
    }

    
    private void TranspileSpawn(SpawnExpression spawn)
    {
        // Spawn expression: returns an ActorRef boxed as object
        _output.Append("(object)ActorsRuntime.Spawn(new ");
        _output.Append(EscapeIdentifier(spawn.ActorName));
        _output.Append("(");
        for (int i = 0; i < spawn.Arguments.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            TranspileExpression(spawn.Arguments[i]);
        }
        _output.Append("))");
    }

    
    private void TranspileSend(SendStatement send)
    {
        // Special handling for stop(): send target.stop() should call ActorsRuntime.Stop() directly
        if (send.HandlerName == "stop" && send.Arguments.Count == 0 && send.Callback == null && send.TimeoutMilliseconds == null)
        {
            WriteIndent();
            _output.Append("RuntimeHelpers.CallActorOrVoidStop(");
            TranspileExpression(send.Target);
            _output.Append(");");
            AppendComment(nameof(TranspileSend) + " (stop)");
            _output.AppendLine();
            return;
        }

        // Call-style send with callback: send target.handlerName(args...) then (result) { ... };
        if (send.Callback != null)
        {
            WriteIndent();
            _output.Append("{");
            AppendComment(nameof(TranspileSend) + " (call-style with callback)");
            _output.AppendLine();
            _indentLevel++;

            // Evaluate target once and cast to ActorRef
            WriteIndent();
            _output.Append("var __target = (ActorRef)(");
            TranspileExpression(send.Target);
            _output.Append(");");
            _output.AppendLine();

            // Get current actor reference (sender)
            WriteIndent();
            _output.Append("var __self = ActorsRuntime.GetSelf();");
            _output.AppendLine();

            // Build callback delegate
            var callback = send.Callback;
            WriteIndent();
            _output.Append("System.Func<object?, System.Threading.Tasks.Task> __callback = async (object? ");
            _output.Append(EscapeIdentifier(callback.ParameterName));
            _output.Append("Arg) =>");
            _output.AppendLine();
            WriteIndent();
            _output.Append("{");
            _output.AppendLine();
            _indentLevel++;

            // Map MALDA callback parameter name to local variable
            WriteIndent();
            _output.Append("object ");
            _output.Append(EscapeIdentifier(callback.ParameterName));
            _output.Append(" = ");
            _output.Append(EscapeIdentifier(callback.ParameterName));
            _output.Append("Arg;");
            _output.AppendLine();

            // Transpile callback body
            TranspileBlock(callback.Body);

            _indentLevel--;
            WriteIndent();
            _output.Append("};");
            _output.AppendLine();

            // Build timeout error handler delegate if provided
            if (send.TimeoutErrorHandler != null)
            {
                var errorHandler = send.TimeoutErrorHandler;
                WriteIndent();
                _output.Append("System.Func<object?, System.Threading.Tasks.Task>? __timeoutErrorHandler = async (object? ");
                _output.Append(EscapeIdentifier(errorHandler.ParameterName));
                _output.Append("Arg) =>");
                _output.AppendLine();
                WriteIndent();
                _output.Append("{");
                _output.AppendLine();
                _indentLevel++;

                // Map MALDA error handler parameter name to local variable
                WriteIndent();
                _output.Append("object ");
                _output.Append(EscapeIdentifier(errorHandler.ParameterName));
                _output.Append(" = ");
                _output.Append(EscapeIdentifier(errorHandler.ParameterName));
                _output.Append("Arg;");
                _output.AppendLine();

                // Transpile error handler body
                TranspileBlock(errorHandler.Body);

                _indentLevel--;
                WriteIndent();
                _output.Append("};");
                _output.AppendLine();
            }
            else
            {
                WriteIndent();
                _output.Append("System.Func<object?, System.Threading.Tasks.Task>? __timeoutErrorHandler = null;");
                _output.AppendLine();
            }

            // Evaluate timeout milliseconds if provided
            if (send.TimeoutMilliseconds != null)
            {
                WriteIndent();
                _output.Append("int? __timeoutMs = (int)(RuntimeHelpers.CoerceToInt(");
                TranspileExpression(send.TimeoutMilliseconds);
                _output.Append("));");
                _output.AppendLine();
            }
            else
            {
                WriteIndent();
                _output.Append("int? __timeoutMs = null;");
                _output.AppendLine();
            }

            // Send with callback through ActorsRuntime
            WriteIndent();
            _output.Append("ActorsRuntime.SendWithCallback(__self, __target, ");
            if (send.HandlerName != null)
            {
                _output.Append("\"");
                _output.Append(send.HandlerName);
                _output.Append("\"");
            }
            else
            {
                _output.Append("null");
            }
            _output.Append(", __callback, __timeoutMs, __timeoutErrorHandler");
            for (int i = 0; i < send.Arguments.Count; i++)
            {
                _output.Append(", ");
                TranspileExpression(send.Arguments[i]);
            }
            _output.Append(");");
            _output.AppendLine();

            _indentLevel--;
            WriteIndent();
            _output.Append("}");
            AppendComment(nameof(TranspileSend) + " (call-style with callback end)");
            _output.AppendLine();
            return;
        }

        // Call-style send without callback: send target.handlerName(args...);
        WriteIndent();
        _output.Append("{");
        AppendComment(nameof(TranspileSend) + " (call-style)");
        _output.AppendLine();
        _indentLevel++;

        // Evaluate target once and cast to ActorRef
        WriteIndent();
        _output.Append("var __target = (ActorRef)(");
        TranspileExpression(send.Target);
        _output.Append(");");
        _output.AppendLine();

        WriteIndent();
        _output.Append("ActorsRuntime.Send(__target, ");
        if (send.HandlerName != null)
        {
            _output.Append("\"");
            _output.Append(send.HandlerName);
            _output.Append("\"");
        }
        else
        {
            _output.Append("null");
        }
        for (int i = 0; i < send.Arguments.Count; i++)
        {
            _output.Append(", ");
            TranspileExpression(send.Arguments[i]);
        }
        _output.Append(");");
        _output.AppendLine();

        _indentLevel--;
        WriteIndent();
        _output.Append("}");
        AppendComment(nameof(TranspileSend) + " (call-style end)");
        _output.AppendLine();
    }
}
