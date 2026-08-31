// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Encode prompt attachments as OpenAI-compatible content parts, and refuse
/// them on in-process GGUF (text-only).
/// </summary>
public static class PromptAttachmentCodec
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const string LocalBackendMessage =
        "Multimodal prompt attachments require an HTTP vision-capable client (LLMClient / OpenRouterClient). In-process GGUF is text-only.";

    public static RuntimeValue BuildContentParts(string userText, IReadOnlyList<PromptAttachment> attachments)
    {
        var parts = new List<RuntimeValue>(1 + attachments.Count);
        var textPart = new JsonObject();
        textPart.Set("type", RuntimeValue.String("text"));
        textPart.Set("text", RuntimeValue.String(userText ?? ""));
        parts.Add(RuntimeValue.Object(textPart));

        foreach (var attachment in attachments)
            parts.Add(EncodeOne(attachment));

        return RuntimeValue.Array(parts);
    }

    public static object? ToWireContent(RuntimeValue content)
    {
        if (content.Type == ValueType.String)
            return content.AsString();
        if (content.Type != ValueType.Array)
            return null;

        var parts = new List<object?>();
        foreach (var item in content.AsArray())
        {
            if (item.Type != ValueType.Object)
                continue;
            var dict = ObjectToDict(item.AsObject());
            if (dict != null)
                parts.Add(dict);
        }

        return parts;
    }

    public static void EnsureLocalBackendAllows(IReadOnlyList<RuntimeValue> messages)
    {
        foreach (var msg in messages)
        {
            if (msg.Type != ValueType.Object)
                continue;
            var content = PromptAttachment.GetField(msg.AsObject(), "content");
            if (HasNonTextContentParts(content))
                throw new RuntimeException(LocalBackendMessage);
        }
    }

    public static bool HasNonTextContentParts(RuntimeValue content)
    {
        if (content.Type != ValueType.Array)
            return false;

        foreach (var item in content.AsArray())
        {
            if (item.Type != ValueType.Object)
                continue;
            var type = PromptAttachment.GetField(item.AsObject(), "type");
            if (type.Type == ValueType.String)
            {
                var t = type.AsString();
                if (!string.Equals(t, "text", StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    public static object TraceSummary(IReadOnlyList<PromptAttachment> attachments)
    {
        var items = new List<object>(attachments.Count);
        foreach (var attachment in attachments)
        {
            items.Add(new
            {
                kind = attachment.Kind,
                path = attachment.Path,
                url = attachment.Url,
                fileName = attachment.FileName
            });
        }

        return items;
    }

    private static RuntimeValue EncodeOne(PromptAttachment attachment)
    {
        if (string.Equals(attachment.Kind, PromptAttachment.KindImage, StringComparison.Ordinal))
            return EncodeImage(attachment);
        if (string.Equals(attachment.Kind, PromptAttachment.KindPdf, StringComparison.Ordinal))
            return EncodePdf(attachment);

        throw new RuntimeException("Prompt attachment kind must be 'image' or 'pdf'.");
    }

    private static RuntimeValue EncodeImage(PromptAttachment attachment)
    {
        string url;
        if (!string.IsNullOrEmpty(attachment.Url))
        {
            url = attachment.Url!;
        }
        else
        {
            var bytes = ReadBytes(attachment.Path!);
            var mime = MimeFromPath(attachment.Path!, PromptAttachment.KindImage);
            url = ToDataUrl(mime, bytes);
        }

        var imageUrl = new JsonObject();
        imageUrl.Set("url", RuntimeValue.String(url));
        var part = new JsonObject();
        part.Set("type", RuntimeValue.String("image_url"));
        part.Set("image_url", RuntimeValue.Object(imageUrl));
        return RuntimeValue.Object(part);
    }

    private static RuntimeValue EncodePdf(PromptAttachment attachment)
    {
        var bytes = ReadBytes(attachment.Path!);
        var fileName = string.IsNullOrEmpty(attachment.FileName) ? "document.pdf" : attachment.FileName;
        var file = new JsonObject();
        file.Set("filename", RuntimeValue.String(fileName));
        file.Set("file_data", RuntimeValue.String(ToDataUrl("application/pdf", bytes)));
        var part = new JsonObject();
        part.Set("type", RuntimeValue.String("file"));
        part.Set("file", RuntimeValue.Object(file));
        return RuntimeValue.Object(part);
    }

    internal static byte[] ReadBytes(string path)
    {
        byte[]? bytes;
        try
        {
            if (EmbeddedFolderStore.IsEmbedPath(path))
            {
                bytes = EmbeddedFolderStore.ReadBytes(path);
                if (bytes == null)
                    throw new RuntimeException($"Prompt attachment file not found: {path}");
            }
            else
            {
                if (!File.Exists(path))
                    throw new RuntimeException($"Prompt attachment file not found: {path}");
                bytes = File.ReadAllBytes(path);
            }
        }
        catch (RuntimeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"Prompt attachment could not be read ({path}): {ex.Message}");
        }

        if (bytes.Length > MaxBytes)
            throw new RuntimeException($"Prompt attachment '{path}' exceeds 10 MB.");

        return bytes;
    }

    internal static string MimeFromPath(string path, string kind)
    {
        string ext;
        try
        {
            ext = Path.GetExtension(path).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            ext = "";
        }

        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            _ => kind == PromptAttachment.KindPdf ? "application/pdf" : "image/png"
        };
    }

    private static string ToDataUrl(string mime, byte[] bytes) =>
        "data:" + mime + ";base64," + Convert.ToBase64String(bytes);

    private static Dictionary<string, object?>? ObjectToDict(ObjectInstance obj)
    {
        IEnumerable<string> keys;
        if (obj is JsonObject json)
            keys = json.GetProperties().Keys;
        else if (obj is DictionaryInstance dict)
            keys = dict.GetAllKeys();
        else
            keys = obj.GetAllKeys();

        var result = new Dictionary<string, object?>();
        foreach (var key in keys)
        {
            var value = PromptAttachment.GetField(obj, key);
            result[key] = WireValue(value);
        }

        return result;
    }

    private static object? WireValue(RuntimeValue value)
    {
        switch (value.Type)
        {
            case ValueType.String:
                return value.AsString();
            case ValueType.Integer:
                return value.AsInteger();
            case ValueType.Float:
                return value.AsFloat();
            case ValueType.Boolean:
                return value.AsBoolean();
            case ValueType.Null:
                return null;
            case ValueType.Object:
                return ObjectToDict(value.AsObject());
            case ValueType.Array:
                var list = new List<object?>();
                foreach (var item in value.AsArray())
                    list.Add(WireValue(item));
                return list;
            default:
                return null;
        }
    }
}
