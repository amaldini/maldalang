// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

/// <summary>
/// Enumerates every <see cref="Node"/> reachable from a root by reflecting over node-typed
/// properties, collections and the non-node holders the parser uses: class members, match
/// cases, interpolation segments, <c>KeyValuePair</c>s and tuples such as the
/// <c>(Key, Value)</c> entries of object and dictionary literals (tuple items are fields, so
/// they are read through <see cref="ITuple"/>). Used by transpiler analyses and
/// <c>malda lint</c> rules that must not miss a construct when the AST grows.
/// </summary>
public static class AstNodeWalker
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    /// <param name="root">Where to start; it is yielded too when it is a node.</param>
    /// <param name="descendInto">
    /// Optional filter: a node it rejects is still yielded but its children are not visited
    /// (e.g. stop at nested lambdas when collecting a function's own returns).
    /// </param>
    public static IEnumerable<Node> Descendants(object root, Func<Node, bool>? descendInto = null)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<object>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (current is Node node)
            {
                yield return node;
                if (descendInto != null && !ReferenceEquals(node, root) && !descendInto(node))
                {
                    continue;
                }
            }

            PushChildren(current, stack);
        }
    }

    private static void PushChildren(object current, Stack<object> stack)
    {
        switch (current)
        {
            case Node:
                break;
            case ITuple tuple:
                for (var i = 0; i < tuple.Length; i++)
                {
                    PushIfStructural(tuple[i], stack);
                }
                return;
            case IEnumerable sequence:
                foreach (var item in sequence)
                {
                    PushIfStructural(item, stack);
                }
                return;
        }

        foreach (var property in GetProperties(current.GetType()))
        {
            object? value;
            try
            {
                value = property.GetValue(current);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            PushIfStructural(value, stack);
        }
    }

    private static void PushIfStructural(object? value, Stack<object> stack)
    {
        if (value != null && IsStructural(value.GetType()))
        {
            stack.Push(value);
        }
    }

    private static bool IsStructural(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
        {
            return false;
        }

        return typeof(Node).IsAssignableFrom(type) ||
               typeof(IEnumerable).IsAssignableFrom(type) ||
               typeof(ITuple).IsAssignableFrom(type) ||
               type.Namespace?.StartsWith("MaldaLang", StringComparison.Ordinal) == true ||
               (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>));
    }

    private static PropertyInfo[] GetProperties(Type type)
    {
        return Properties.GetOrAdd(type, static t => t
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead &&
                        !p.PropertyType.IsPrimitive && !p.PropertyType.IsEnum &&
                        p.PropertyType != typeof(string) && p.PropertyType != typeof(decimal))
            .ToArray());
    }
}
