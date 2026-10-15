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
    // ========== Random Functions ==========
    
    private static Random _random = new Random();
    
    private static RuntimeValue BuiltInRandom(List<RuntimeValue> args)
    {
        if (args.Count != 0) throw new Exception("random() expects 0 arguments");
        return RuntimeValue.Float(_random.NextDouble());
    }
    
    private static RuntimeValue BuiltInRandomInt(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("randomInt() expects 2 arguments: (min, max)");
        if (!NumericCoercion.TryAsInteger(args[0], out var min)
            || !NumericCoercion.TryAsInteger(args[1], out var max))
            throw new Exception("randomInt() expects integer arguments");

        if (min > max)
            throw new Exception("randomInt() min must be <= max");
        
        return RuntimeValue.Integer(_random.Next(min, max + 1));
    }
    
    private static RuntimeValue BuiltInRandomFloat(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("randomFloat() expects 2 arguments: (min, max)");
        double min, max;
        
        if (args[0].Type == ValueType.Integer)
            min = args[0].AsInteger();
        else if (args[0].Type == ValueType.Float)
            min = args[0].AsFloat();
        else
            throw new Exception("randomFloat() min must be a number");
        
        if (args[1].Type == ValueType.Integer)
            max = args[1].AsInteger();
        else if (args[1].Type == ValueType.Float)
            max = args[1].AsFloat();
        else
            throw new Exception("randomFloat() max must be a number");
        
        if (min > max)
            throw new Exception("randomFloat() min must be <= max");
        
        var range = max - min;
        return RuntimeValue.Float(min + _random.NextDouble() * range);
    }
}
