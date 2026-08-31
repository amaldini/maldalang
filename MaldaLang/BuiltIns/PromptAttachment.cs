// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Metadata for a prompt <c>attachments:</c> entry. Bytes are loaded only at
/// <c>await</c> / <c>runPrompt</c> / <c>think</c> — not when constructing the instance.
/// </summary>
public sealed class PromptAttachment
{
    public const int MaxCount = 8;
    public const string KindImage = "image";
    public const string KindPdf = "pdf";

    public string Kind { get; }
    public string? Path { get; }
    public string? Url { get; }
    public string? FileName { get; }

    public PromptAttachment(string kind, string? path, string? url, string? fileName)
    {
        Kind = kind;
        Path = path;
        Url = url;
        FileName = fileName;
    }

    public static List<PromptAttachment>? ParseListOrNull(RuntimeValue value)
    {
        if (value.Type == ValueType.Null)
            return null;
        if (value.Type != ValueType.Array)
            throw new RuntimeException("Prompt 'attachments' field must be an array of objects.");

        var items = value.AsArray();
        if (items.Count == 0)
            return null;
        if (items.Count > MaxCount)
            throw new RuntimeException($"Prompt attachments: at most {MaxCount} items.");

        var list = new List<PromptAttachment>(items.Count);
        foreach (var item in items)
            list.Add(ParseOne(item));
        return list;
    }

    public static RuntimeValue ToRuntimeArray(IReadOnlyList<PromptAttachment>? attachments)
    {
        if (attachments == null || attachments.Count == 0)
            return RuntimeValue.Null();

        var items = new List<RuntimeValue>(attachments.Count);
        foreach (var attachment in attachments)
        {
            var obj = new JsonObject();
            obj.Set("kind", RuntimeValue.String(attachment.Kind));
            if (!string.IsNullOrEmpty(attachment.Path))
                obj.Set("path", RuntimeValue.String(attachment.Path));
            if (!string.IsNullOrEmpty(attachment.Url))
                obj.Set("url", RuntimeValue.String(attachment.Url));
            if (!string.IsNullOrEmpty(attachment.FileName))
                obj.Set("fileName", RuntimeValue.String(attachment.FileName));
            items.Add(RuntimeValue.Object(obj));
        }

        return RuntimeValue.Array(items);
    }

    public static void ApplyParameterInterpolation(
        List<PromptAttachment> attachments,
        List<string> paramNames,
        List<RuntimeValue> arguments)
    {
        for (int i = 0; i < attachments.Count; i++)
        {
            var item = attachments[i];
            var path = Interpolate(item.Path, paramNames, arguments);
            var url = Interpolate(item.Url, paramNames, arguments);
            var fileName = !string.IsNullOrEmpty(path)
                ? System.IO.Path.GetFileName(path)
                : item.FileName;
            if (path != item.Path || url != item.Url || fileName != item.FileName)
                attachments[i] = new PromptAttachment(item.Kind, path, url, fileName);
        }
    }

    private static PromptAttachment ParseOne(RuntimeValue item)
    {
        if (item.Type != ValueType.Object)
            throw new RuntimeException("Each prompt attachment must be an object with kind/path or kind/url.");

        var obj = item.AsObject();
        var kindValue = GetField(obj, "kind");
        var pathValue = GetField(obj, "path");
        var urlValue = GetField(obj, "url");

        string? path = null;
        string? url = null;

        if (pathValue.Type != ValueType.Null)
        {
            if (CapStdLib.TryGetToken(pathValue, out var token))
            {
                if (!string.Equals(token.Kind, CapabilityToken.KindFileRead, StringComparison.Ordinal))
                    throw new RuntimeException("Prompt attachment path capability kind is '" + token.Kind + "', expected 'fileRead'");
                path = token.Path;
            }
            else if (pathValue.Type == ValueType.String)
            {
                path = pathValue.AsString();
            }
            else
            {
                throw new RuntimeException("Prompt attachment 'path' must be a string or a cap.fileRead token.");
            }
        }

        if (urlValue.Type != ValueType.Null)
        {
            if (urlValue.Type != ValueType.String)
                throw new RuntimeException("Prompt attachment 'url' must be a string.");
            url = urlValue.AsString();
        }

        if (string.IsNullOrEmpty(path) == string.IsNullOrEmpty(url))
        {
            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(url))
                throw new RuntimeException("Each prompt attachment needs a 'path' or a 'url', not both.");
            throw new RuntimeException("Each prompt attachment needs a 'path' or a 'url', not both.");
        }

        if (!string.IsNullOrEmpty(url))
            ValidateHttpUrl(url);

        string kind;
        if (kindValue.Type == ValueType.String && !string.IsNullOrWhiteSpace(kindValue.AsString()))
        {
            kind = kindValue.AsString().Trim().ToLowerInvariant();
            if (kind != KindImage && kind != KindPdf)
                throw new RuntimeException("Prompt attachment kind must be 'image' or 'pdf'.");
        }
        else
        {
            kind = InferKind(path, url);
        }

        if (kind == KindPdf && !string.IsNullOrEmpty(url))
            throw new RuntimeException("Prompt attachment url is only allowed for kind 'image'.");

        var fileName = !string.IsNullOrEmpty(path)
            ? System.IO.Path.GetFileName(path)
            : TryFileNameFromUrl(url);

        return new PromptAttachment(kind, path, url, fileName);
    }

    internal static string InferKind(string? path, string? url)
    {
        var source = path ?? url ?? "";
        string ext;
        try
        {
            ext = System.IO.Path.GetExtension(source).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            ext = "";
        }

        if (ext == ".pdf")
            return KindPdf;
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif")
            return KindImage;

        throw new RuntimeException(
            "Prompt attachment kind could not be inferred; set kind to 'image' or 'pdf'.");
    }

    private static void ValidateHttpUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new RuntimeException("Prompt attachment url must be an http or https URL.");
        }
    }

    private static string? TryFileNameFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;
        var name = System.IO.Path.GetFileName(uri.AbsolutePath);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static string? Interpolate(string? text, List<string> paramNames, List<RuntimeValue> arguments)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        for (int p = 0; p < paramNames.Count; p++)
        {
            var placeholder = "{" + paramNames[p] + "}";
            if (text.Contains(placeholder))
                text = text.Replace(placeholder, arguments[p].ToString());
        }

        return text;
    }

    internal static RuntimeValue GetField(ObjectInstance obj, string name)
    {
        if (obj is DictionaryInstance dict)
            return dict.TryGetEntry(name, out var value) ? value : RuntimeValue.Null();
        if (obj is JsonObject json)
            return json.Get(name);
        try
        {
            return obj.Get(name, null) ?? RuntimeValue.Null();
        }
        catch
        {
            return RuntimeValue.Null();
        }
    }
}
