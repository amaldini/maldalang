// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.UI;

using MaldaLang.Interpreter;

public static class UiDiffEngine
{
    public static List<UiPatch> Diff(UiNode? previous, UiNode? current)
    {
        var patches = new List<UiPatch>();
        DiffInternal(previous, current, "/", patches);
        return patches;
    }

    private static void DiffInternal(UiNode? previous, UiNode? current, string path, List<UiPatch> patches)
    {
        if (previous == null && current == null)
        {
            return;
        }

        if (previous == null && current != null)
        {
            patches.Add(new UiPatch(UiPatchOperation.ReplaceNode, path, value: current.ToRuntimeValue()));
            return;
        }

        if (previous != null && current == null)
        {
            patches.Add(new UiPatch(UiPatchOperation.ReplaceNode, path, value: RuntimeValue.Null()));
            return;
        }

        if (previous == null || current == null)
        {
            return;
        }

        if (!string.Equals(previous.Type, current.Type, StringComparison.Ordinal) ||
            !string.Equals(previous.Key, current.Key, StringComparison.Ordinal))
        {
            patches.Add(new UiPatch(UiPatchOperation.ReplaceNode, path, value: current.ToRuntimeValue()));
            return;
        }

        DiffProps(previous, current, path, patches);
        DiffChildren(previous, current, path, patches);
    }

    private static void DiffProps(UiNode previous, UiNode current, string path, List<UiPatch> patches)
    {
        foreach (var previousProp in previous.Props)
        {
            if (!current.Props.ContainsKey(previousProp.Key))
            {
                patches.Add(new UiPatch(UiPatchOperation.RemoveProp, path, previousProp.Key));
            }
        }

        foreach (var currentProp in current.Props)
        {
            if (!previous.Props.TryGetValue(currentProp.Key, out var previousValue) || !RuntimeValueEquals(previousValue, currentProp.Value))
            {
                patches.Add(new UiPatch(UiPatchOperation.SetProp, path, currentProp.Key, currentProp.Value));
            }
        }
    }

    private static void DiffChildren(UiNode previous, UiNode current, string path, List<UiPatch> patches)
    {
        if (CanUseKeyedDiff(previous.Children, current.Children))
        {
            DiffChildrenByKey(previous.Children, current.Children, path, patches);
            return;
        }

        var minCount = Math.Min(previous.Children.Count, current.Children.Count);
        for (var i = 0; i < minCount; i++)
        {
            DiffInternal(previous.Children[i], current.Children[i], path + i + "/", patches);
        }

        if (current.Children.Count > previous.Children.Count)
        {
            for (var i = previous.Children.Count; i < current.Children.Count; i++)
            {
                patches.Add(new UiPatch(
                    UiPatchOperation.InsertChild,
                    path,
                    value: current.Children[i].ToRuntimeValue(),
                    childIndex: i));
            }
        }

        if (previous.Children.Count > current.Children.Count)
        {
            for (var i = previous.Children.Count - 1; i >= current.Children.Count; i--)
            {
                patches.Add(new UiPatch(UiPatchOperation.RemoveChild, path, childIndex: i));
            }
        }
    }

    /// <summary>
    /// Keyed diff needs a non-empty key on every child of both lists and no key twice in a
    /// list; otherwise children are matched by index.
    /// </summary>
    private static bool CanUseKeyedDiff(List<UiNode> previous, List<UiNode> current)
    {
        if (previous.Count == 0 || current.Count == 0)
        {
            return false;
        }

        return HasUniqueKeys(previous) && HasUniqueKeys(current);
    }

    private static bool HasUniqueKeys(List<UiNode> nodes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Key) || !seen.Add(node.Key))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Patches apply in order, so every index is the child's position at that moment:
    /// first remove the children whose key is gone (highest index first), then walk the new
    /// order and, at each position, move the matching child there or insert a new one. A
    /// child is diffed once it sits at its final index, and later moves only touch higher
    /// indices, so the paths in its patches stay valid.
    /// </summary>
    private static void DiffChildrenByKey(List<UiNode> previous, List<UiNode> current, string path, List<UiPatch> patches)
    {
        var currentKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in current)
        {
            currentKeys.Add(node.Key!);
        }

        var working = new List<UiNode>(previous.Count);
        for (var i = previous.Count - 1; i >= 0; i--)
        {
            if (currentKeys.Contains(previous[i].Key!))
            {
                working.Insert(0, previous[i]);
            }
            else
            {
                patches.Add(new UiPatch(UiPatchOperation.RemoveChild, path, childIndex: i));
            }
        }

        for (var i = 0; i < current.Count; i++)
        {
            var key = current[i].Key!;
            var from = working.FindIndex(i, node => string.Equals(node.Key, key, StringComparison.Ordinal));
            if (from < 0)
            {
                patches.Add(new UiPatch(UiPatchOperation.InsertChild, path, value: current[i].ToRuntimeValue(), childIndex: i));
                working.Insert(i, current[i]);
                continue;
            }

            if (from != i)
            {
                patches.Add(new UiPatch(UiPatchOperation.MoveChild, path, childIndex: i, fromIndex: from));
                var moved = working[from];
                working.RemoveAt(from);
                working.Insert(i, moved);
            }

            DiffInternal(working[i], current[i], path + i + "/", patches);
        }
    }

    private static bool RuntimeValueEquals(RuntimeValue left, RuntimeValue right)
    {
        if (left.Type != right.Type)
        {
            return false;
        }

        return left.Type switch
        {
            ValueType.Null => true,
            ValueType.Integer => left.AsInteger() == right.AsInteger(),
            ValueType.Float => Math.Abs(left.AsFloat() - right.AsFloat()) < 1e-9,
            ValueType.String => string.Equals(left.AsString(), right.AsString(), StringComparison.Ordinal),
            ValueType.Boolean => left.AsBoolean() == right.AsBoolean(),
            _ => string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal)
        };
    }
}
