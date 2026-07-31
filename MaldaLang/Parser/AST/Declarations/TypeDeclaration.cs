// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Statements;

public class TypeDeclaration : Statement
{
    public string TypeName { get; }
    public List<VariantConstructor> Constructors { get; }

    public TypeDeclaration(string typeName, List<VariantConstructor> constructors, int line = 0, int column = 0)
        : base(line, column)
    {
        TypeName = typeName;
        Constructors = constructors ?? new List<VariantConstructor>();
    }
}
