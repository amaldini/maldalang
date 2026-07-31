// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Message declarations are used inside actor declarations to describe
// the shape of messages that the actor can handle when using the
// actor-sugar style with `message` declarations and `receive()`-based
// pattern matching.

namespace MaldaLang.Parser.AST.Declarations;

using System.Collections.Generic;

public class MessageDeclaration
{
    public string Name { get; }
    public List<string> ParameterNames { get; }
    public string? ReturnType { get; }

    public MessageDeclaration(string name, List<string> parameterNames, string? returnType = null)
    {
        Name = name;
        ParameterNames = parameterNames ?? new List<string>();
        ReturnType = returnType;
    }
}

