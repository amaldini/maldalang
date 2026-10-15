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
    // ========== Date/Time Functions ==========
    
    private static RuntimeValue BuiltInNow(List<RuntimeValue> args)
    {
        if (args.Count != 0) throw new Exception("now() expects 0 arguments");
        var now = DateTimeOffset.UtcNow;
        return RuntimeValue.Float(now.ToUnixTimeMilliseconds());
    }
    
    private static RuntimeValue BuiltInFormatDate(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("formatDate() expects 1-2 arguments: (timestamp, format?)");
        var timestamp = args[0];
        var format = args.Count > 1 ? args[1] : null;
        
        double timestampMs;
        if (timestamp.Type == ValueType.Integer)
            timestampMs = timestamp.AsInteger();
        else if (timestamp.Type == ValueType.Float)
            timestampMs = timestamp.AsFloat();
        else
            throw new Exception("formatDate() timestamp must be a number");
        
        var dateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)timestampMs).DateTime;
        var formatStr = format != null && format.Type == ValueType.String ? format.AsString() : "yyyy-MM-dd HH:mm:ss";
        
        return RuntimeValue.String(dateTime.ToString(formatStr));
    }
    
    private static RuntimeValue BuiltInParseDate(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("parseDate() expects 1 argument: (dateString)");
        if (args[0].Type != ValueType.String)
            throw new Exception("parseDate() expects a string argument");
        
        var dateStr = args[0].AsString();
        if (DateTimeOffset.TryParse(dateStr, out var dateTime))
        {
            return RuntimeValue.Float(dateTime.ToUnixTimeMilliseconds());
        }
        throw new Exception($"parseDate() could not parse date string: {dateStr}");
    }
    
    private static RuntimeValue BuiltInAddDays(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("addDays() expects 2 arguments: (timestamp, days)");
        var timestamp = args[0];
        var days = args[1];
        
        double timestampMs;
        if (timestamp.Type == ValueType.Integer)
            timestampMs = timestamp.AsInteger();
        else if (timestamp.Type == ValueType.Float)
            timestampMs = timestamp.AsFloat();
        else
            throw new Exception("addDays() timestamp must be a number");
        
        double daysVal;
        if (days.Type == ValueType.Integer)
            daysVal = days.AsInteger();
        else if (days.Type == ValueType.Float)
            daysVal = days.AsFloat();
        else
            throw new Exception("addDays() days must be a number");
        
        var dateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)timestampMs);
        var result = dateTime.AddDays(daysVal);
        return RuntimeValue.Float(result.ToUnixTimeMilliseconds());
    }
    
    private static RuntimeValue BuiltInAddHours(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("addHours() expects 2 arguments: (timestamp, hours)");
        var timestamp = args[0];
        var hours = args[1];
        
        double timestampMs;
        if (timestamp.Type == ValueType.Integer)
            timestampMs = timestamp.AsInteger();
        else if (timestamp.Type == ValueType.Float)
            timestampMs = timestamp.AsFloat();
        else
            throw new Exception("addHours() timestamp must be a number");
        
        double hoursVal;
        if (hours.Type == ValueType.Integer)
            hoursVal = hours.AsInteger();
        else if (hours.Type == ValueType.Float)
            hoursVal = hours.AsFloat();
        else
            throw new Exception("addHours() hours must be a number");
        
        var dateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)timestampMs);
        var result = dateTime.AddHours(hoursVal);
        return RuntimeValue.Float(result.ToUnixTimeMilliseconds());
    }
}
