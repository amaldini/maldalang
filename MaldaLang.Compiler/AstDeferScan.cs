// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Compiler;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Answers "can a <c>defer</c> statement run anywhere inside this subtree?" so the C#
/// transpiler can skip the defer-frame push/try/finally on blocks that never register one.
/// The walk is reflective over every <see cref="Node"/>-typed property, so new AST shapes
/// (match arms, lambdas, workflow bodies) are covered without listing them; it errs on the
/// side of "yes" because a missing frame is a runtime error while an extra one only costs time.
/// </summary>
internal sealed class AstDeferScan
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ChildProperties = new();
    private readonly Dictionary<Node, bool> _cache = new(ReferenceEqualityComparer.Instance);

    public bool ContainsDefer(Node? node)
    {
        if (node == null)
        {
            return false;
        }

        if (node is DeferStatement)
        {
            return true;
        }

        if (_cache.TryGetValue(node, out var cached))
        {
            return cached;
        }

        // Guard against cycles (parent links) while the node is being visited.
        _cache[node] = false;
        var result = false;
        foreach (var property in GetChildProperties(node.GetType()))
        {
            var value = property.GetValue(node);
            if (value is Node child)
            {
                result = ContainsDefer(child);
            }
            else if (value is IEnumerable sequence and not string)
            {
                foreach (var item in sequence)
                {
                    if (ContainsDeferInValue(item))
                    {
                        result = true;
                        break;
                    }
                }
            }

            if (result)
            {
                break;
            }
        }

        _cache[node] = result;
        return result;
    }

    public bool ContainsDefer(IEnumerable<Statement> statements) => statements.Any(s => ContainsDefer(s));

    private bool ContainsDeferInValue(object? value)
    {
        return value switch
        {
            Node node => ContainsDefer(node),
            // Dictionary entries and tuples (e.g. object literal properties) can hold nodes.
            DictionaryEntry entry => ContainsDeferInValue(entry.Key) || ContainsDeferInValue(entry.Value),
            IEnumerable sequence and not string => sequence.Cast<object?>().Any(ContainsDeferInValue),
            // Tuple items are fields, not properties, so read them through ITuple.
            ITuple tuple => Enumerable.Range(0, tuple.Length).Any(i => ContainsDeferInValue(tuple[i])),
            _ when value != null && IsKeyValuePairOrTuple(value.GetType()) =>
                GetChildProperties(value.GetType()).Any(p => ContainsDeferInValue(p.GetValue(value))),
            _ => false
        };
    }

    private static bool IsKeyValuePairOrTuple(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(KeyValuePair<,>) ||
               (definition.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) ?? false) ||
               (definition.FullName?.StartsWith("System.Tuple`", StringComparison.Ordinal) ?? false);
    }

    private static PropertyInfo[] GetChildProperties(Type type)
    {
        return ChildProperties.GetOrAdd(type, static t => t
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && MayHoldNodes(p.PropertyType))
            .ToArray());
    }

    private static bool MayHoldNodes(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
        {
            return false;
        }

        if (typeof(Node).IsAssignableFrom(type) || type == typeof(object))
        {
            return true;
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            return true;
        }

        return IsKeyValuePairOrTuple(type);
    }
}
