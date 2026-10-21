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
    private static RuntimeValue BuiltInInt(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("int() expects 1 argument");
        var arg = args[0];
        return arg.Type switch
        {
            MaldaLang.Interpreter.ValueType.Integer => arg,
            MaldaLang.Interpreter.ValueType.Float => RuntimeValue.Integer((int)arg.AsFloat()),
            // A string that is not an integer gives 0, as on the C# and JavaScript backends.
            MaldaLang.Interpreter.ValueType.String => RuntimeValue.Integer(
                int.TryParse(arg.AsString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0),
            _ => throw new Exception("Cannot convert to int")
        };
    }

    private static RuntimeValue BuiltInToIntOr(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("toIntOr() expects 2 arguments: (value, fallback)");
        var fallback = CoerceRuntimeValueToInt(args[1], "toIntOr() fallback");
        if (IsNullOrEmptyRuntimeValue(args[0]))
        {
            return RuntimeValue.Integer(fallback);
        }

        return TryCoerceRuntimeValueToInt(args[0], out var value)
            ? RuntimeValue.Integer(value)
            : RuntimeValue.Integer(fallback);
    }

    private static RuntimeValue BuiltInToIntOrNull(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("toIntOrNull() expects 1 argument");
        if (IsNullOrEmptyRuntimeValue(args[0]))
        {
            return RuntimeValue.Null();
        }

        return TryCoerceRuntimeValueToInt(args[0], out var value)
            ? RuntimeValue.Integer(value)
            : RuntimeValue.Null();
    }

    private static bool IsNullOrEmptyRuntimeValue(RuntimeValue value)
    {
        return value.Type == ValueType.Null ||
               (value.Type == ValueType.String && string.IsNullOrWhiteSpace(value.AsString()));
    }

    private static int CoerceRuntimeValueToInt(RuntimeValue value, string label)
    {
        if (TryCoerceRuntimeValueToInt(value, out var parsed))
        {
            return parsed;
        }

        throw new Exception($"{label} must be an integer-compatible value");
    }

    private static bool TryCoerceRuntimeValueToInt(RuntimeValue value, out int parsed)
    {
        switch (value.Type)
        {
            case ValueType.Integer:
                parsed = value.AsInteger();
                return true;
            case ValueType.Float:
                parsed = (int)value.AsFloat();
                return true;
            case ValueType.Boolean:
                parsed = value.AsBoolean() ? 1 : 0;
                return true;
            case ValueType.String:
            {
                var text = value.AsString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    parsed = 0;
                    return false;
                }

                if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var intValue))
                {
                    parsed = intValue;
                    return true;
                }

                if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var doubleValue))
                {
                    parsed = (int)doubleValue;
                    return true;
                }

                parsed = 0;
                return false;
            }
            default:
                parsed = 0;
                return false;
        }
    }
    
    private static RuntimeValue BuiltInFloat(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("float() expects 1 argument");
        var arg = args[0];
        return arg.Type switch
        {
            MaldaLang.Interpreter.ValueType.Float => arg,
            MaldaLang.Interpreter.ValueType.Integer => RuntimeValue.Float(arg.AsInteger()),
            MaldaLang.Interpreter.ValueType.String => RuntimeValue.Float(double.Parse(arg.AsString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)),
            _ => throw new Exception("Cannot convert to float")
        };
    }
    
    private static RuntimeValue BuiltInString(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("string() expects 1 argument");
        return RuntimeValue.String(args[0].ToString());
    }
    
    private static RuntimeValue BuiltInFormatNumber(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("formatNumber() expects 2 arguments (number, decimalPlaces)");
        
        var number = args[0];
        var decimalPlaces = args[1];
        
        if (!NumericCoercion.TryAsInteger(decimalPlaces, out var places))
            throw new Exception("formatNumber() second argument (decimalPlaces) must be an integer");

        if (places < 0)
            throw new Exception("formatNumber() decimalPlaces must be non-negative");
        
        double value;
        if (number.Type == ValueType.Integer)
            value = number.AsInteger();
        else if (number.Type == ValueType.Float)
            value = number.AsFloat();
        else
            throw new Exception("formatNumber() first argument must be a number");
        
        return RuntimeValue.String(value.ToString($"F{places}", System.Globalization.CultureInfo.InvariantCulture));
    }
    
    private static RuntimeValue BuiltInPrint(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count != 1) throw new Exception("print() expects 1 argument");
        var text = args[0].ToString();
        var callback = interpreter?.GetOutputCallback();
        if (callback != null)
            callback(text);
        else
            Console.WriteLine(text);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInDomUnavailable(string builtInName)
    {
        throw new Exception($"{builtInName}() is only available in browser-hosted JavaScript runtime. Verify with compile_malda mode \"js\" (malda compile --mode js).");
    }
}
