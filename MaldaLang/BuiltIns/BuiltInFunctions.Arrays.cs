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
    // ========== Array Utilities ==========
    
    private static RuntimeValue BuiltInJoin(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("join() expects 1-2 arguments: (array, separator?)");
        if (args[0].Type != ValueType.Array)
            throw new Exception("join() expects an array as first argument");
        
        var array = args[0].AsArray();
        var separator = args.Count > 1 && args[1].Type == ValueType.String ? args[1].AsString() : ",";
        
        var stringParts = new List<string>();
        foreach (var item in array)
        {
            stringParts.Add(item.ToString());
        }
        
        return RuntimeValue.String(string.Join(separator, stringParts));
    }

    private static RuntimeValue BuiltInToCsv(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3) throw new Exception("toCsv() expects 1-3 arguments: (rows, columns?, options?)");
        if (args[0].Type != ValueType.Array)
            throw new Exception("toCsv() first argument must be an array");

        var rows = args[0].AsArray();
        var columns = new List<string>();
        JsonObject? options = null;

        if (args.Count >= 2 && args[1].Type != ValueType.Null)
        {
            if (args[1].Type == ValueType.Array)
            {
                foreach (var col in args[1].AsArray())
                {
                    columns.Add(col.Type == ValueType.String ? col.AsString() : col.ToString());
                }
            }
            else if (args[1].Type == ValueType.Object)
            {
                options = CoerceRuntimeObjectToJsonObject(args[1]);
            }
            else
            {
                throw new Exception("toCsv() second argument must be columns array, options object, or null");
            }
        }

        if (args.Count == 3)
        {
            if (args[2].Type == ValueType.Object)
            {
                options = CoerceRuntimeObjectToJsonObject(args[2]);
            }
            else if (args[2].Type != ValueType.Null)
            {
                throw new Exception("toCsv() third argument must be options object or null");
            }
        }

        var delimiter = options != null ? GetStringOptionFromObject(options, "delimiter", ",") : ",";
        var newline = options != null ? GetStringOptionFromObject(options, "newline", "\n") : "\n";
        var includeHeader = options == null || GetBooleanOptionFromObject(options, "includeHeader", true);
        var quoteAll = options != null && GetBooleanOptionFromObject(options, "quoteAll", false);

        if (columns.Count == 0)
        {
            columns = InferCsvColumns(rows);
        }

        var lines = new List<string>();
        if (includeHeader && columns.Count > 0)
        {
            lines.Add(string.Join(delimiter, columns.Select(c => EscapeCsvCell(c, delimiter, quoteAll))));
        }

        foreach (var row in rows)
        {
            var cells = new List<string>();
            for (int i = 0; i < columns.Count; i++)
            {
                var value = ResolveCsvValue(row, columns[i]);
                var text = value.Type == ValueType.Null ? string.Empty : RuntimeValueToTemplateString(value);
                cells.Add(EscapeCsvCell(text, delimiter, quoteAll));
            }

            lines.Add(string.Join(delimiter, cells));
        }

        return RuntimeValue.String(string.Join(newline, lines));
    }

    private static List<string> InferCsvColumns(List<RuntimeValue> rows)
    {
        if (rows.Count == 0)
        {
            return new List<string>();
        }

        var first = rows[0];
        var firstObj = CoerceRuntimeObjectToJsonObject(first);
        if (firstObj == null)
        {
            return new List<string> { "value" };
        }

        return firstObj.GetProperties().Select(kvp => kvp.Key).ToList();
    }

    private static RuntimeValue ResolveCsvValue(RuntimeValue row, string columnName)
    {
        var obj = CoerceRuntimeObjectToJsonObject(row);
        if (obj != null)
        {
            return obj.Get(columnName, null);
        }

        return columnName == "value" ? row : RuntimeValue.Null();
    }

    private static string EscapeCsvCell(string text, string delimiter, bool quoteAll)
    {
        var raw = text ?? string.Empty;
        var escaped = raw.Replace("\"", "\"\"");
        var mustQuote = quoteAll ||
                        escaped.Contains(delimiter, StringComparison.Ordinal) ||
                        escaped.Contains("\"", StringComparison.Ordinal) ||
                        escaped.Contains("\n", StringComparison.Ordinal) ||
                        escaped.Contains("\r", StringComparison.Ordinal);
        return mustQuote ? $"\"{escaped}\"" : escaped;
    }
    
    private static RuntimeValue BuiltInReverse(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("reverse() expects 1 argument");
        if (args[0].Type != ValueType.Array)
            throw new Exception("reverse() expects an array");
        
        var array = args[0].AsArray();
        var reversed = new List<RuntimeValue>(array);
        reversed.Reverse();
        return RuntimeValue.Array(reversed);
    }
    
    private static RuntimeValue BuiltInSort(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("sort() expects 1-2 arguments: (array, compareFn?)");
        if (args[0].Type != ValueType.Array)
            throw new Exception("sort() expects an array as first argument");
        
        var array = args[0].AsArray();
        var sorted = new List<RuntimeValue>(array);
        
        // Simple numeric/string sort if no compare function
        if (args.Count == 1 || args[1].Type == ValueType.Null)
        {
            sorted.Sort((a, b) =>
            {
                // Try to compare as numbers first
                if (a.Type == ValueType.Integer && b.Type == ValueType.Integer)
                    return a.AsInteger().CompareTo(b.AsInteger());
                if ((a.Type == ValueType.Integer || a.Type == ValueType.Float) &&
                    (b.Type == ValueType.Integer || b.Type == ValueType.Float))
                {
                    var aVal = a.Type == ValueType.Integer ? a.AsInteger() : a.AsFloat();
                    var bVal = b.Type == ValueType.Integer ? b.AsInteger() : b.AsFloat();
                    return aVal.CompareTo(bVal);
                }
                // Otherwise compare as strings
                return a.ToString().CompareTo(b.ToString());
            });
        }
        else
        {
            if (args[1].Type != ValueType.Function)
                throw new Exception("sort() second argument must be a function or null");
            if (interpreter == null)
                throw new Exception("sort() with compare function is not supported in transpiled code; use interpreter mode.");
            var compareFn = args[1].AsFunction();
            sorted.Sort((a, b) =>
            {
                var result = interpreter.CallFunctionAsync(compareFn, new List<RuntimeValue> { a, b }).GetAwaiter().GetResult();
                if (result.Type == ValueType.Integer)
                    return result.AsInteger();
                if (result.Type == ValueType.Float)
                    return (int)result.AsFloat();
                if (result.Type == ValueType.Boolean)
                    return result.AsBoolean() ? 1 : -1;
                return result.IsTruthy() ? 1 : -1;
            });
        }
        
        return RuntimeValue.Array(sorted);
    }
    
    private static RuntimeValue BuiltInIncludes(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("includes() expects 2 arguments: (array, value)");
        if (args[0].Type != ValueType.Array)
            throw new Exception("includes() expects an array as first argument");
        
        var array = args[0].AsArray();
        var searchValue = args[1];
        
        foreach (var item in array)
        {
            if (item.Type == searchValue.Type && item.ToString() == searchValue.ToString())
                return RuntimeValue.Boolean(true);
        }
        
        return RuntimeValue.Boolean(false);
    }
    
    // ========== Range Generation ==========
    
    private static RuntimeValue BuiltInRange(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3) throw new Exception("range() expects 1-3 arguments: (end) or (start, end) or (start, end, step)");
        
        int start, end, step = 1;
        
        if (args.Count == 1)
        {
            if (!NumericCoercion.TryAsInteger(args[0], out end))
                throw new Exception("range(end) expects an integer");
            start = 0;
        }
        else
        {
            if (!NumericCoercion.TryAsInteger(args[0], out start)
                || !NumericCoercion.TryAsInteger(args[1], out end))
                throw new Exception("range(start, end) expects integers");

            if (args.Count == 3)
            {
                if (!NumericCoercion.TryAsInteger(args[2], out step))
                    throw new Exception("range(start, end, step) step must be an integer");
                if (step == 0)
                    throw new Exception("range() step cannot be zero");
            }
        }
        
        var result = new List<RuntimeValue>();
        if (step > 0)
        {
            for (int i = start; i < end; i += step)
                result.Add(RuntimeValue.Integer(i));
        }
        else
        {
            for (int i = start; i > end; i += step)
                result.Add(RuntimeValue.Integer(i));
        }
        
        return RuntimeValue.Array(result);
    }
}
