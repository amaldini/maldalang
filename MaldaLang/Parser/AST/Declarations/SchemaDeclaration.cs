// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Statements;

public sealed class SchemaField
{
    public string Name { get; }
    public string TypeName { get; }
    public bool Required { get; }

    public SchemaField(string name, string typeName, bool required)
    {
        Name = name;
        TypeName = typeName;
        Required = required;
    }
}

/// <summary>
/// Phase 6.2: declarative JSON-schema-like object shape.
/// </summary>
public sealed class SchemaDeclaration : Statement
{
    public string Name { get; }
    public List<SchemaField> Fields { get; }

    public SchemaDeclaration(string name, List<SchemaField> fields, int line = 0, int column = 0)
        : base(line, column)
    {
        Name = name;
        Fields = fields;
    }
}
