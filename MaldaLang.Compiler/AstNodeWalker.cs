// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Compiler;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MaldaLang.Parser.AST;

/// <summary>
/// Enumerates every <see cref="Node"/> reachable from a root by reflecting over node-typed
/// properties, collections and the non-node holders the parser uses (class members, match
/// cases, tuples). Used by transpiler analyses that must not miss a construct when the AST grows.
/// </summary>
internal static class AstNodeWalker
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    public static IEnumerable<Node> Descendants(object root)
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
            }

            foreach (var property in GetProperties(current.GetType()))
            {
                var value = property.GetValue(current);
                switch (value)
                {
                    case null:
                    case string:
                        break;
                    case Node child:
                        stack.Push(child);
                        break;
                    case IEnumerable sequence:
                        foreach (var item in sequence)
                        {
                            if (item != null && IsStructural(item.GetType()))
                            {
                                stack.Push(item);
                            }
                        }
                        break;
                    default:
                        if (IsStructural(value.GetType()))
                        {
                            stack.Push(value);
                        }
                        break;
                }
            }
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
               type.Namespace?.StartsWith("MaldaLang", StringComparison.Ordinal) == true ||
               (type.IsGenericType && type.Namespace == "System" &&
                (type.Name.StartsWith("ValueTuple`", StringComparison.Ordinal) || type.Name.StartsWith("Tuple`", StringComparison.Ordinal))) ||
               (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>));
    }

    private static PropertyInfo[] GetProperties(Type type)
    {
        return Properties.GetOrAdd(type, static t => t
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
            .ToArray());
    }
}
