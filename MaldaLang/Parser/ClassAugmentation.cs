// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser;

using System;
using System.Collections.Generic;
using System.Linq;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// A later <c>class Name { … }</c> for an already-declared class appends members
/// to the first declaration. Interpreter and both transpilers then see one class.
/// </summary>
internal static class ClassAugmentation
{
    public static void Merge(List<Statement> statements, List<ParseException> errors, string? sourceFileName)
    {
        var firstByName = new Dictionary<string, ClassDeclaration>(StringComparer.Ordinal);
        var remove = new HashSet<ClassDeclaration>();

        foreach (var stmt in statements)
        {
            if (stmt is not ClassDeclaration later)
                continue;

            if (!firstByName.TryGetValue(later.Name, out var first))
            {
                firstByName[later.Name] = later;
                continue;
            }

            TryAppend(first, later, errors, sourceFileName);
            remove.Add(later);
        }

        if (remove.Count == 0)
            return;

        statements.RemoveAll(s => s is ClassDeclaration c && remove.Contains(c));
    }

    private static void TryAppend(
        ClassDeclaration first,
        ClassDeclaration later,
        List<ParseException> errors,
        string? sourceFileName)
    {
        var problems = new List<ParseException>();

        if (later.HasPrimaryConstructor)
        {
            problems.Add(Error(later, sourceFileName,
                $"Class '{later.Name}' is already defined; a later declaration cannot use a primary constructor."));
        }

        if (later.Superclass != null)
        {
            if (first.Superclass == null)
            {
                problems.Add(Error(later, sourceFileName,
                    $"Class '{later.Name}' is already defined; a later declaration cannot add 'extends'."));
            }
            else if (!string.Equals(first.Superclass, later.Superclass, StringComparison.Ordinal))
            {
                problems.Add(Error(later, sourceFileName,
                    $"Class '{later.Name}' is already defined; a later declaration cannot change its superclass from '{first.Superclass}' to '{later.Superclass}'."));
            }
        }

        var existingNames = new HashSet<string>(first.Members.Select(m => m.Name), StringComparer.Ordinal);
        var laterNames = new HashSet<string>(StringComparer.Ordinal);
        var firstHasConstructor = first.Members.Any(m => m.Type == MemberType.Constructor);

        foreach (var member in later.Members)
        {
            if (member.Type == MemberType.Constructor && firstHasConstructor)
            {
                problems.Add(Error(later, sourceFileName,
                    $"Class '{later.Name}' already has a constructor; a later declaration cannot add another."));
            }

            if (!laterNames.Add(member.Name))
            {
                problems.Add(Error(later, sourceFileName,
                    $"Member '{member.Name}' is declared more than once on a later class '{later.Name}'."));
            }

            if (existingNames.Contains(member.Name))
            {
                problems.Add(Error(later, sourceFileName,
                    $"Member '{member.Name}' is already defined on class '{later.Name}'."));
            }
        }

        errors.AddRange(problems);
        if (problems.Count > 0)
            return;

        if (later.IsExported)
            first.IsExported = true;

        first.Members.AddRange(later.Members);
    }

    private static ParseException Error(ClassDeclaration later, string? sourceFileName, string message)
        => new(later.Line, later.Column, message, later.SourceFile ?? sourceFileName);
}
