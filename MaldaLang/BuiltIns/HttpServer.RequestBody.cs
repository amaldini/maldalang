// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using System.IO;
using MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;

public partial class HttpServerInstance
{
    private async Task<RuntimeValue> ParseRequestBodyAsync(HttpListenerRequest request)
    {
        var contentType = request.ContentType ?? "";

        if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream();
            await request.InputStream.CopyToAsync(ms);
            var bodyBytes = ms.ToArray();
            if (bodyBytes.Length == 0)
                return RuntimeValue.Null();
            var parsed = ParseMultipartFormData(bodyBytes, contentType);
            if (parsed != null)
                return RuntimeValue.Object(parsed);
            return RuntimeValue.String(Encoding.UTF8.GetString(bodyBytes));
        }

        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var bodyText = await reader.ReadToEndAsync();
        
        if (string.IsNullOrEmpty(bodyText))
            return RuntimeValue.Null();
        
        if (contentType.Contains("application/json"))
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(bodyText);
                return JsonToRuntimeValue(jsonDoc.RootElement);
            }
            catch
            {
                // If JSON parsing fails, try to parse as form-urlencoded as fallback
                // This handles cases where Content-Type is set incorrectly
                if (bodyText.Contains('&') && bodyText.Contains('='))
                {
                    return RuntimeValue.Object(ParseUrlEncodedBody(bodyText));
                }
                return RuntimeValue.String(bodyText);
            }
        }
        else if (contentType.Contains("application/x-www-form-urlencoded") || 
                 (string.IsNullOrEmpty(contentType) && bodyText.Contains('&') && bodyText.Contains('=')))
        {
            // Handle form-urlencoded data, or data that looks like form-urlencoded even without Content-Type
            return RuntimeValue.Object(ParseUrlEncodedBody(bodyText));
        }
        
        // Default: return as string if we can't parse it
        return RuntimeValue.String(bodyText);
    }

    private static JsonObject ParseUrlEncodedBody(string bodyText)
    {
        var jsonObj = new JsonObject();
        var pairs = bodyText.Split('&');
        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair))
            {
                continue;
            }

            var parts = pair.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var key = DecodeFormValue(parts[0]);
            var value = DecodeFormValue(parts[1]);
            AppendFormField(jsonObj, key, RuntimeValue.String(value));
        }

        return jsonObj;
    }

    /// <summary>
    /// HTML forms may repeat the same name (e.g. checkbox group <c>tags=a&amp;tags=b</c>).
    /// First value stays a string; further values promote/append to an array.
    /// </summary>
    private static void AppendFormField(JsonObject jsonObj, string key, RuntimeValue value)
    {
        if (!jsonObj.TryGet(key, out var existing) || existing == null || existing.Type == ValueType.Null)
        {
            jsonObj.Set(key, value);
            return;
        }

        if (existing.Type == ValueType.Array)
        {
            var list = new List<RuntimeValue>(existing.AsArray()) { value };
            jsonObj.Set(key, RuntimeValue.Array(list));
            return;
        }

        jsonObj.Set(key, RuntimeValue.Array(new List<RuntimeValue> { existing, value }));
    }

    private static string DecodeFormValue(string value)
    {
        // x-www-form-urlencoded uses '+' for spaces.
        return Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
    }

    private static JsonObject? ParseMultipartFormData(byte[] body, string contentType)
    {
        // Byte-safe parser: file parts expose contentBase64 so PDF/DOCX survive.
        var boundaryMatch = Regex.Match(contentType, "boundary=(?:\"(?<b>[^\"]+)\"|(?<b>[^;]+))", RegexOptions.IgnoreCase);
        if (!boundaryMatch.Success)
        {
            return null;
        }

        var boundary = boundaryMatch.Groups["b"].Value.Trim();
        if (string.IsNullOrWhiteSpace(boundary))
        {
            return null;
        }

        var marker = Encoding.ASCII.GetBytes("--" + boundary);
        var crlfHeaderSep = Encoding.ASCII.GetBytes("\r\n\r\n");
        var lfHeaderSep = Encoding.ASCII.GetBytes("\n\n");
        var result = new JsonObject();

        var searchFrom = 0;
        while (true)
        {
            var boundAt = IndexOfBytes(body, marker, searchFrom);
            if (boundAt < 0)
            {
                break;
            }

            var afterMarker = boundAt + marker.Length;
            if (afterMarker + 1 < body.Length && body[afterMarker] == (byte)'-' && body[afterMarker + 1] == (byte)'-')
            {
                break;
            }

            var partStart = afterMarker;
            if (partStart + 1 < body.Length && body[partStart] == (byte)'\r' && body[partStart + 1] == (byte)'\n')
            {
                partStart += 2;
            }
            else if (partStart < body.Length && body[partStart] == (byte)'\n')
            {
                partStart += 1;
            }

            var nextBound = IndexOfBytes(body, marker, partStart);
            if (nextBound < 0)
            {
                break;
            }

            var partEnd = nextBound;
            if (partEnd >= 2 && body[partEnd - 2] == (byte)'\r' && body[partEnd - 1] == (byte)'\n')
            {
                partEnd -= 2;
            }
            else if (partEnd >= 1 && body[partEnd - 1] == (byte)'\n')
            {
                partEnd -= 1;
            }

            if (partEnd > partStart)
            {
                ParseMultipartPart(body, partStart, partEnd, crlfHeaderSep, lfHeaderSep, result);
            }

            searchFrom = nextBound;
        }

        return result;
    }

    private static void ParseMultipartPart(
        byte[] body,
        int partStart,
        int partEnd,
        byte[] crlfHeaderSep,
        byte[] lfHeaderSep,
        JsonObject result)
    {
        var sepAt = IndexOfBytes(body, crlfHeaderSep, partStart, partEnd);
        var sepLen = 4;
        if (sepAt < 0)
        {
            sepAt = IndexOfBytes(body, lfHeaderSep, partStart, partEnd);
            sepLen = 2;
        }

        if (sepAt < 0)
        {
            return;
        }

        var headers = Encoding.UTF8.GetString(body, partStart, sepAt - partStart);
        var contentStart = sepAt + sepLen;
        var contentLen = Math.Max(0, partEnd - contentStart);
        var contentBytes = new byte[contentLen];
        if (contentLen > 0)
        {
            Buffer.BlockCopy(body, contentStart, contentBytes, 0, contentLen);
        }

        var disposition = headers
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(h => h.StartsWith("Content-Disposition", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(disposition))
        {
            return;
        }

        var nameMatch = Regex.Match(disposition, "name=(?:\"(?<n>[^\"]+)\"|(?<n>[^;\\r\\n]+))", RegexOptions.IgnoreCase);
        if (!nameMatch.Success)
        {
            return;
        }

        var fieldName = nameMatch.Groups["n"].Value.Trim();
        var fileNameMatch = Regex.Match(disposition, "filename=(?:\"(?<f>[^\"]*)\"|(?<f>[^;\\r\\n]+))", RegexOptions.IgnoreCase);
        var contentUtf8 = Encoding.UTF8.GetString(contentBytes);
        if (fileNameMatch.Success)
        {
            var fileObj = new JsonObject();
            fileObj.Set("fileName", RuntimeValue.String(fileNameMatch.Groups["f"].Value));
            fileObj.Set("content", RuntimeValue.String(contentUtf8));
            fileObj.Set("contentBase64", RuntimeValue.String(Convert.ToBase64String(contentBytes)));
            var typeLine = headers
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(h => h.StartsWith("Content-Type", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(typeLine))
            {
                var colon = typeLine.IndexOf(':');
                if (colon >= 0)
                {
                    fileObj.Set("contentType", RuntimeValue.String(typeLine.Substring(colon + 1).Trim()));
                }
            }
            AppendFormField(result, fieldName, RuntimeValue.Object(fileObj));
        }
        else
        {
            AppendFormField(result, fieldName, RuntimeValue.String(contentUtf8));
        }
    }

    private static int IndexOfBytes(byte[] haystack, byte[] needle, int start)
    {
        return IndexOfBytes(haystack, needle, start, haystack.Length);
    }

    private static int IndexOfBytes(byte[] haystack, byte[] needle, int start, int endExclusive)
    {
        if (needle.Length == 0 || start < 0)
        {
            return -1;
        }

        var last = endExclusive - needle.Length;
        for (var i = start; i <= last; i++)
        {
            var j = 0;
            for (; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    break;
                }
            }

            if (j == needle.Length)
            {
                return i;
            }
        }

        return -1;
    }
    
    private RuntimeValue JsonToRuntimeValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonObjectToRuntimeValue(element),
            JsonValueKind.Array => JsonArrayToRuntimeValue(element),
            JsonValueKind.String => RuntimeValue.String(element.GetString() ?? ""),
            JsonValueKind.Number => element.TryGetInt32(out var intVal) 
                ? RuntimeValue.Integer(intVal) 
                : RuntimeValue.Float(element.GetDouble()),
            JsonValueKind.True => RuntimeValue.Boolean(true),
            JsonValueKind.False => RuntimeValue.Boolean(false),
            JsonValueKind.Null => RuntimeValue.Null(),
            _ => RuntimeValue.Null()
        };
    }
    
    private RuntimeValue JsonObjectToRuntimeValue(JsonElement element)
    {
        var jsonObj = new JsonObject();
        foreach (var prop in element.EnumerateObject())
        {
            jsonObj.Set(prop.Name, JsonToRuntimeValue(prop.Value));
        }
        return RuntimeValue.Object(jsonObj);
    }
    
    private RuntimeValue JsonArrayToRuntimeValue(JsonElement element)
    {
        var list = new List<RuntimeValue>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(JsonToRuntimeValue(item));
        }
        return RuntimeValue.Array(list);
    }
}
