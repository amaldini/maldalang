// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using ValueType = MaldaLang.Interpreter.ValueType;
using System.Text.Json;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;

public partial class GraphMemoryInstance
{
    private static JsonObject? ParseQueryOptions(List<RuntimeValue> args, out int maxResults)
    {
        maxResults = 5;
        JsonObject? options = null;

        if (args.Count >= 2)
        {
            if (args[1].Type == ValueType.Integer)
                maxResults = Math.Max(1, args[1].AsInteger());
            else if (args[1].Type == ValueType.Object)
                options = CoerceToJsonObject(args[1]);
        }

        if (args.Count >= 3 && args[2].Type == ValueType.Object)
            options = CoerceToJsonObject(args[2]) ?? options;

        if (options != null)
        {
            var maxFromOptions = GetIntOption(options, "maxResults", maxResults);
            maxResults = Math.Max(1, maxFromOptions);
        }

        return options;
    }

    /// <summary>
    /// <c>{ }</c> literals are <see cref="JsonObject"/>. <c>dict { }</c> and transpiled
    /// object literals are <see cref="DictionaryInstance"/>. Option helpers expect JsonObject.
    /// </summary>
    private static JsonObject RequireJsonObject(RuntimeValue value, string errorMessage)
    {
        var options = CoerceToJsonObject(value);
        if (options == null)
            throw new RuntimeException(errorMessage);
        return options;
    }

    private static JsonObject? CoerceToJsonObject(RuntimeValue value)
    {
        if (value.Type != ValueType.Object)
            return null;
        var obj = value.AsObject();
        if (obj is JsonObject json)
            return json;
        if (obj is DictionaryInstance dict)
        {
            var copy = new JsonObject();
            foreach (var kvp in dict.GetEntries())
                copy.Set(kvp.Key, kvp.Value);
            return copy;
        }
        return null;
    }
    
    private static int GetIntOption(JsonObject? options, string key, int defaultValue)
    {
        if (options == null)
            return defaultValue;
        var val = options.Get(key, null);
        if (val != null && val.Type == ValueType.Integer)
            return val.AsInteger();
        return defaultValue;
    }
    
    private static bool GetBoolOption(JsonObject? options, string key, bool defaultValue)
    {
        if (options == null)
            return defaultValue;
        var val = options.Get(key, null);
        if (val != null && val.Type == ValueType.Boolean)
            return val.AsBoolean();
        return defaultValue;
    }
    
    private static bool? GetOptionalBoolOption(JsonObject? options, string key)
    {
        if (options == null)
            return null;
        var val = options.Get(key, null);
        if (val != null && val.Type == ValueType.Boolean)
            return val.AsBoolean();
        return null;
    }
    
    private static HashSet<string>? GetStringListOption(JsonObject? options, string key)
    {
        if (options == null)
            return null;
        
        var val = options.Get(key, null);
        if (val == null || val.Type == ValueType.Null)
            return null;
        
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (val.Type == ValueType.Array)
        {
            foreach (var item in val.AsArray())
            {
                if (item.Type == ValueType.String && !string.IsNullOrWhiteSpace(item.AsString()))
                    result.Add(item.AsString().Trim());
            }
        }
        else if (val.Type == ValueType.String)
        {
            foreach (var part in val.AsString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                result.Add(part);
        }
        
        return result.Count > 0 ? result : null;
    }
    
    private static string? GetStringOption(JsonObject? options, string key)
    {
        if (options == null)
            return null;
        var val = options.Get(key, null);
        if (val != null && val.Type == ValueType.String)
        {
            var text = val.AsString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        return null;
    }
    
    private static double GetDoubleOption(JsonObject? options, string key, double defaultValue)
    {
        if (options == null)
            return defaultValue;
        var val = options.Get(key, null);
        if (val == null)
            return defaultValue;
        if (val.Type == ValueType.Float)
            return val.AsFloat();
        if (val.Type == ValueType.Integer)
            return val.AsInteger();
        return defaultValue;
    }
    
    private static bool UseBm25Lexical(JsonObject? options)
    {
        if (GetBoolOption(options, "bm25", false))
            return true;
        var mode = GetStringOption(options, "lexicalMode");
        if (string.Equals(mode, "bm25", StringComparison.OrdinalIgnoreCase))
            return true;
        var env = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_LEXICAL_MODE");
        return string.Equals(env, "bm25", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string>? ResolveScopeHierarchy(string? scopeFilter, JsonObject? options)
    {
        var fromOptions = GetStringListOption(options, "scopeHierarchy");
        if (fromOptions != null && fromOptions.Count > 0)
            return fromOptions;

        var fromEnv = ParseScopeHierarchyEnv(System.Environment.GetEnvironmentVariable("MALDA_MEMORY_SCOPE_HIERARCHY"));
        if (fromEnv != null && fromEnv.Count > 0)
            return MergeScopeHierarchy(scopeFilter, fromEnv);

        if (string.IsNullOrWhiteSpace(scopeFilter))
            return null;

        var hierarchy = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            scopeFilter.Trim(),
            "global"
        };
        var parent = GetStringOption(options, "scopeParent");
        if (string.IsNullOrWhiteSpace(parent))
            parent = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_SCOPE_PARENT");
        if (!string.IsNullOrWhiteSpace(parent))
            hierarchy.Add(parent.Trim());
        return hierarchy;
    }

    private static HashSet<string>? ParseScopeHierarchyEnv(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return null;
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            result.Add(value.Trim());
                    }
                }
                return result.Count > 0 ? result : null;
            }
            catch
            {
                return null;
            }
        }

        var parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;
        return new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> MergeScopeHierarchy(string? scopeFilter, HashSet<string> configuredHierarchy)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(scopeFilter))
            merged.Add(scopeFilter.Trim());
        foreach (var scope in configuredHierarchy)
            merged.Add(scope);
        if (!merged.Contains("global"))
            merged.Add("global");
        return merged;
    }

    private static double NormalizeBm25Score(double raw) =>
        Math.Min(1.0, raw / Bm25ScoreNormalizationCap);
}
