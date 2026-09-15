// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser;

using System;
using System.Collections.Generic;
using System.Linq;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// A later <c>class Name { … }</c> or brace-less <c>class Name function …</c> for an
/// already-declared class appends members to the first declaration. Interpreter and
/// both transpilers then see one class. The REPL applies the same rules when a later
/// entry declares a name already bound in the session.
/// </summary>
internal static class ClassAugmentation
{
    internal readonly struct ExistingClass
    {
        public ExistingClass(string name, string? superclass, bool hasConstructor, IEnumerable<string> memberNames)
        {
            Name = name;
            Superclass = superclass;
            HasConstructor = hasConstructor;
            MemberNames = new HashSet<string>(memberNames, StringComparer.Ordinal);
        }

        public string Name { get; }
        public string? Superclass { get; }
        public bool HasConstructor { get; }
        public HashSet<string> MemberNames { get; }

        public static ExistingClass From(ClassDeclaration first) =>
            new(
                first.Name,
                first.Superclass,
                first.Members.Any(m => m.Type == MemberType.Constructor),
                first.Members.Select(m => m.Name));
    }

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

    /// <summary>
    /// Same merge rules as <see cref="Merge"/>, for a class already materialized in the
    /// interpreter (REPL entries, a second <c>InterpretAsync</c> on one session).
    /// </summary>
    public static List<string> CollectProblems(ExistingClass first, ClassDeclaration later)
    {
        var problems = new List<string>();

        if (later.HasPrimaryConstructor)
        {
            problems.Add($"Class '{later.Name}' is already defined; a later declaration cannot use a primary constructor.");
        }

        if (later.Superclass != null)
        {
            if (first.Superclass == null)
            {
                problems.Add($"Class '{later.Name}' is already defined; a later declaration cannot add 'extends'.");
            }
            else if (!string.Equals(first.Superclass, later.Superclass, StringComparison.Ordinal))
            {
                problems.Add($"Class '{later.Name}' is already defined; a later declaration cannot change its superclass from '{first.Superclass}' to '{later.Superclass}'.");
            }
        }

        var laterNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in later.Members)
        {
            if (member.Type == MemberType.Constructor && first.HasConstructor)
            {
                problems.Add($"Class '{later.Name}' already has a constructor; a later declaration cannot add another.");
            }

            if (!laterNames.Add(member.Name))
            {
                problems.Add($"Member '{member.Name}' is declared more than once on a later class '{later.Name}'.");
            }

            if (first.MemberNames.Contains(member.Name))
            {
                problems.Add($"Member '{member.Name}' is already defined on class '{later.Name}'.");
            }
        }

        return problems;
    }

    private static void TryAppend(
        ClassDeclaration first,
        ClassDeclaration later,
        List<ParseException> errors,
        string? sourceFileName)
    {
        var problems = CollectProblems(ExistingClass.From(first), later);
        foreach (var message in problems)
            errors.Add(Error(later, sourceFileName, message));
        if (problems.Count > 0)
            return;

        if (later.IsExported)
            first.IsExported = true;

        first.Members.AddRange(later.Members);
    }

    private static ParseException Error(ClassDeclaration later, string? sourceFileName, string message)
        => new(later.Line, later.Column, message, later.SourceFile ?? sourceFileName);
}
