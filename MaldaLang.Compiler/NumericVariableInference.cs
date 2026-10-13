// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Compiler;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MaldaLang;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Finds untyped <c>var</c> declarations the C# transpiler can emit as <c>int</c> or
/// <c>double</c> instead of a boxed <c>object</c>, so arithmetic on them compiles to plain C#.
/// <para>
/// A name qualifies only when the whole program gives it one meaning: it is declared by exactly
/// one <c>var</c> with no type hint, never bound any other way (parameter, lambda, for-in,
/// catch, pattern, class member, function name, ...), and its initializer and every assignment
/// are certainly of that type. The rules are syntactic and mirror the interpreter:
/// </para>
/// <list type="bullet">
/// <item>Certainly an int: an int literal, an int name, unary minus or <c>+ - * %</c> over those
/// (int arithmetic stays int, overflow is checked). <c>++</c>/<c>--</c> keep a name an int.</item>
/// <item>Certainly a float: a float literal, a float name, unary minus, <c>+ - * %</c> with a float
/// on one side and a float or int on the other, or <c>/</c> between any two numbers (MALDA
/// division always yields a float). An int literal alone is not a float: <c>var i = 0</c> is an
/// int. Names touched by <c>++</c>/<c>--</c> are never floats.</item>
/// </list>
/// <para>
/// Every value stored in such a variable has that type in the interpreter too, so the typed
/// emission prints, compares and coerces exactly as the boxed one did.
/// </para>
/// </summary>
internal static class NumericVariableInference
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> NodeProperties = new();

    // String-valued AST properties that are not variable bindings.
    private static readonly HashSet<string> NonBindingStringProperties = new(StringComparer.Ordinal)
    {
        nameof(Node.SourceFile),
        "TypeHint",
        "ReturnType",
        "ParameterTypeHints",
        "Documentation",
        "DocComment",
    };

    internal sealed record Result(HashSet<string> Doubles, HashSet<string> Ints)
    {
        public static Result Empty() =>
            new(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
    }

    public static Result Infer(IEnumerable<Statement> program)
    {
        var globals = CollectGlobals(program);

        // Ints first: whether something is an int never depends on the floats.
        var ints = new HashSet<string>(globals.Names, StringComparer.Ordinal);
        var noDoubles = new HashSet<string>(StringComparer.Ordinal);
        Shrink(ints, name => GlobalQualifies(globals, name, Kind.Int, ints, noDoubles, null));

        var doubles = new HashSet<string>(globals.DoubleCandidates(ints), StringComparer.Ordinal);
        Shrink(doubles, name => GlobalQualifies(globals, name, Kind.Double, ints, doubles, null));

        return new Result(doubles, ints);
    }

    /// <summary>
    /// The top-level names <see cref="Infer"/> starts from (declared once by an untyped <c>var</c>,
    /// bound no other way), with what is stored in them, so a caller can shrink them together with
    /// its own assumptions (the C# transpiler does, with calls to typed function variants).
    /// </summary>
    internal sealed class GlobalCandidates
    {
        private readonly Facts _facts;

        internal GlobalCandidates(Facts facts, HashSet<string> names)
        {
            _facts = facts;
            Names = names;
        }

        public HashSet<string> Names { get; }

        internal Facts Facts => _facts;

        /// <summary>The names that may still be floats once <paramref name="ints"/> are fixed.</summary>
        public IEnumerable<string> DoubleCandidates(ISet<string> ints) =>
            Names.Where(n => !ints.Contains(n) && !_facts.Incremented.Contains(n));
    }

    public static GlobalCandidates CollectGlobals(IEnumerable<Statement> program)
    {
        var facts = new Facts();
        foreach (var statement in program)
        {
            facts.Collect(statement);
        }

        var uniquelyDeclared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, decls) in facts.Declarations)
        {
            if (decls.Count == 1 &&
                string.IsNullOrWhiteSpace(decls[0].TypeHint) &&
                !facts.OtherBindings.Contains(name))
            {
                uniquelyDeclared.Add(name);
            }
        }
        return new GlobalCandidates(facts, uniquelyDeclared);
    }

    /// <summary>
    /// True when the initializer and every store of <paramref name="name"/> are certainly of
    /// <paramref name="kind"/>, given the names in <paramref name="ints"/> / <paramref name="doubles"/>
    /// and calls resolved by <paramref name="calls"/>. Stores inside function bodies are checked
    /// with the same names, which is safe: a top-level name is bound nowhere else.
    /// </summary>
    public static bool GlobalQualifies(
        GlobalCandidates globals,
        string name,
        Kind kind,
        ISet<string> ints,
        ISet<string> doubles,
        CallKindResolver? calls)
    {
        var facts = globals.Facts;
        var initializer = facts.Declarations[name][0].Initializer;
        var ok = kind == Kind.Int
            ? IsCertainlyInt(initializer, ints, calls)
            : IsCertainlyDouble(initializer, doubles, ints, calls);
        if (ok && facts.Assignments.TryGetValue(name, out var stores))
        {
            ok = stores.All(store => kind == Kind.Int
                ? IsIntStore(store, ints, calls)
                : IsDoubleStore(store, doubles, ints, calls));
        }
        return ok;
    }

    /// <summary>Locals of one function or method body that can be emitted as <c>int</c> / <c>double</c>.</summary>
    internal sealed record LocalResult(
        Dictionary<VarDeclStatement, Kind> Declarations,
        HashSet<string> Ints,
        HashSet<string> Doubles)
    {
        public static LocalResult Empty() => new(
            new Dictionary<VarDeclStatement, Kind>(ReferenceEqualityComparer.Instance),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));
    }

    /// <summary>
    /// The same rules as <see cref="Infer"/>, applied to the <c>var</c>s of one body, so a name
    /// such as <c>i</c> that other functions or top-level code also declare can still be typed
    /// here. A local name qualifies when the body (nested lambdas and functions included) binds
    /// it only with untyped <c>var</c>s, it is not one of <paramref name="parameters"/>, and every
    /// use of the name sits after one of those declarations inside its block (or inside its
    /// <c>for</c>), so no use can mean an outer variable. Several declarations (two loops that
    /// both use <c>i</c>) are typed together: every initializer and store must qualify.
    /// <para>
    /// <paramref name="outerInts"/> / <paramref name="outerDoubles"/> are the names already known
    /// in this body (top-level inferred variables, typed parameters); <paramref name="resolverFor"/>
    /// builds the call resolver for a given set of int / double names.
    /// </para>
    /// </summary>
    public static LocalResult InferLocals(
        BlockStatement body,
        IReadOnlyCollection<string> parameters,
        ISet<string> outerInts,
        ISet<string> outerDoubles,
        Func<ISet<string>, ISet<string>, CallKindResolver?>? resolverFor = null)
    {
        var facts = new Facts();
        facts.Collect(body);

        var nodes = AstNodeWalker.Descendants(body).ToList();
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, decls) in facts.Declarations)
        {
            if (decls.All(d => string.IsNullOrWhiteSpace(d.TypeHint)) &&
                !facts.OtherBindings.Contains(name) &&
                !parameters.Contains(name) &&
                !outerInts.Contains(name) &&
                !outerDoubles.Contains(name) &&
                AllUsesInScope(nodes, decls, name))
            {
                candidates.Add(name);
            }
        }

        var result = LocalResult.Empty();
        if (candidates.Count == 0)
        {
            return result;
        }

        var ints = new HashSet<string>(candidates, StringComparer.Ordinal);
        ShrinkLocals(ints, facts, shrinkingInts: true, outerInts, outerDoubles, resolverFor);

        var allInts = Union(outerInts, ints);
        var doubles = new HashSet<string>(
            candidates.Where(n => !ints.Contains(n) && !facts.Incremented.Contains(n)),
            StringComparer.Ordinal);
        ShrinkLocals(doubles, facts, shrinkingInts: false, allInts, outerDoubles, resolverFor);

        foreach (var name in ints)
        {
            foreach (var declaration in facts.Declarations[name])
                result.Declarations[declaration] = Kind.Int;
            result.Ints.Add(name);
        }
        foreach (var name in doubles)
        {
            foreach (var declaration in facts.Declarations[name])
                result.Declarations[declaration] = Kind.Double;
            result.Doubles.Add(name);
        }
        return result;
    }

    // Shrink like Infer, over the outer names plus the locals still standing, with calls
    // resolved in that scope.
    private static void ShrinkLocals(
        HashSet<string> candidates,
        Facts facts,
        bool shrinkingInts,
        ISet<string> outerInts,
        ISet<string> outerDoubles,
        Func<ISet<string>, ISet<string>, CallKindResolver?>? resolverFor)
    {
        bool changed;
        do
        {
            changed = false;
            var ints = shrinkingInts ? Union(outerInts, candidates) : Union(outerInts, Array.Empty<string>());
            var doubles = shrinkingInts ? Union(outerDoubles, Array.Empty<string>()) : Union(outerDoubles, candidates);
            var calls = resolverFor?.Invoke(ints, doubles);
            foreach (var name in candidates.ToList())
            {
                var ok = facts.Declarations[name].All(d => shrinkingInts
                    ? IsCertainlyInt(d.Initializer, ints, calls)
                    : IsCertainlyDouble(d.Initializer, doubles, ints, calls));
                if (ok && facts.Assignments.TryGetValue(name, out var stores))
                {
                    ok = stores.All(store => shrinkingInts
                        ? IsIntStore(store, ints, calls)
                        : IsDoubleStore(store, doubles, ints, calls));
                }

                if (!ok)
                {
                    candidates.Remove(name);
                    changed = true;
                }
            }
        }
        while (changed);
    }

    private static HashSet<string> Union(IEnumerable<string> first, IEnumerable<string> second)
    {
        var set = new HashSet<string>(first, StringComparer.Ordinal);
        set.UnionWith(second);
        return set;
    }

    /// <summary>
    /// True when every <c>name</c> in the body refers to one of <paramref name="declarations"/>:
    /// each is a statement of a block (or the initializer of a <c>for</c>), and each use sits in
    /// such a block after its declaration (or in that <c>for</c>), outside that declaration's own
    /// initializer.
    /// </summary>
    private static bool AllUsesInScope(List<Node> bodyNodes, List<VarDeclStatement> declarations, string name)
    {
        var allowed = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        foreach (var declaration in declarations)
        {
            IEnumerable<Node>? region = null;
            foreach (var node in bodyNodes)
            {
                if (node is BlockStatement block)
                {
                    var index = block.Statements.FindIndex(s => ReferenceEquals(s, declaration));
                    if (index >= 0)
                    {
                        region = block.Statements.Skip(index).SelectMany(s => AstNodeWalker.Descendants(s));
                        break;
                    }
                }
                else if (node is ForStatement loop && ReferenceEquals(loop.Initializer, declaration))
                {
                    region = AstNodeWalker.Descendants(loop);
                    break;
                }
            }

            if (region == null)
            {
                return false;
            }

            var ownInitializer = declaration.Initializer == null
                ? new HashSet<Node>(ReferenceEqualityComparer.Instance)
                : new HashSet<Node>(AstNodeWalker.Descendants(declaration.Initializer), ReferenceEqualityComparer.Instance);
            allowed.UnionWith(region.Where(node => !ownInitializer.Contains(node)));
        }

        return bodyNodes.All(node => node is not IdentifierExpression id || id.Name != name || allowed.Contains(id));
    }

    // Shrink to a fixpoint: a name stays only while everything stored in it qualifies given the
    // names still standing.
    private static void Shrink(HashSet<string> candidates, Func<string, bool> qualifies)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var name in candidates.ToList())
            {
                if (!qualifies(name))
                {
                    candidates.Remove(name);
                    changed = true;
                }
            }
        }
        while (changed);
    }

    internal enum Kind
    {
        None,
        Int,
        Double
    }

    /// <summary>
    /// Resolves the numeric kind a user function call certainly returns (see the C# transpiler's
    /// typed function twins), or <see cref="Kind.None"/>. Null when calls are never numeric.
    /// </summary>
    internal delegate Kind CallKindResolver(FunctionCallExpression call);

    internal static bool IsIntStore(AssignmentStatement assignment, ISet<string> ints, CallKindResolver? calls = null)
    {
        return assignment.Operator switch
        {
            TokenType.Assign or TokenType.PlusAssign or TokenType.MinusAssign or TokenType.MultiplyAssign =>
                IsCertainlyInt(assignment.Value, ints, calls),
            // x /= v makes x a float.
            _ => false
        };
    }

    internal static bool IsDoubleStore(AssignmentStatement assignment, ISet<string> doubles, ISet<string> ints, CallKindResolver? calls = null)
    {
        return assignment.Operator switch
        {
            TokenType.Assign => IsCertainlyDouble(assignment.Value, doubles, ints, calls),
            // x op= v with x a float: float op (float or int) is a float.
            TokenType.PlusAssign or TokenType.MinusAssign or TokenType.MultiplyAssign or TokenType.DivideAssign =>
                IsCertainlyNumber(assignment.Value, doubles, ints, calls),
            _ => false
        };
    }

    public static bool IsCertainlyInt(Expression? expression, ISet<string> ints, CallKindResolver? calls = null)
    {
        switch (expression)
        {
            case LiteralExpression { Value: int }:
                return true;
            case IdentifierExpression id:
                return ints.Contains(id.Name);
            case UnaryExpression { Operator: TokenType.Minus } unary:
                return IsCertainlyInt(unary.Right, ints, calls);
            case BinaryExpression binary when binary.Operator is
                TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Modulo:
                return IsCertainlyInt(binary.Left, ints, calls) && IsCertainlyInt(binary.Right, ints, calls);
            case FunctionCallExpression call when calls != null:
                return calls(call) == Kind.Int;
            default:
                return false;
        }
    }

    public static bool IsCertainlyDouble(Expression? expression, ISet<string> doubles, ISet<string> ints, CallKindResolver? calls = null)
    {
        switch (expression)
        {
            case LiteralExpression { Value: double }:
                return true;
            case IdentifierExpression id:
                return doubles.Contains(id.Name);
            case UnaryExpression { Operator: TokenType.Minus } unary:
                return IsCertainlyDouble(unary.Right, doubles, ints, calls);
            case BinaryExpression { Operator: TokenType.Divide } division:
                return IsCertainlyNumber(division.Left, doubles, ints, calls) &&
                       IsCertainlyNumber(division.Right, doubles, ints, calls);
            case BinaryExpression binary when binary.Operator is
                TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Modulo:
                var left = IsCertainlyDouble(binary.Left, doubles, ints, calls);
                var right = IsCertainlyDouble(binary.Right, doubles, ints, calls);
                return (left && (right || IsCertainlyInt(binary.Right, ints, calls))) ||
                       (right && IsCertainlyInt(binary.Left, ints, calls));
            case FunctionCallExpression call when calls != null:
                return calls(call) == Kind.Double;
            default:
                return false;
        }
    }

    private static bool IsCertainlyNumber(Expression? expression, ISet<string> doubles, ISet<string> ints, CallKindResolver? calls) =>
        IsCertainlyInt(expression, ints, calls) || IsCertainlyDouble(expression, doubles, ints, calls);

    /// <summary>
    /// Every name bound inside <paramref name="root"/> other than by a plain use: var
    /// declarations, parameters of nested functions and lambdas, for-in / catch variables,
    /// patterns, and so on.
    /// With <paramref name="ignoreTypeReferences"/>, names that only refer to a type
    /// (<c>new T()</c>, <c>spawn T()</c>, a superclass, a type name) are not counted.
    /// </summary>
    public static HashSet<string> BindingsWithin(Node root, bool ignoreTypeReferences = false)
    {
        var facts = new Facts { IgnoreTypeReferences = ignoreTypeReferences };
        facts.Collect(root);
        var names = new HashSet<string>(facts.OtherBindings, StringComparer.Ordinal);
        names.UnionWith(facts.Declarations.Keys);
        return names;
    }

    /// <summary>Declarations, assignments and every other way a name is bound or bumped.</summary>
    internal sealed class Facts
    {
        public Dictionary<string, List<VarDeclStatement>> Declarations { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<AssignmentStatement>> Assignments { get; } = new(StringComparer.Ordinal);
        public HashSet<string> OtherBindings { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Incremented { get; } = new(StringComparer.Ordinal);
        public bool IgnoreTypeReferences { get; init; }
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

        public void Collect(object node)
        {
            if (!_visited.Add(node))
            {
                return;
            }

            switch (node)
            {
                case VarDeclStatement varDecl:
                    Add(Declarations, varDecl.Name, varDecl);
                    break;
                case AssignmentStatement { Target: IdentifierExpression target } assignment:
                    Add(Assignments, target.Name, assignment);
                    break;
                case PostfixExpression { Left: IdentifierExpression postfixTarget }:
                    Incremented.Add(postfixTarget.Name);
                    break;
                case UnaryExpression { Operator: TokenType.Increment or TokenType.Decrement, Right: IdentifierExpression prefixTarget }:
                    Incremented.Add(prefixTarget.Name);
                    break;
            }

            foreach (var property in GetProperties(node.GetType()))
            {
                var value = property.GetValue(node);
                switch (value)
                {
                    case null:
                        break;
                    case string text:
                        if (IsBindingProperty(node, property) && !IsIgnoredTypeReference(property))
                        {
                            OtherBindings.Add(text);
                        }
                        break;
                    case Node child:
                        Collect(child);
                        break;
                    case IEnumerable sequence:
                        foreach (var item in sequence)
                        {
                            CollectValue(item, node, property);
                        }
                        break;
                    default:
                        if (IsCompositeValue(value.GetType()))
                        {
                            CollectValue(value, node, property);
                        }
                        break;
                }
            }
        }

        private void CollectValue(object? item, object owner, PropertyInfo ownerProperty)
        {
            switch (item)
            {
                case null:
                    return;
                case string text:
                    // e.g. FunctionDeclaration.Parameters, LambdaExpression.Parameters.
                    if (IsBindingProperty(owner, ownerProperty) && !IsIgnoredTypeReference(ownerProperty))
                    {
                        OtherBindings.Add(text);
                    }
                    return;
                case Node node:
                    Collect(node);
                    return;
                case IEnumerable nested:
                    foreach (var inner in nested)
                    {
                        CollectValue(inner, owner, ownerProperty);
                    }
                    return;
            }

            if (!IsCompositeValue(item.GetType()) || !_visited.Add(item))
            {
                return;
            }

            // Non-node holders (ClassMember, MatchCase-like records, tuples, key/value pairs):
            // walk their properties the same way, treating their strings as bindings.
            foreach (var property in GetProperties(item.GetType()))
            {
                CollectValue(property.GetValue(item), item, property);
            }
        }

        private bool IsIgnoredTypeReference(PropertyInfo property) =>
            IgnoreTypeReferences && property.Name is "ClassName" or "TypeName" or "ActorName" or "Superclass";

        private static void Add<T>(Dictionary<string, List<T>> map, string name, T value)
        {
            if (!map.TryGetValue(name, out var list))
            {
                map[name] = list = new List<T>();
            }
            list.Add(value);
        }
    }

    private static bool IsBindingProperty(object owner, PropertyInfo property)
    {
        if (NonBindingStringProperties.Contains(property.Name) ||
            property.PropertyType == typeof(object) ||
            owner is LiteralExpression or InterpolatedStringExpression or InterpolatedStringSegment)
        {
            return false;
        }

        // Uses and member names are not bindings; the declaration itself is counted separately.
        return owner switch
        {
            VarDeclStatement when property.Name == nameof(VarDeclStatement.Name) => false,
            IdentifierExpression when property.Name == nameof(IdentifierExpression.Name) => false,
            MemberAccessExpression when property.Name == nameof(MemberAccessExpression.Member) => false,
            _ => true
        };
    }

    private static bool IsCompositeValue(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
        {
            return false;
        }

        // Literal payloads (boxed numbers, dates) are not AST structure.
        return type.Namespace?.StartsWith("MaldaLang", StringComparison.Ordinal) == true ||
               (type.IsGenericType && type.Namespace == "System" &&
                (type.Name.StartsWith("ValueTuple`", StringComparison.Ordinal) || type.Name.StartsWith("Tuple`", StringComparison.Ordinal))) ||
               (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>));
    }

    private static PropertyInfo[] GetProperties(Type type)
    {
        return NodeProperties.GetOrAdd(type, static t => t
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
            .ToArray());
    }
}
