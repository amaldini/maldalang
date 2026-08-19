// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class DebugInspectExpansionStateTests
{
    [Fact]
    public void Join_BuildsStableNamePaths()
    {
        Assert.Equal("var", DebugInspectExpansionState.Join("", "var"));
        Assert.Equal("var/Locals", DebugInspectExpansionState.Join("var", "Locals"));
        Assert.Equal("var/Locals/obj", DebugInspectExpansionState.Join("var/Locals", "obj"));
        Assert.Equal("watch/user.profile", DebugInspectExpansionState.Join("watch", "user.profile"));
    }

    [Fact]
    public void Collapse_DoesNotForgetNestedPaths()
    {
        var state = new DebugInspectExpansionState();
        state.SetExpanded("var/Locals", true);
        state.SetExpanded("var/Locals/obj", true);

        state.SetExpanded("var/Locals", false);

        Assert.False(state.IsExpanded("var/Locals"));
        Assert.True(state.IsExpanded("var/Locals/obj"));
    }

    [Fact]
    public void RestoreExpanded_ReopensLazyChildrenAfterRebuild()
    {
        var state = new DebugInspectExpansionState();
        state.SetExpanded("var/Locals", true);
        state.SetExpanded("var/Locals/obj", true);

        var locals = LazyScope("Locals", () =>
        {
            return new List<FakeInspectItem>
            {
                Leaf("x"),
                Lazy("obj", () => new List<FakeInspectItem> { Leaf("name") })
            };
        });
        var roots = new List<FakeInspectItem> { locals, LazyScope("Globals", () => new List<FakeInspectItem> { Leaf("print") }) };

        Restore(state, roots, "var");

        Assert.True(locals.IsExpanded);
        Assert.Equal(2, locals.Children.Count);
        var obj = locals.Children.Single(child => child.Name == "obj");
        Assert.True(obj.IsExpanded);
        Assert.Contains(obj.Children, child => child.Name == "name");
        Assert.False(roots.Single(child => child.Name == "Globals").IsExpanded);
    }

    [Fact]
    public void RestoreExpanded_SkipsMissingNodesAfterRebuild()
    {
        var state = new DebugInspectExpansionState();
        state.SetExpanded("var/Locals", true);
        state.SetExpanded("var/Locals/gone", true);

        var locals = LazyScope("Locals", () => new List<FakeInspectItem> { Leaf("x") });
        Restore(state, new List<FakeInspectItem> { locals }, "var");

        Assert.True(locals.IsExpanded);
        Assert.DoesNotContain(locals.Children, child => child.Name == "gone");
        Assert.True(state.IsExpanded("var/Locals/gone"));
    }

    private static void Restore(DebugInspectExpansionState state, IEnumerable<FakeInspectItem> items, string parentPath)
    {
        state.RestoreExpanded(
            items,
            parentPath,
            item => item.Name,
            item => item.CanExpand,
            item =>
            {
                item.IsExpanded = true;
                if (item.Children.Count == 0 && item.Load != null)
                {
                    item.Children = item.Load();
                }
            },
            item => item.Children);
    }

    private static FakeInspectItem LazyScope(string name, Func<List<FakeInspectItem>> load) =>
        new() { Name = name, CanExpand = true, Load = load };

    private static FakeInspectItem Lazy(string name, Func<List<FakeInspectItem>> load) =>
        new() { Name = name, CanExpand = true, Load = load };

    private static FakeInspectItem Leaf(string name) =>
        new() { Name = name, CanExpand = false };

    private sealed class FakeInspectItem
    {
        public required string Name { get; init; }
        public bool CanExpand { get; init; }
        public bool IsExpanded { get; set; }
        public List<FakeInspectItem> Children { get; set; } = new();
        public Func<List<FakeInspectItem>>? Load { get; init; }
    }
}
