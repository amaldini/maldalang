// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Maps sum-type declarations and constructor names for exhaustiveness checking.
/// </summary>
public sealed class SumTypeIndex
{
    private readonly Dictionary<string, string> _constructorToType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _typeToConstructors = new(StringComparer.Ordinal);

    public static SumTypeIndex Build(IEnumerable<Statement> statements)
    {
        var index = new SumTypeIndex();
        foreach (var stmt in statements)
            index.Register(stmt);
        return index;
    }

    public bool IsSumType(string typeName) => _typeToConstructors.ContainsKey(typeName);

    public bool TryGetSumTypeForConstructor(string constructorName, out string sumTypeName) =>
        _constructorToType.TryGetValue(constructorName, out sumTypeName!);

    public IReadOnlyList<string> GetConstructors(string sumTypeName) =>
        _typeToConstructors.TryGetValue(sumTypeName, out var ctors) ? ctors : Array.Empty<string>();

    private void Register(Statement stmt)
    {
        switch (stmt)
        {
            case TypeDeclaration typeDecl:
                RegisterType(typeDecl);
                break;
            case FunctionDeclaration funcDecl:
                foreach (var inner in funcDecl.Body.Statements)
                    Register(inner);
                break;
            case ClassDeclaration classDecl:
                foreach (var member in classDecl.Members)
                {
                    if (member.Value is FunctionDeclaration method)
                    {
                        foreach (var inner in method.Body.Statements)
                            Register(inner);
                    }
                }
                break;
        }
    }

    private void RegisterType(TypeDeclaration typeDecl)
    {
        var constructors = new List<string>();
        foreach (var ctor in typeDecl.Constructors)
        {
            constructors.Add(ctor.Name);
            _constructorToType[ctor.Name] = typeDecl.TypeName;
        }

        _typeToConstructors[typeDecl.TypeName] = constructors;
    }
}
