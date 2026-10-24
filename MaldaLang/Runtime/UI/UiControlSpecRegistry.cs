// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.UI;

using MaldaLang.Interpreter;

public sealed class UiControlSpec
{
    public string Type { get; }
    public HashSet<string> AllowedEvents { get; }
    public HashSet<string> RequiredProps { get; }
    public HashSet<string> AllowedProps { get; }

    public UiControlSpec(
        string type,
        IEnumerable<string>? allowedEvents = null,
        IEnumerable<string>? requiredProps = null,
        IEnumerable<string>? allowedProps = null)
    {
        Type = type;
        AllowedEvents = new HashSet<string>(allowedEvents ?? Array.Empty<string>(), StringComparer.Ordinal);
        RequiredProps = new HashSet<string>(requiredProps ?? Array.Empty<string>(), StringComparer.Ordinal);
        AllowedProps = new HashSet<string>(allowedProps ?? Array.Empty<string>(), StringComparer.Ordinal);
    }
}

public static class UiControlSpecRegistry
{
    private static readonly Dictionary<string, UiControlSpec> Specs = new(StringComparer.Ordinal)
    {
        ["row"] = Layout("row"),
        ["column"] = Layout("column"),
        ["stack"] = Layout("stack"),
        ["spacer"] = Layout("spacer"),
        ["panel"] = Layout("panel"),
        ["drawer"] = Layout("drawer"),
        ["tabs"] = Layout("tabs"),
        ["accordion"] = Layout("accordion"),
        ["breadcrumbs"] = Layout("breadcrumbs"),
        ["text"] = Textual("text"),
        ["heading"] = Textual("heading"),
        ["image"] = new UiControlSpec("image", allowedProps: BaseProps("src", "alt", "width", "height")),
        ["icon"] = new UiControlSpec("icon", allowedProps: BaseProps("name", "size")),
        ["button"] = Interactive("button", new[] { "onClick" }, BaseProps("label", "disabled", "variant")),
        ["textField"] = Interactive("textField", new[] { "onChange", "onInput" }, BaseProps("name", "value", "defaultValue", "placeholder", "disabled", "inputDebounceMs")),
        ["textArea"] = Interactive("textArea", new[] { "onChange", "onInput" }, BaseProps("name", "value", "defaultValue", "placeholder", "rows", "disabled", "inputDebounceMs")),
        ["checkbox"] = Interactive("checkbox", new[] { "onChange" }, BaseProps("name", "checked", "defaultChecked", "disabled", "label")),
        ["switch"] = Interactive("switch", new[] { "onChange" }, BaseProps("name", "checked", "defaultChecked", "disabled", "label")),
        ["select"] = Interactive("select", new[] { "onChange" }, BaseProps("name", "value", "defaultValue", "options", "disabled")),
        ["radioGroup"] = Interactive("radioGroup", new[] { "onChange" }, BaseProps("name", "value", "defaultValue", "options", "disabled")),
        ["slider"] = Interactive("slider", new[] { "onChange", "onInput" }, BaseProps("name", "value", "min", "max", "step", "disabled", "inputDebounceMs")),
        ["datePicker"] = Interactive("datePicker", new[] { "onChange", "onInput" }, BaseProps("name", "value", "defaultValue", "placeholder", "disabled", "includeTime", "inputDebounceMs")),
        ["form"] = Interactive("form", new[] { "onSubmit" }, BaseProps("method", "action")),
        ["field"] = Layout("field"),
        ["list"] = Layout("list"),
        ["table"] = Layout("table"),
        ["dataGrid"] = Interactive(
            "dataGrid",
            new[] { "onRowClick", "onSelectionChange", "onSort", "onFilter", "onPageChange", "onViewportChange", "onDragStart", "onDragOver", "onDrop", "onDragEnd" },
            BaseProps(
                "columns", "rows", "rowKey",
                "selectionMode", "selectedKeys",
                "sortable", "sort", "filter",
                "page", "pageSize", "totalItems",
                "virtualize", "rowHeight", "overscan", "height")),
        ["treeView"] = Interactive(
            "treeView",
            new[] { "onNodeSelect", "onNodeToggle", "onNodeExpand", "onNodeCollapse", "onNodeActivate", "onLoadChildren", "onDragStart", "onDragOver", "onDrop", "onDragEnd" },
            BaseProps(
                "nodes", "nodeKey",
                "expandedKeys", "selectedKeys",
                "selectionMode", "showLines", "lazy")),
        ["paginator"] = Interactive("paginator", new[] { "onChange" }, BaseProps("page", "pageSize", "totalItems")),
        ["emptyState"] = Layout("emptyState"),
        ["badge"] = Layout("badge"),
        ["alert"] = Layout("alert"),
        ["progress"] = Layout("progress"),
        ["modal"] = Interactive("modal", new[] { "onClose" }, BaseProps("open", "title")),
        ["toast"] = Interactive("toast", new[] { "onClose" }, BaseProps("open", "message", "variant")),
        ["skeleton"] = Layout("skeleton"),
        ["spinner"] = Layout("spinner"),
        ["errorBoundary"] = Layout("errorBoundary"),
        ["slot"] = Layout("slot"),
        ["splitPane"] = Interactive("splitPane", new[] { "onResize" }, BaseProps("direction", "sizes", "minSize")),
        ["menuBar"] = Interactive("menuBar", new[] { "onSelect" }, BaseProps("menus")),
        ["contextMenu"] = Interactive("contextMenu", new[] { "onSelect" }, BaseProps("items")),
        ["commandPalette"] = Interactive("commandPalette", new[] { "onSelect", "onClose" }, BaseProps("open", "commands", "placeholder")),
        ["shortcuts"] = Interactive("shortcuts", Array.Empty<string>(), BaseProps("bindings")),
        ["propertyGrid"] = Interactive("propertyGrid", new[] { "onChange" }, BaseProps("properties")),
        ["canvas"] = Interactive(
            "canvas",
            new[] { "onClick", "onPointerDown", "onPointerMove", "onPointerUp", "onWheel" },
            BaseProps("width", "height", "background", "shapes")),
        ["hostSlot"] = Layout("hostSlot"),
        ["when"] = Layout("when"),
        ["choose"] = Layout("choose"),
        ["each"] = Layout("each")
    };

    /// <summary>True when <paramref name="type"/> is a known control whose spec lists <paramref name="prop"/>.</summary>
    public static bool ListsProp(string type, string prop)
        => Specs.TryGetValue(type, out var spec) && spec.AllowedProps.Contains(prop);

    public static void Validate(UiNode node)
    {
        if (!Specs.TryGetValue(node.Type, out var spec))
        {
            return;
        }

        foreach (var required in spec.RequiredProps)
        {
            if (!node.Props.ContainsKey(required))
            {
                throw new Exception($"ui.{node.Type}() missing required prop '{required}'.");
            }
        }

        foreach (var prop in node.Props)
        {
            if (prop.Key.StartsWith("on", StringComparison.Ordinal))
            {
                if (!spec.AllowedEvents.Contains(prop.Key))
                {
                    throw new Exception($"ui.{node.Type}() does not support event '{prop.Key}'.");
                }
                continue;
            }

            if (spec.AllowedProps.Count > 0 && !spec.AllowedProps.Contains(prop.Key))
            {
                // Other attributes stay permitted; URL props must be listed (UiPropSanitizer).
                continue;
            }
        }
    }

    private static UiControlSpec Layout(string type)
        => new(type, allowedProps: BaseProps("className", "id", "role", "ariaLabel", "componentId", "key"));

    private static UiControlSpec Textual(string type)
        => new(type, requiredProps: new[] { "value" }, allowedProps: BaseProps("value"));

    private static UiControlSpec Interactive(string type, IEnumerable<string> events, IEnumerable<string> props)
        => new(type, allowedEvents: events, allowedProps: props);

    private static IEnumerable<string> BaseProps(params string[] extra)
    {
        var baseProps = new List<string> { "className", "id", "role", "ariaLabel", "componentId", "key", "disabled", "style" };
        baseProps.AddRange(extra);
        return baseProps;
    }
}
