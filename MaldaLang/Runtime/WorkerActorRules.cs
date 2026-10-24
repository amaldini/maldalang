// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.Actors;

using MaldaLang.BuiltIns;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// What a <c>@worker</c> actor may use, checked the same way before the interpreter, C# transpile
/// and JavaScript run a program. A worker has its own copy of the program's functions, classes
/// and constants but none of its variables (on JavaScript it is a Web Worker that never runs the
/// top-level code), so an actor that reads or writes a top-level <c>var</c>, directly or through
/// a function or class it uses, would behave differently per backend. <c>import js</c> bindings
/// are not loaded into a worker.
/// </summary>
public static class WorkerActorRules
{
    public sealed record Violation(string Message, int Line);

    public static IReadOnlyList<Violation> Check(IEnumerable<Statement> program)
    {
        var statements = program.ToList();
        var workers = statements.OfType<ActorDeclaration>().Where(a => a.IsWorker).ToList();
        if (workers.Count == 0)
            return Array.Empty<Violation>();

        var topVars = statements.OfType<VarDeclStatement>().Where(v => !v.IsConst).Select(v => v.Name)
            .ToHashSet(StringComparer.Ordinal);
        var jsNames = statements.OfType<JsImportStatement>().SelectMany(j => j.Names).ToHashSet(StringComparer.Ordinal);
        var declarations = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case FunctionDeclaration function: declarations.TryAdd(function.Name, function); break;
                case ClassDeclaration klass: declarations.TryAdd(klass.Name, klass); break;
            }
        }

        var violations = new List<Violation>();
        foreach (var worker in workers)
        {
            // The actor itself, then the functions and classes it reaches by name.
            var reported = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<(Node Node, string? Via)>();
            pending.Enqueue((worker, null));
            while (pending.Count > 0)
            {
                var (node, via) = pending.Dequeue();
                var bound = BoundNames(node);
                foreach (var used in AstNodeWalker.Descendants(node))
                {
                    if (used is MemberAccessExpression { Object: IdentifierExpression { Name: "io" } } ioMember
                        && StdLibNamespaces.IoStoreMethodNames.Contains(ioMember.Member)
                        && !bound.Contains("io")
                        && reported.Add("io." + ioMember.Member))
                    {
                        violations.Add(new Violation(
                            $"Worker actor '{worker.Name}' calls io.{ioMember.Member}(). The store and the browser files belong to the main thread: send the value to an actor on the main thread instead.",
                            used.Line > 0 ? used.Line : worker.Line));
                        continue;
                    }

                    var name = used switch
                    {
                        IdentifierExpression identifier => identifier.Name,
                        NewExpression created => created.ClassName,
                        _ => null
                    };
                    if (name == null || bound.Contains(name))
                        continue;

                    if (topVars.Contains(name) && reported.Add(name))
                    {
                        var through = via == null ? string.Empty : $" through '{via}'";
                        violations.Add(new Violation(
                            $"Worker actor '{worker.Name}' uses the top-level variable '{name}'{through}. A worker has its own copy of the program and shares no variables: pass the value in a message or a spawn argument, or declare it with const.",
                            used.Line > 0 ? used.Line : worker.Line));
                    }
                    else if (jsNames.Contains(name) && reported.Add(name))
                    {
                        violations.Add(new Violation(
                            $"Worker actor '{worker.Name}' uses the JavaScript import '{name}'. 'import js' bindings are not loaded into a worker.",
                            used.Line > 0 ? used.Line : worker.Line));
                    }
                    else if (declarations.TryGetValue(name, out var declaration) && visited.Add(name))
                    {
                        pending.Enqueue((declaration, name));
                    }
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Names a declaration binds for itself: fields, members, parameters and every local (a name
    /// declared anywhere inside counts as local everywhere inside, which can only miss a use).
    /// </summary>
    private static HashSet<string> BoundNames(Node declaration)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        switch (declaration)
        {
            case ActorDeclaration actor:
                names.UnionWith(actor.Members.Select(m => m.Name));
                break;
            case ClassDeclaration klass:
                names.UnionWith(klass.Members.Select(m => m.Name));
                break;
        }

        foreach (var node in AstNodeWalker.Descendants(declaration))
        {
            switch (node)
            {
                case VarDeclStatement local: names.Add(local.Name); break;
                case FunctionDeclaration function:
                    names.UnionWith(function.Parameters);
                    if (!ReferenceEquals(function, declaration))
                        names.Add(function.Name);
                    break;
                case LambdaExpression lambda: names.UnionWith(lambda.Parameters); break;
                case ForInStatement forIn: names.Add(forIn.VariableName); break;
                case CatchClause { ExceptionVariable: { } caught }: names.Add(caught); break;
                case SendStatement send:
                    if (send.Callback != null) names.Add(send.Callback.ParameterName);
                    if (send.TimeoutErrorHandler != null) names.Add(send.TimeoutErrorHandler.ParameterName);
                    break;
            }
        }

        return names;
    }
}
