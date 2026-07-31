// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.UI;
using Xunit;

namespace MaldaLang.Tests;

public class UiControlSpecRegistryTests
{
    [Fact]
    public void FromRuntimeValue_RejectsUnsupportedEvent()
    {
        var node = new JsonObject();
        node.Set("type", RuntimeValue.String("text"));
        var props = new JsonObject();
        props.Set("value", RuntimeValue.String("hello"));
        props.Set("onSubmit", RuntimeValue.String("handler"));
        node.Set("props", RuntimeValue.Object(props));
        node.Set("children", RuntimeValue.Array(new List<RuntimeValue>()));

        Assert.Throws<Exception>(() => UiNode.FromRuntimeValue(RuntimeValue.Object(node)));
    }

    [Fact]
    public void FromRuntimeValue_AllowsSupportedEvent()
    {
        var node = new JsonObject();
        node.Set("type", RuntimeValue.String("button"));
        var props = new JsonObject();
        props.Set("label", RuntimeValue.String("Save"));
        props.Set("onClick", RuntimeValue.String("save"));
        node.Set("props", RuntimeValue.Object(props));
        node.Set("children", RuntimeValue.Array(new List<RuntimeValue>()));

        var parsed = UiNode.FromRuntimeValue(RuntimeValue.Object(node));
        Assert.Equal("button", parsed.Type);
        Assert.True(parsed.Props.ContainsKey("onClick"));
    }

    [Fact]
    public void FromRuntimeValue_AllowsAdvancedDataGridEvents()
    {
        var node = new JsonObject();
        node.Set("type", RuntimeValue.String("dataGrid"));
        var props = new JsonObject();
        props.Set("columns", RuntimeValue.Array(new List<RuntimeValue>()));
        props.Set("rows", RuntimeValue.Array(new List<RuntimeValue>()));
        props.Set("onSort", RuntimeValue.String("sort"));
        props.Set("onSelectionChange", RuntimeValue.String("sel"));
        props.Set("onViewportChange", RuntimeValue.String("viewport"));
        node.Set("props", RuntimeValue.Object(props));
        node.Set("children", RuntimeValue.Array(new List<RuntimeValue>()));

        var parsed = UiNode.FromRuntimeValue(RuntimeValue.Object(node));
        Assert.Equal("dataGrid", parsed.Type);
        Assert.True(parsed.Props.ContainsKey("onSort"));
        Assert.True(parsed.Props.ContainsKey("onSelectionChange"));
        Assert.True(parsed.Props.ContainsKey("onViewportChange"));
    }

    [Fact]
    public void FromRuntimeValue_AllowsTreeViewEvents()
    {
        var node = new JsonObject();
        node.Set("type", RuntimeValue.String("treeView"));
        var props = new JsonObject();
        props.Set("nodes", RuntimeValue.Array(new List<RuntimeValue>()));
        props.Set("onNodeSelect", RuntimeValue.String("select"));
        props.Set("onNodeToggle", RuntimeValue.String("toggle"));
        node.Set("props", RuntimeValue.Object(props));
        node.Set("children", RuntimeValue.Array(new List<RuntimeValue>()));

        var parsed = UiNode.FromRuntimeValue(RuntimeValue.Object(node));
        Assert.Equal("treeView", parsed.Type);
        Assert.True(parsed.Props.ContainsKey("onNodeSelect"));
        Assert.True(parsed.Props.ContainsKey("onNodeToggle"));
    }

    [Fact]
    public void FromRuntimeValue_AllowsDatePickerPropsAndEvents()
    {
        var node = new JsonObject();
        node.Set("type", RuntimeValue.String("datePicker"));
        var props = new JsonObject();
        props.Set("value", RuntimeValue.String("2026-02-22"));
        props.Set("includeTime", RuntimeValue.Boolean(true));
        props.Set("onChange", RuntimeValue.String("changed"));
        node.Set("props", RuntimeValue.Object(props));
        node.Set("children", RuntimeValue.Array(new List<RuntimeValue>()));

        var parsed = UiNode.FromRuntimeValue(RuntimeValue.Object(node));
        Assert.Equal("datePicker", parsed.Type);
        Assert.True(parsed.Props.ContainsKey("value"));
        Assert.True(parsed.Props.ContainsKey("includeTime"));
        Assert.True(parsed.Props.ContainsKey("onChange"));
    }
}
