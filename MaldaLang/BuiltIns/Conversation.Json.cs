// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using MaldaLang.Runtime.Tracing;
using Spectre.Console;
using ValueType = MaldaLang.Interpreter.ValueType;

public partial class ConversationInstance
{
    private string SerializeRuntimeValueToJson(RuntimeValue value)
    {
        switch (value.Type)
        {
            case ValueType.String:
                return JsonSerializer.Serialize(value.AsString());
            
            case ValueType.Integer:
                return value.AsInteger().ToString();
            
            case ValueType.Float:
                return value.AsFloat().ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            
            case ValueType.Boolean:
                return value.AsBoolean().ToString().ToLower();
            
            case ValueType.Null:
                return "null";
            
            case ValueType.Array:
                var arr = value.AsArray();
                var items = arr.Select(SerializeRuntimeValueToJson);
                return "[" + string.Join(",", items) + "]";
            
            case ValueType.Object:
                var obj = value.AsObject();
                if (obj is JsonObject jsonObj)
                {
                    var props = new List<string>();
                    foreach (var kvp in jsonObj.GetProperties())
                    {
                        var key = JsonSerializer.Serialize(kvp.Key);
                        var val = SerializeRuntimeValueToJson(kvp.Value);
                        props.Add($"{key}:{val}");
                    }
                    return "{" + string.Join(",", props) + "}";
                }
                // For regular ObjectInstance, return empty object for now
                return "{}";
            
            default:
                return JsonSerializer.Serialize($"<{value.Type}>");
        }
    }
    
    private RuntimeValue? TryExtractFromTruncatedJson(string json)
    {
        // Try to extract filePath and content from truncated JSON
        // This is a fallback for when JSON is truncated but we can still extract the values we need
        try
        {
            var result = new JsonObject();
            bool foundAny = false;
            
            // Try to extract filePath - it should be complete even if content is truncated
            var filePathMatch = System.Text.RegularExpressions.Regex.Match(
                json, 
                @"""filePath""\s*:\s*""((?:[^""\\]|\\.)*)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
            if (filePathMatch.Success)
            {
                var filePath = UnescapeJsonString(filePathMatch.Groups[1].Value);
                result.Set("filePath", RuntimeValue.String(filePath));
                foundAny = true;
            }
            
            // Try to extract content - this might be truncated
            // Look for "content":" and extract everything after it
            var contentStartMatch = System.Text.RegularExpressions.Regex.Match(
                json,
                @"""content""\s*:\s*""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
            if (contentStartMatch.Success)
            {
                // Extract everything after "content":"
                var contentStart = contentStartMatch.Index + contentStartMatch.Length;
                var contentRaw = json.Substring(contentStart);
                
                // Try to unescape the content, handling truncation
                var unescapedContent = UnescapeJsonStringTruncated(contentRaw);
                result.Set("content", RuntimeValue.String(unescapedContent));
                foundAny = true;
            }
            
            return foundAny ? RuntimeValue.Object(result) : null;
        }
        catch
        {
            return null;
        }
    }
    
    private string TryFixJsonEscapeSequences(string json)
    {
        // This method attempts to fix common JSON escape sequence issues
        // It's conservative - only fixes clearly malformed sequences, not truncated ones
        var result = new StringBuilder();
        bool inString = false;
        bool isEscaped = false;
        
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            
            if (!inString)
            {
                result.Append(c);
                if (c == '"')
                {
                    inString = true;
                }
                continue;
            }
            
            // We're inside a string
            if (isEscaped)
            {
                if (c == 'u')
                {
                    // Unicode escape sequence: \uXXXX
                    if (i + 4 < json.Length)
                    {
                        // Complete unicode escape - check if valid
                        var hex = json.Substring(i + 1, 4);
                        if (System.Text.RegularExpressions.Regex.IsMatch(hex, @"^[0-9A-Fa-f]{4}$"))
                        {
                            // Valid unicode escape - keep as is
                            result.Append('\\').Append('u').Append(hex);
                            i += 4;
                        }
                        else
                        {
                            // Invalid hex digits - replace with null character escape
                            result.Append('\\').Append('u').Append("0000");
                            i += 4;
                        }
                    }
                    else
                    {
                        // Truncated unicode escape - replace with a safe character (space)
                        // This prevents JSON parse errors while indicating data loss
                        result.Append(' ');
                        var remaining = json.Length - i - 1;
                        if (remaining > 0)
                        {
                            i += remaining;
                        }
                    }
                    isEscaped = false;
                }
                else if (c == '\\' || c == '"' || c == '/' || c == 'b' || c == 'f' || c == 'n' || c == 'r' || c == 't')
                {
                    // Valid escape sequences
                    result.Append('\\').Append(c);
                    isEscaped = false;
                }
                else
                {
                    // Unknown escape sequence - keep as is (might be valid in some contexts)
                    result.Append('\\').Append(c);
                    isEscaped = false;
                }
            }
            else if (c == '\\')
            {
                isEscaped = true;
            }
            else if (c == '"')
            {
                result.Append(c);
                inString = false;
            }
            else
            {
                result.Append(c);
            }
        }
        
        // If we ended in the middle of an escape sequence, complete it safely
        if (isEscaped)
        {
            result.Append('\\');
        }
        
        return result.ToString();
    }
    
    private string UnescapeJsonString(string escaped)
    {
        var result = new StringBuilder();
        bool isEscaped = false;
        for (int i = 0; i < escaped.Length; i++)
        {
            char c = escaped[i];
            if (isEscaped)
            {
                switch (c)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        // Unicode escape: \uXXXX
                        if (i + 4 < escaped.Length)
                        {
                            var hex = escaped.Substring(i + 1, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                            {
                                result.Append((char)code);
                                i += 4;
                            }
                            else
                            {
                                result.Append('\\').Append('u').Append(hex);
                                i += 4;
                            }
                        }
                        else
                        {
                            result.Append('\\').Append(c);
                        }
                        break;
                    default: result.Append('\\').Append(c); break;
                }
                isEscaped = false;
            }
            else if (c == '\\')
            {
                isEscaped = true;
            }
            else if (c == '"')
            {
                // End of string
                break;
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
    
    private string UnescapeJsonStringTruncated(string escaped)
    {
        // Same as UnescapeJsonString but doesn't stop at closing quote (for truncated strings)
        var result = new StringBuilder();
        bool isEscaped = false;
        for (int i = 0; i < escaped.Length; i++)
        {
            char c = escaped[i];
            if (isEscaped)
            {
                switch (c)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        // Unicode escape: \uXXXX
                        if (i + 4 < escaped.Length)
                        {
                            var hex = escaped.Substring(i + 1, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                            {
                                result.Append((char)code);
                                i += 4;
                            }
                            else
                            {
                                result.Append('\\').Append('u').Append(hex);
                                i += 4;
                            }
                        }
                        else
                        {
                            // Truncated unicode escape - just append what we have
                            result.Append('\\').Append(c);
                        }
                        break;
                    default: result.Append('\\').Append(c); break;
                }
                isEscaped = false;
            }
            else if (c == '\\')
            {
                isEscaped = true;
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
    
    private RuntimeValue JsonToRuntimeValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var jsonObj = new JsonObject();
                foreach (var prop in element.EnumerateObject())
                {
                    jsonObj.Set(prop.Name, JsonToRuntimeValue(prop.Value));
                }
                return RuntimeValue.Object(jsonObj);
            
            case JsonValueKind.Array:
                var arr = new List<RuntimeValue>();
                foreach (var item in element.EnumerateArray())
                {
                    arr.Add(JsonToRuntimeValue(item));
                }
                return RuntimeValue.Array(arr);
            
            case JsonValueKind.String:
                return RuntimeValue.String(element.GetString() ?? "");
            
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intVal))
                    return RuntimeValue.Integer(intVal);
                return RuntimeValue.Float(element.GetDouble());
            
            case JsonValueKind.True:
                return RuntimeValue.Boolean(true);
            
            case JsonValueKind.False:
                return RuntimeValue.Boolean(false);
            
            case JsonValueKind.Null:
                return RuntimeValue.Null();
            
            default:
                return RuntimeValue.Null();
        }
    }
}
