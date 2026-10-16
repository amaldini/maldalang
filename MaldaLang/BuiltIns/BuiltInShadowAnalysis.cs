// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Lexical name resolution for flat built-in names. A user binding in scope — a function,
/// variable, constant, parameter, lambda parameter, loop / catch / pattern variable, import,
/// class or actor name, a field of the enclosing class, or a member of the enclosing actor —
/// shadows a flat built-in of the same name; namespaced calls (<c>math.abs(...)</c>) are member
/// accesses and never reach this analysis. Class methods are not unqualified bindings (call
/// them as <c>this.name(...)</c>), so a method named like a built-in does not shadow it.
/// <para>
/// <see cref="Analyze"/> sets <see cref="IdentifierExpression.ShadowsBuiltIn"/> on every use of
/// a built-in name that resolves to such a binding. The interpreter, the C# transpiler, the JS
/// transpiler and the language service all read that flag, so the four agree. Declarations are
/// hoisted to the start of their block (program, function body, block), which matches function
/// hoisting; a variable read before its declaration is the only over-approximation, and the
/// interpreter then still falls back to the built-in because the name is not bound yet.
/// </para>
/// </summary>
public static class BuiltInShadowAnalysis
{
    public sealed class Result
    {
        public static readonly Result Empty = new(new HashSet<string>(StringComparer.Ordinal), new List<IdentifierExpression>());

        public Result(IReadOnlySet<string> programScopeNames, IReadOnlyList<IdentifierExpression> shadowedUses)
        {
            ProgramScopeNames = programScopeNames;
            ShadowedUses = shadowedUses;
        }

        /// <summary>Built-in names the program binds at its top level (functions, variables, imports, ...).</summary>
        public IReadOnlySet<string> ProgramScopeNames { get; }

        /// <summary>Every identifier use marked <see cref="IdentifierExpression.ShadowsBuiltIn"/>.</summary>
        public IReadOnlyList<IdentifierExpression> ShadowedUses { get; }
    }

    /// <summary>Default predicate: a name the interpreter dispatches as a flat built-in.</summary>
    public static bool IsFlatBuiltIn(string name) => BuiltInRegistry.IsInterpreterBuiltIn(name);

    /// <param name="statements">The whole program (includes are already spliced in by the parser).</param>
    /// <param name="isBuiltIn">Which names count as built-ins; defaults to <see cref="IsFlatBuiltIn"/>.</param>
    /// <param name="outerNames">Built-in names already bound outside these statements (REPL lines, merged imports).</param>
    public static Result Analyze(
        IReadOnlyList<Statement> statements,
        Func<string, bool>? isBuiltIn = null,
        IEnumerable<string>? outerNames = null)
    {
        var walker = new Walker(isBuiltIn ?? IsFlatBuiltIn, statements);
        var program = new Scope(null);
        if (outerNames != null)
        {
            foreach (var name in outerNames)
                program.Add(name, walker.IsBuiltIn);
        }

        walker.Hoist(statements, program);
        if (!walker.AnyBuiltInBindingPossible(program))
            return new Result(program.NamesOrEmpty(), Array.Empty<IdentifierExpression>());

        foreach (var statement in statements)
            walker.Visit(statement, program);

        return new Result(program.NamesOrEmpty(), walker.Shadowed);
    }

    private sealed class Scope
    {
        private HashSet<string>? _names;

        public Scope(Scope? parent) => Parent = parent;

        public Scope? Parent { get; }

        public void Add(string? name, Func<string, bool> isBuiltIn)
        {
            if (string.IsNullOrEmpty(name) || !isBuiltIn(name))
                return;
            (_names ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
        }

        public bool Resolves(string name)
        {
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                if (scope._names != null && scope._names.Contains(name))
                    return true;
            }

            return false;
        }

        public bool IsEmptyChain()
        {
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                if (scope._names is { Count: > 0 })
                    return false;
            }

            return true;
        }

        public IReadOnlySet<string> NamesOrEmpty() =>
            _names ?? (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal);
    }

    private sealed class Walker
    {
        private readonly Dictionary<string, ClassDeclaration> _classes = new(StringComparer.Ordinal);
        private bool? _anyNestedBinding;
        private readonly IReadOnlyList<Statement> _statements;

        public Walker(Func<string, bool> isBuiltIn, IReadOnlyList<Statement> statements)
        {
            IsBuiltIn = isBuiltIn;
            _statements = statements;
            foreach (var cls in statements.OfType<ClassDeclaration>())
                _classes[cls.Name] = cls;
        }

        public Func<string, bool> IsBuiltIn { get; }

        public List<IdentifierExpression> Shadowed { get; } = new();

        /// <summary>
        /// Cheap pre-check: when the program scope binds no built-in name and no nested
        /// declaration anywhere uses one, there is nothing to mark.
        /// </summary>
        public bool AnyBuiltInBindingPossible(Scope program)
        {
            if (!program.IsEmptyChain())
                return true;
            _anyNestedBinding ??= _statements.Any(s => AstNodeWalker.Descendants(s).Any(DeclaresBuiltInName));
            return _anyNestedBinding.Value;
        }

        private bool DeclaresBuiltInName(Node node)
        {
            foreach (var name in DeclaredNames(node))
            {
                if (!string.IsNullOrEmpty(name) && IsBuiltIn(name))
                    return true;
            }

            return false;
        }

        /// <summary>Every name a node introduces somewhere (used only by the pre-check).</summary>
        private static IEnumerable<string?> DeclaredNames(Node node)
        {
            switch (node)
            {
                case FunctionDeclaration f:
                    yield return f.Name;
                    foreach (var p in f.Parameters) yield return p;
                    break;
                case LambdaExpression l:
                    foreach (var p in l.Parameters) yield return p;
                    break;
                case PromptDeclaration p:
                    yield return p.Name;
                    foreach (var q in p.Parameters) yield return q;
                    break;
                case WorkflowDeclaration w:
                    yield return w.Name;
                    foreach (var q in w.Parameters) yield return q;
                    break;
                case VarDeclStatement v:
                    yield return v.Name;
                    break;
                case ForInStatement f:
                    yield return f.VariableName;
                    break;
                case CatchClause c:
                    yield return c.ExceptionVariable;
                    break;
                case UsingResourceStatement u:
                    yield return u.VariableName;
                    break;
                case ListComprehensionExpression lc:
                    yield return lc.Variable;
                    break;
                case DictComprehensionExpression dc:
                    yield return dc.Variable;
                    break;
                case ClassDeclaration cls:
                    yield return cls.Name;
                    foreach (var m in cls.Members.Where(m => m.Type == MemberType.Field)) yield return m.Name;
                    break;
                case ActorDeclaration actor:
                    yield return actor.Name;
                    foreach (var m in actor.Members) yield return m.Name;
                    break;
                case ImportStatement import:
                    yield return import.Alias;
                    if (import.SelectedNames != null)
                        foreach (var n in import.SelectedNames) yield return n;
                    break;
                case TypeDeclaration type:
                    foreach (var c in type.Constructors) yield return c.Name;
                    break;
                case Pattern pattern:
                    foreach (var n in PatternNames(pattern)) yield return n;
                    break;
            }
        }

        /// <summary>Adds the names <paramref name="statements"/> declare directly (not in nested blocks).</summary>
        public void Hoist(IEnumerable<Statement> statements, Scope scope)
        {
            foreach (var statement in statements)
            {
                switch (statement)
                {
                    case VarDeclStatement v:
                        scope.Add(v.Name, IsBuiltIn);
                        break;
                    case DestructuringVarDecl d:
                        foreach (var name in PatternNames(d.Pattern))
                            scope.Add(name, IsBuiltIn);
                        break;
                    case FunctionDeclaration f when !ShaderFunction.IsMarked(f):
                        scope.Add(f.Name, IsBuiltIn);
                        break;
                    case ClassDeclaration c:
                        scope.Add(c.Name, IsBuiltIn);
                        break;
                    case ActorDeclaration a:
                        scope.Add(a.Name, IsBuiltIn);
                        break;
                    case PromptDeclaration p:
                        scope.Add(p.Name, IsBuiltIn);
                        break;
                    case WorkflowDeclaration w:
                        scope.Add(w.Name, IsBuiltIn);
                        break;
                    case ImportStatement import:
                        scope.Add(import.Alias, IsBuiltIn);
                        if (import.SelectedNames != null)
                        {
                            foreach (var name in import.SelectedNames)
                                scope.Add(name, IsBuiltIn);
                        }
                        break;
                    case TypeDeclaration type:
                        foreach (var ctor in type.Constructors)
                            scope.Add(ctor.Name, IsBuiltIn);
                        break;
                }
            }
        }

        public void Visit(Node node, Scope scope)
        {
            switch (node)
            {
                case IdentifierExpression id:
                    if (IsBuiltIn(id.Name) && scope.Resolves(id.Name))
                    {
                        id.ShadowsBuiltIn = true;
                        Shadowed.Add(id);
                    }
                    return;

                case FunctionDeclaration f:
                {
                    // @shader() bodies are GLSL: bare dot / clamp / mix are GPU intrinsics.
                    if (ShaderFunction.IsMarked(f))
                        return;
                    var inner = With(scope, f.Parameters);
                    VisitBlock(f.Body, inner);
                    return;
                }

                case LambdaExpression lambda:
                {
                    var inner = With(scope, lambda.Parameters);
                    if (lambda.ExpressionBody != null)
                        Visit(lambda.ExpressionBody, inner);
                    if (lambda.BlockBody != null)
                        VisitBlock(lambda.BlockBody, inner);
                    return;
                }

                case PromptDeclaration prompt:
                    VisitChildren(prompt, With(scope, prompt.Parameters));
                    return;

                case WorkflowDeclaration workflow:
                    VisitChildren(workflow, With(scope, workflow.Parameters));
                    return;

                case BlockStatement block:
                    VisitBlock(block, scope);
                    return;

                case ForInStatement forIn:
                    Visit(forIn.Collection, scope);
                    Visit(forIn.Body, With(scope, [forIn.VariableName]));
                    return;

                case ForStatement forStatement:
                {
                    var inner = new Scope(scope);
                    if (forStatement.Initializer != null)
                        Hoist([forStatement.Initializer], inner);
                    VisitChildren(forStatement, inner);
                    return;
                }

                case CatchClause catchClause:
                    VisitChildren(catchClause, With(scope, [catchClause.ExceptionVariable]));
                    return;

                case UsingResourceStatement usingResource:
                    Visit(usingResource.Initializer, scope);
                    VisitBlock(usingResource.Body, With(scope, [usingResource.VariableName]));
                    return;

                case ListComprehensionExpression list:
                {
                    Visit(list.Iterable, scope);
                    var inner = With(scope, [list.Variable]);
                    Visit(list.Element, inner);
                    if (list.Filter != null)
                        Visit(list.Filter, inner);
                    return;
                }

                case DictComprehensionExpression dict:
                {
                    Visit(dict.Iterable, scope);
                    var inner = With(scope, [dict.Variable]);
                    Visit(dict.Key, inner);
                    Visit(dict.Value, inner);
                    if (dict.Filter != null)
                        Visit(dict.Filter, inner);
                    return;
                }

                case MatchCase matchCase:
                {
                    var inner = With(scope, PatternNames(matchCase.Pattern));
                    if (matchCase.Guard != null)
                        Visit(matchCase.Guard, inner);
                    Visit(matchCase.Body, inner);
                    return;
                }

                case ClassDeclaration cls:
                {
                    var inner = With(scope, ClassFieldNames(cls));
                    foreach (var member in cls.Members)
                    {
                        if (member.Value is Node value)
                            Visit(value, inner);
                    }
                    return;
                }

                case ActorDeclaration actor:
                {
                    var inner = With(scope, actor.Members.Select(m => m.Name));
                    foreach (var member in actor.Members)
                    {
                        if (member.Value is Node value)
                            Visit(value, inner);
                    }
                    return;
                }

                default:
                    VisitChildren(node, scope);
                    return;
            }
        }

        private void VisitBlock(BlockStatement block, Scope scope)
        {
            var inner = new Scope(scope);
            Hoist(block.Statements, inner);
            foreach (var statement in block.Statements)
                Visit(statement, inner);
        }

        private void VisitChildren(Node node, Scope scope)
        {
            foreach (var child in AstNodeWalker.Descendants(node, static _ => false))
            {
                if (!ReferenceEquals(child, node))
                    Visit(child, scope);
            }
        }

        private Scope With(Scope parent, IEnumerable<string?> names)
        {
            var scope = new Scope(parent);
            foreach (var name in names)
                scope.Add(name, IsBuiltIn);
            return scope;
        }

        /// <summary>Fields of the class and its superclasses: bare names in methods read them.</summary>
        private IEnumerable<string> ClassFieldNames(ClassDeclaration cls)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var current = cls; current != null && seen.Add(current.Name);)
            {
                foreach (var member in current.Members)
                {
                    if (member.Type == MemberType.Field)
                        yield return member.Name;
                }

                current = current.Superclass != null && _classes.TryGetValue(current.Superclass, out var parent)
                    ? parent
                    : null;
            }
        }
    }

    /// <summary>Names a pattern binds (identifiers, rest names, object shorthand keys).</summary>
    public static IEnumerable<string> PatternNames(Pattern? pattern)
    {
        switch (pattern)
        {
            case null:
                yield break;
            case IdentifierPattern id:
                yield return id.Name;
                break;
            case RestPattern rest when !string.IsNullOrEmpty(rest.Name):
                yield return rest.Name!;
                break;
            case ArrayPattern array:
                foreach (var element in array.Elements)
                    foreach (var name in PatternNames(element)) yield return name;
                foreach (var name in PatternNames(array.Rest)) yield return name;
                break;
            case ArrayDestructuringPattern array:
                foreach (var element in array.Elements)
                    foreach (var name in PatternNames(element)) yield return name;
                foreach (var name in PatternNames(array.Rest)) yield return name;
                break;
            case ObjectPattern obj:
                foreach (var property in obj.Properties)
                    foreach (var name in PropertyNames(property)) yield return name;
                break;
            case ObjectDestructuringPattern obj:
                foreach (var property in obj.Properties)
                    foreach (var name in PropertyNames(property)) yield return name;
                break;
            case VariantPattern variant:
                foreach (var payload in variant.PayloadPatterns)
                    foreach (var name in PatternNames(payload)) yield return name;
                break;
        }
    }

    private static IEnumerable<string> PropertyNames(ObjectPatternProperty property)
    {
        if (property.Pattern != null)
            return PatternNames(property.Pattern);
        return [property.BindingName ?? property.Key];
    }
}
