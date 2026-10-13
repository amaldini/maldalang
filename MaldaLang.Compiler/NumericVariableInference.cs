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

        // Ints first: whether something is an int never depends on the floats.
        var ints = new HashSet<string>(uniquelyDeclared, StringComparer.Ordinal);
        Shrink(ints, facts,
            init => IsCertainlyInt(init, ints),
            store => IsIntStore(store, ints));

        var doubles = new HashSet<string>(
            uniquelyDeclared.Where(n => !ints.Contains(n) && !facts.Incremented.Contains(n)),
            StringComparer.Ordinal);
        Shrink(doubles, facts,
            init => IsCertainlyDouble(init, doubles, ints),
            store => IsDoubleStore(store, doubles, ints));

        return new Result(doubles, ints);
    }

    // Shrink to a fixpoint: a name stays only while everything stored in it qualifies given the
    // names still standing.
    private static void Shrink(
        HashSet<string> candidates,
        Facts facts,
        Func<Expression, bool> initializerOk,
        Func<AssignmentStatement, bool> storeOk)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var name in candidates.ToList())
            {
                var ok = initializerOk(facts.Declarations[name][0].Initializer);
                if (ok && facts.Assignments.TryGetValue(name, out var stores))
                {
                    ok = stores.All(storeOk);
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

    private static bool IsIntStore(AssignmentStatement assignment, ISet<string> ints)
    {
        return assignment.Operator switch
        {
            TokenType.Assign or TokenType.PlusAssign or TokenType.MinusAssign or TokenType.MultiplyAssign =>
                IsCertainlyInt(assignment.Value, ints),
            // x /= v makes x a float.
            _ => false
        };
    }

    private static bool IsDoubleStore(AssignmentStatement assignment, ISet<string> doubles, ISet<string> ints)
    {
        return assignment.Operator switch
        {
            TokenType.Assign => IsCertainlyDouble(assignment.Value, doubles, ints),
            // x op= v with x a float: float op (float or int) is a float.
            TokenType.PlusAssign or TokenType.MinusAssign or TokenType.MultiplyAssign or TokenType.DivideAssign =>
                IsCertainlyNumber(assignment.Value, doubles, ints),
            _ => false
        };
    }

    public static bool IsCertainlyInt(Expression? expression, ISet<string> ints)
    {
        switch (expression)
        {
            case LiteralExpression { Value: int }:
                return true;
            case IdentifierExpression id:
                return ints.Contains(id.Name);
            case UnaryExpression { Operator: TokenType.Minus } unary:
                return IsCertainlyInt(unary.Right, ints);
            case BinaryExpression binary when binary.Operator is
                TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Modulo:
                return IsCertainlyInt(binary.Left, ints) && IsCertainlyInt(binary.Right, ints);
            default:
                return false;
        }
    }

    public static bool IsCertainlyDouble(Expression? expression, ISet<string> doubles, ISet<string> ints)
    {
        switch (expression)
        {
            case LiteralExpression { Value: double }:
                return true;
            case IdentifierExpression id:
                return doubles.Contains(id.Name);
            case UnaryExpression { Operator: TokenType.Minus } unary:
                return IsCertainlyDouble(unary.Right, doubles, ints);
            case BinaryExpression { Operator: TokenType.Divide } division:
                return IsCertainlyNumber(division.Left, doubles, ints) &&
                       IsCertainlyNumber(division.Right, doubles, ints);
            case BinaryExpression binary when binary.Operator is
                TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Modulo:
                var left = IsCertainlyDouble(binary.Left, doubles, ints);
                var right = IsCertainlyDouble(binary.Right, doubles, ints);
                return (left && (right || IsCertainlyInt(binary.Right, ints))) ||
                       (right && IsCertainlyInt(binary.Left, ints));
            default:
                return false;
        }
    }

    private static bool IsCertainlyNumber(Expression? expression, ISet<string> doubles, ISet<string> ints) =>
        IsCertainlyInt(expression, ints) || IsCertainlyDouble(expression, doubles, ints);

    /// <summary>Declarations, assignments and every other way a name is bound or bumped.</summary>
    private sealed class Facts
    {
        public Dictionary<string, List<VarDeclStatement>> Declarations { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<AssignmentStatement>> Assignments { get; } = new(StringComparer.Ordinal);
        public HashSet<string> OtherBindings { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Incremented { get; } = new(StringComparer.Ordinal);
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
                        if (IsBindingProperty(node, property))
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
                    if (IsBindingProperty(owner, ownerProperty))
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
