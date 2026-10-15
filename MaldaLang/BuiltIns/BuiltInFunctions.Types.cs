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
    // ========== Type Checking Functions ==========
    
    private static RuntimeValue BuiltInIsNumber(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("isNumber() expects 1 argument");
        var arg = args[0];
        return RuntimeValue.Boolean(arg.Type == ValueType.Integer || arg.Type == ValueType.Float);
    }
    
    private static RuntimeValue BuiltInIsString(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("isString() expects 1 argument");
        return RuntimeValue.Boolean(args[0].Type == ValueType.String);
    }
    
    private static RuntimeValue BuiltInIsArray(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("isArray() expects 1 argument");
        return RuntimeValue.Boolean(args[0].Type == ValueType.Array);
    }
    
    private static RuntimeValue BuiltInIsObject(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("isObject() expects 1 argument");
        return RuntimeValue.Boolean(args[0].Type == ValueType.Object);
    }
    
    private static RuntimeValue BuiltInTypeOf(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("typeOf() expects 1 argument");
        return RuntimeValue.String(Tier0TypeTags.GetTag(args[0]));
    }

    private static RuntimeValue BuiltInIsTag(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("isTag() expects 2 arguments: (value, tag)");
        if (args[1].Type != ValueType.String)
            throw new Exception("isTag() second argument must be a string tag");
        var actual = Tier0TypeTags.GetTag(args[0]);
        return RuntimeValue.Boolean(Tier0TypeTags.MatchesTag(actual, args[1].AsString()));
    }

    private static RuntimeValue BuiltInValidate(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("validate() expects 2 arguments: (schema, value)");

        var schema = SchemaRegistry.ResolveSchemaArgument(args[0]);
        var value = args[1];
        var result = new JsonObject();

        if (TypedPromptValidator.TryValidateReturnType(value, schema, out var error))
        {
            result.Set("ok", RuntimeValue.Boolean(true));
            result.Set("data", value);
            return RuntimeValue.Object(result);
        }

        result.Set("ok", RuntimeValue.Boolean(false));
        result.Set("error", RuntimeValue.String(error));
        var schemaName = args[0].Type == ValueType.String ? args[0].AsString() : "schema";
        result.Set("agentError", AgentErrorStdLib.SchemaMismatch(schemaName, error));
        return RuntimeValue.Object(result);
    }
}
