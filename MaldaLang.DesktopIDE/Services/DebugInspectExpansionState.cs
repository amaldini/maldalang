// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Remembers which inspect-tree paths the user expanded. Variable handles are
/// reset on every pause, so expansion is keyed by stable name paths
/// (for example <c>var/Locals/obj</c>), not DAP references.
/// </summary>
public sealed class DebugInspectExpansionState
{
    private readonly HashSet<string> _expandedPaths = new(StringComparer.Ordinal);

    public static string Join(string parentPath, string name)
    {
        if (string.IsNullOrEmpty(parentPath))
        {
            return name;
        }

        if (string.IsNullOrEmpty(name))
        {
            return parentPath;
        }

        return parentPath + "/" + name;
    }

    public void SetExpanded(string path, bool expanded)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (expanded)
        {
            _expandedPaths.Add(path);
        }
        else
        {
            _expandedPaths.Remove(path);
        }
    }

    public bool IsExpanded(string path) =>
        !string.IsNullOrEmpty(path) && _expandedPaths.Contains(path);

    /// <summary>
    /// Expands items whose path is remembered, loading children as a side effect
    /// of <paramref name="expandAndLoadChildren"/> before restoring nested paths.
    /// Collapsing a parent does not forget child paths.
    /// </summary>
    public void RestoreExpanded<T>(
        IEnumerable<T> items,
        string parentPath,
        Func<T, string> getName,
        Func<T, bool> canExpand,
        Action<T> expandAndLoadChildren,
        Func<T, IEnumerable<T>> getChildren)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(getName);
        ArgumentNullException.ThrowIfNull(canExpand);
        ArgumentNullException.ThrowIfNull(expandAndLoadChildren);
        ArgumentNullException.ThrowIfNull(getChildren);

        foreach (var item in items.ToList())
        {
            var path = Join(parentPath, getName(item));
            if (!IsExpanded(path) || !canExpand(item))
            {
                continue;
            }

            expandAndLoadChildren(item);
            RestoreExpanded(getChildren(item), path, getName, canExpand, expandAndLoadChildren, getChildren);
        }
    }
}
