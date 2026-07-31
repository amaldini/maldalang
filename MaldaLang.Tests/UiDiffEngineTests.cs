// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Interpreter;
using MaldaLang.Runtime.UI;
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

    [Fact]
    public void Diff_KeyedChildren_PrefersInsertRemoveOverReplace()
    {
        var previous = new UiNode("row", children: new List<UiNode>
        {
            new("text", key: "a"),
            new("text", key: "b")
        });

        var current = new UiNode("row", children: new List<UiNode>
        {
            new("text", key: "b"),
            new("text", key: "c")
        });

        var patches = UiDiffEngine.Diff(previous, current);
        Assert.Contains(patches, p => p.Operation == UiPatchOperation.InsertChild);
        Assert.Contains(patches, p => p.Operation == UiPatchOperation.RemoveChild);
    }

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
