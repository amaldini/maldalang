// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Statements;

using MaldaLang.Parser.AST.Expressions;

/// <summary>
/// Represents the callback part of a send statement:
///   send target.handler(args) then (param) { ... }
/// </summary>
public class CallbackDefinition
{
    public string ParameterName { get; }
    public BlockStatement Body { get; }

    public CallbackDefinition(string parameterName, BlockStatement body)
    {
        ParameterName = parameterName;
        Body = body;
    }
}

