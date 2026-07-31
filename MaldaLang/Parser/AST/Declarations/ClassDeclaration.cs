// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Statements;

public class ClassDeclaration : Statement
{
    public string Name { get; }
    public string? Superclass { get; }
    public List<ClassMember> Members { get; }
    public bool IsExported { get; }
    
    public ClassDeclaration(string name, string? superclass, List<ClassMember> members, bool isExported = false, int line = 0, int column = 0)
        : base(line, column)
    {
        Name = name;
        IsExported = isExported;
        Superclass = superclass;
        Members = members;
    }
}

public class ClassMember
{
    public AccessModifier Access { get; }
    public bool IsStatic { get; }
    public MemberType Type { get; }
    public string Name { get; }
    public object? Value { get; } // For fields: initializer expression, for methods: FunctionDeclaration
    public string? TypeHint { get; } // Optional type hint for fields
    
    public ClassMember(AccessModifier access, bool isStatic, MemberType type, string name, object? value = null, string? typeHint = null)
    {
        Access = access;
        IsStatic = isStatic;
        Type = type;
        Name = name;
        Value = value;
        TypeHint = typeHint;
    }
}

public enum AccessModifier
{
    Public,
    Private,
    Default
}

public enum MemberType
{
    Field,
    Method,
    Constructor
}