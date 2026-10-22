// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Interpreter;
using MaldaLang.Runtime.UI;
using MaldaLang.Tests.Fuzz;
using Xunit;

namespace MaldaLang.Tests;

public class UiDiffEngineTests
{
    [Fact]
    public void Diff_WhenPropChanges_EmitsSetPropPatch()
    {
        var previous = new UiNode("text", new Dictionary<string, RuntimeValue>
        {
            ["value"] = RuntimeValue.String("a")
        });
        var current = new UiNode("text", new Dictionary<string, RuntimeValue>
        {
            ["value"] = RuntimeValue.String("b")
        });

        var patches = UiDiffEngine.Diff(previous, current);
        Assert.Contains(patches, p => p.Operation == UiPatchOperation.SetProp && p.PropertyName == "value");
    }

    [Fact]
    public void Diff_WhenNodeTypeChanges_EmitsReplaceNodePatch()
    {
        var previous = new UiNode("text");
        var current = new UiNode("button");

        var patches = UiDiffEngine.Diff(previous, current);
        Assert.Contains(patches, p => p.Operation == UiPatchOperation.ReplaceNode);
    }

    [Theory]
    [InlineData("ab", "cb")]
    [InlineData("ab", "ba")]
    [InlineData("abcde", "edcba")]
    [InlineData("abc", "xabcy")]
    [InlineData("abcd", "bd")]
    [InlineData("abcd", "dxbz")]
    [InlineData("abc", "cab")]
    public void Diff_KeyedChildren_PatchesRebuildTheNewOrder(string before, string after)
    {
        var previous = KeyedRow(before);
        var current = KeyedRow(after);

        Assert.Equal(
            UiPatchApplier.Canonical(UiPatchApplier.ToJson(current)),
            UiPatchApplier.DiffAndApply(previous, current));
    }

    [Fact]
    public void Diff_KeyedChildren_MovesAChildInsteadOfReplacingIt()
    {
        var patches = UiDiffEngine.Diff(KeyedRow("ab"), KeyedRow("ba"));

        var move = Assert.Single(patches);
        Assert.Equal(UiPatchOperation.MoveChild, move.Operation);
        Assert.Equal(1, move.FromIndex);
        Assert.Equal(0, move.ChildIndex);
    }

    [Fact]
    public void Diff_DuplicateKeys_FallBackToIndexDiff()
    {
        var previous = KeyedRow("aa");
        var current = KeyedRow("aab");

        Assert.Equal(
            UiPatchApplier.Canonical(UiPatchApplier.ToJson(current)),
            UiPatchApplier.DiffAndApply(previous, current));
        Assert.DoesNotContain(UiDiffEngine.Diff(previous, current), p => p.Operation == UiPatchOperation.MoveChild);
    }

    /// <summary>A row of text children, one per character, keyed and labelled by it.</summary>
    private static UiNode KeyedRow(string keys) =>
        new("row", children: keys
            .Select(k => new UiNode(
                "text",
                new Dictionary<string, RuntimeValue> { ["value"] = RuntimeValue.String(k.ToString().ToUpperInvariant()) },
                key: k.ToString()))
            .ToList());

    [Fact]
    public void Diff_WhenStyleChanges_EmitsSetPropPatch()
    {
        var previous = new UiNode("panel", new Dictionary<string, RuntimeValue>
        {
            ["style"] = RuntimeValue.String("color:red; margin:4px;")
        });
        var current = new UiNode("panel", new Dictionary<string, RuntimeValue>
        {
            ["style"] = RuntimeValue.String("color:blue; margin:4px;")
        });

        var patches = UiDiffEngine.Diff(previous, current);
        Assert.Contains(patches, p => p.Operation == UiPatchOperation.SetProp && p.PropertyName == "style");
    }

    [Fact]
    public void Diff_WhenStyleIsRemoved_EmitsRemovePropPatch()
    {
        var previous = new UiNode("panel", new Dictionary<string, RuntimeValue>
        {
            ["style"] = RuntimeValue.String("display:flex;")
        });
        var current = new UiNode("panel");

        var patches = UiDiffEngine.Diff(previous, current);
        Assert.Contains(patches, p => p.Operation == UiPatchOperation.RemoveProp && p.PropertyName == "style");
    }
}
