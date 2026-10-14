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
    private void TranspileFunction(FunctionDeclaration funcDecl)
    {
        var returnType = ResolveTranspiledTypeHint(funcDecl.ReturnType);

        // Transpile decorators
        if (funcDecl.Decorators != null && funcDecl.Decorators.Count > 0)
        {
            foreach (var decorator in funcDecl.Decorators)
            {
                WriteIndent();
                TranspileDecorator(decorator);
                _output.AppendLine();
            }
        }

        WriteIndent();
        _output.Append("static async Task<");
        _output.Append(GetClrTypeName(returnType));
        _output.Append("> ");
        _output.Append(EscapeIdentifier(funcDecl.Name));
        _output.Append("(");

        PushTypedScope();
        for (int i = 0; i < funcDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");

            // Transpile parameter decorators
            // Note: ParameterDecorators is a flat list where decorators for parameter i are at index i
            if (funcDecl.ParameterDecorators != null && i < funcDecl.ParameterDecorators.Count)
            {
                var decorator = funcDecl.ParameterDecorators[i];
                if (decorator != null)
                {
                    TranspileDecorator(decorator);
                    _output.Append(" ");
                }
            }

            var parameterType = (funcDecl.ParameterTypeHints != null && i < funcDecl.ParameterTypeHints.Count)
                ? ResolveTranspiledTypeHint(funcDecl.ParameterTypeHints[i])
                : TranspiledClrType.Object;
            RegisterTypedVariable(funcDecl.Parameters[i], parameterType);
            _output.Append(GetClrTypeName(parameterType));
            _output.Append(" ");
            _output.Append(EscapeIdentifier(funcDecl.Parameters[i]));
        }
        
        _output.Append(")");
        AppendComment(nameof(TranspileFunction));
        _output.AppendLine();
        var previousCanAwait = _canAwait;
        _canAwait = true;
        _currentFunctionReturnType.Push(returnType);
        var previousLocals = EnterLocals(funcDecl, typed: false);
        TranspileFunctionBlock(funcDecl.Body, funcDecl.Name, funcDecl.Line, appendImplicitNullReturn: true,
            budget: DeclarationBounds.TryGetResourceBudget(funcDecl));
        _activeLocals = previousLocals;
        _currentFunctionReturnType.Pop();
        PopTypedScope();
        _canAwait = previousCanAwait;

        if (_syncTwinFunctions != null && _syncTwinFunctions.Contains(funcDecl.Name))
        {
            TranspileSyncTwin(funcDecl, returnType);
        }

        if (_typedTwins.TryGetValue(funcDecl.Name, out var typedSignature))
        {
            TranspileTypedTwin(funcDecl, typedSignature);
        }
    }

    /// <summary>
    /// Emits a block-scoped MALDA function as a C# local function so it closes over
    /// the surrounding locals. Top-level functions are still static methods.
    /// </summary>
    private void TranspileLocalFunction(FunctionDeclaration funcDecl)
    {
        if (ShaderFunction.IsMarked(funcDecl))
            return;

        RegisterLocalFunction(funcDecl);
        var returnType = ResolveTranspiledTypeHint(funcDecl.ReturnType);

        WriteIndent();
        _output.Append("async Task<");
        _output.Append(GetClrTypeName(returnType));
        _output.Append("> ");
        _output.Append(EscapeIdentifier(funcDecl.Name));
        _output.Append("(");

        PushTypedScope();
        for (int i = 0; i < funcDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");

            var parameterType = (funcDecl.ParameterTypeHints != null && i < funcDecl.ParameterTypeHints.Count)
                ? ResolveTranspiledTypeHint(funcDecl.ParameterTypeHints[i])
                : TranspiledClrType.Object;
            RegisterTypedVariable(funcDecl.Parameters[i], parameterType);
            _output.Append(GetClrTypeName(parameterType));
            _output.Append(" ");
            _output.Append(EscapeIdentifier(funcDecl.Parameters[i]));
        }

        _output.Append(")");
        AppendComment(nameof(TranspileLocalFunction));
        _output.AppendLine();
        var previousCanAwait = _canAwait;
        _canAwait = true;
        _currentFunctionReturnType.Push(returnType);
        TranspileFunctionBlock(funcDecl.Body, funcDecl.Name, funcDecl.Line, appendImplicitNullReturn: true,
            budget: DeclarationBounds.TryGetResourceBudget(funcDecl));
        _currentFunctionReturnType.Pop();
        PopTypedScope();
        _canAwait = previousCanAwait;
    }

    private void PushLocalFunctionScope() =>
        _localFunctionStack.Push(new Dictionary<string, IReadOnlyList<TranspiledClrType>>(StringComparer.Ordinal));

    private void PopLocalFunctionScope()
    {
        if (_localFunctionStack.Count > 0)
            _localFunctionStack.Pop();
    }

    private void RegisterLocalFunction(FunctionDeclaration funcDecl)
    {
        if (_localFunctionStack.Count == 0)
            PushLocalFunctionScope();

        var parameterTypes = new List<TranspiledClrType>(funcDecl.Parameters.Count);
        for (int i = 0; i < funcDecl.Parameters.Count; i++)
        {
            parameterTypes.Add(
                funcDecl.ParameterTypeHints != null && i < funcDecl.ParameterTypeHints.Count
                    ? ResolveTranspiledTypeHint(funcDecl.ParameterTypeHints[i])
                    : TranspiledClrType.Object);
        }

        _localFunctionStack.Peek()[funcDecl.Name] = parameterTypes;
    }

    private bool IsKnownUserFunction(string name)
    {
        foreach (var frame in _localFunctionStack)
        {
            if (frame.ContainsKey(name))
                return true;
        }

        return _functionNames.Contains(name);
    }

    private bool TryGetKnownFunctionParameterTypes(string name, out IReadOnlyList<TranspiledClrType> types)
    {
        foreach (var frame in _localFunctionStack)
        {
            if (frame.TryGetValue(name, out types!))
                return true;
        }

        return _functionParameterTypes.TryGetValue(name, out types!);
    }
}
