// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.UI;

using System.Text.RegularExpressions;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

/// <summary>
/// Which props a <see cref="UiNode"/> keeps. The browser client turns scalar props into DOM
/// attributes, so a prop survives only when it is safe as one:
/// <list type="bullet">
/// <item>names are plain identifiers; <c>on*</c> only for the known event props; never
/// <c>srcdoc</c> or <c>formaction</c></item>
/// <item>URL props (<c>src</c>, <c>href</c>, <c>action</c>, …) only on a control whose spec in
/// <see cref="UiControlSpecRegistry"/> lists them, and only with an <c>http</c>, <c>https</c>,
/// <c>mailto</c> or <c>tel</c> scheme or a relative URL</item>
/// <item><c>style</c> without <c>url(</c>, <c>expression(</c>, <c>@import</c>, escapes or
/// script schemes</item>
/// </list>
/// <c>malda-ui-client.js</c> applies the same rules (<c>MaldaUiDom.isSafeAttribute</c>) to what
/// any host sends it.
/// </summary>
public static class UiPropSanitizer
{
    private static readonly HashSet<string> EventProps = new(StringComparer.Ordinal)
    {
        "onClick", "onChange", "onInput", "onSubmit", "onClose", "onFocus", "onBlur",
        "onRowClick", "onSelectionChange", "onSort", "onFilter", "onPageChange", "onViewportChange",
        "onNodeSelect", "onNodeToggle", "onNodeExpand", "onNodeCollapse", "onNodeActivate", "onLoadChildren",
        "onDragStart", "onDragOver", "onDrop", "onDragEnd"
    };

    private static readonly HashSet<string> RejectedNames = new(StringComparer.OrdinalIgnoreCase) { "srcdoc", "formaction" };

    private static readonly HashSet<string> UrlProps = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "srcset", "action", "poster", "background", "cite", "data", "codebase", "ping", "manifest", "xlink:href"
    };

    private static readonly HashSet<string> SafeSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto", "tel" };

    private static readonly string[] UnsafeStyleTokens =
    {
        "\\", "url(", "image-set(", "expression(", "javascript:", "vbscript:", "@import", "behavior:", "-moz-binding"
    };

    private static readonly Regex PropName = new("^[A-Za-z][A-Za-z0-9_:.-]*$", RegexOptions.CultureInvariant);
    private static readonly Regex Scheme = new("^([A-Za-z][A-Za-z0-9+.-]*):", RegexOptions.CultureInvariant);
    private static readonly Regex CssComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>True when <paramref name="name"/> = <paramref name="value"/> may stay on a <paramref name="controlType"/> node.</summary>
    public static bool IsSafe(string controlType, string name, RuntimeValue value)
    {
        if (string.IsNullOrEmpty(name) || !PropName.IsMatch(name) || RejectedNames.Contains(name))
            return false;

        if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            return EventProps.Contains(name);

        if (UrlProps.Contains(name))
        {
            return UiControlSpecRegistry.ListsProp(controlType, name) &&
                   value.Type == ValueType.String &&
                   (name.Equals("srcset", StringComparison.OrdinalIgnoreCase) ? IsSafeSrcSet(value.AsString()) : IsSafeUrl(value.AsString()));
        }

        if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
            return IsSafeStyle(value);

        return true;
    }

    /// <summary>An <c>http</c>, <c>https</c>, <c>mailto</c> or <c>tel</c> URL, or a relative one.</summary>
    public static bool IsSafeUrl(string url)
    {
        // Browsers drop tabs and newlines anywhere in a URL and trim controls and spaces at the ends.
        var normalized = url.Replace("\t", "").Replace("\n", "").Replace("\r", "").Trim(ControlsAndSpace);
        var scheme = Scheme.Match(normalized);
        return !scheme.Success || SafeSchemes.Contains(scheme.Groups[1].Value);
    }

    private static bool IsSafeSrcSet(string srcSet) =>
        srcSet.Split(',').All(candidate => IsSafeUrl(candidate.Trim().Split(' ', 2)[0]));

    private static bool IsSafeStyle(RuntimeValue value)
    {
        if (value.Type == ValueType.String)
            return IsSafeCss(value.AsString());
        if (value.Type == ValueType.Object && value.AsObject() is JsonObject declarations)
            return declarations.GetProperties().All(kvp => IsSafeCss(kvp.Key) && (kvp.Value.Type != ValueType.String || IsSafeCss(kvp.Value.AsString())));
        return true;
    }

    private static bool IsSafeCss(string css)
    {
        var compact = new string(CssComment.Replace(css, "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
        return !UnsafeStyleTokens.Any(compact.Contains);
    }

    private static readonly char[] ControlsAndSpace = Enumerable.Range(0, 0x21).Select(c => (char)c).ToArray();
}
