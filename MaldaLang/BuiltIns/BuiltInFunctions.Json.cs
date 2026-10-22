// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInParseJSON(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new Exception("parseJSON() expects 1 argument (the JSON string). For schema-validated LLM JSON use parseJson(value, schemaRef).");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("parseJSON() expects a string argument");
        
        try
        {
            var jsonString = args[0].AsString();
            using var doc = JsonDocument.Parse(jsonString);
            return JsonToRuntimeValue(doc.RootElement);
        }
        catch (JsonException)
        {
            throw new Exception("Invalid JSON string");
        }
    }
    
    internal static RuntimeValue JsonToRuntimeValue(JsonElement element)
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
    
    private static RuntimeValue BuiltInToJSON(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("toJSON() expects 1 argument");
        
        var json = RuntimeValueToJson(args[0]);
        return RuntimeValue.String(json);
    }

    /// <summary>
    /// Serializes a RuntimeValue to a JSON string. Used by the transpiler to embed prompt return-type schemas in generated code.
    /// </summary>
    public static string SerializeToJson(RuntimeValue value)
    {
        return RuntimeValueToJson(value);
    }

    private static string RuntimeValueToJson(RuntimeValue value)
    {
        return RuntimeValueToJson(value, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static string RuntimeValueToJson(RuntimeValue value, HashSet<object> seen)
    {
        switch (value.Type)
        {
            case MaldaLang.Interpreter.ValueType.String:
                return JsonSerializer.Serialize(value.AsString());
            
            case MaldaLang.Interpreter.ValueType.Integer:
                return value.AsInteger().ToString();
            
            case MaldaLang.Interpreter.ValueType.Float:
                {
                    var f = value.AsFloat();
                    // JSON has no NaN/Infinity; unquoted tokens break browser JSON.parse (e.g. SSE live handlers).
                    if (double.IsNaN(f) || double.IsInfinity(f))
                        return "null";
                    return f.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                }
            
            case MaldaLang.Interpreter.ValueType.Boolean:
                return value.AsBoolean().ToString().ToLower();
            
            case MaldaLang.Interpreter.ValueType.Null:
                return "null";
            
            case MaldaLang.Interpreter.ValueType.Array:
                var arr = value.AsArray();
                if (!seen.Add(arr))
                    throw new Exception("toJSON() cannot serialize a cyclic value");
                try
                {
                    var items = arr.Select(item => RuntimeValueToJson(item, seen));
                    return "[" + string.Join(",", items) + "]";
                }
                finally
                {
                    seen.Remove(arr);
                }
            
            case MaldaLang.Interpreter.ValueType.Object:
                var obj = value.AsObject();
                if (!seen.Add(obj))
                    throw new Exception("toJSON() cannot serialize a cyclic value");
                try
                {
                    return RuntimeObjectToJson(obj, seen);
                }
                finally
                {
                    seen.Remove(obj);
                }
            
            default:
                return "\"<" + value.Type + ">\"";
        }
    }

    private static string RuntimeObjectToJson(ObjectInstance obj, HashSet<object> seen)
    {
        if (obj is JsonObject jsonObj)
        {
            var props = new List<string>();
            foreach (var kvp in jsonObj.GetProperties())
            {
                var key = JsonSerializer.Serialize(kvp.Key);
                var val = RuntimeValueToJson(kvp.Value, seen);
                props.Add($"{key}:{val}");
            }
            return "{" + string.Join(",", props) + "}";
        }

        if (obj is DictionaryInstance dictObj)
        {
            var props = new List<string>();
            foreach (var kvp in dictObj.GetEntries())
            {
                var key = JsonSerializer.Serialize(kvp.Key);
                var val = RuntimeValueToJson(kvp.Value, seen);
                props.Add($"{key}:{val}");
            }
            return "{" + string.Join(",", props) + "}";
        }

        // User class instances only. Built-in ObjectInstance subclasses (HTTP, graphs, …)
        // keep the previous "{}" dump so internal host state is not leaked.
        if (obj.GetType() == typeof(ObjectInstance) && obj.Class != null)
            return ClassInstanceToJson(obj, seen);

        return "{}";
    }

    private static string ClassInstanceToJson(ObjectInstance instance, HashSet<object> seen)
    {
        var order = new List<string>();
        var values = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
        foreach (var field in instance.Class!.EnumeratePublicInstanceFieldsFromBase())
        {
            if (!values.ContainsKey(field.Name))
                order.Add(field.Name);
            instance.TryGet(field.Name, out var fieldValue);
            values[field.Name] = fieldValue ?? RuntimeValue.Null();
        }

        var props = new List<string>(order.Count);
        foreach (var name in order)
        {
            var key = JsonSerializer.Serialize(name);
            var val = RuntimeValueToJson(values[name], seen);
            props.Add($"{key}:{val}");
        }
        return "{" + string.Join(",", props) + "}";
    }
}
