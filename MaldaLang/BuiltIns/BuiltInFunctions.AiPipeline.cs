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
    private static RuntimeValue BuiltInParseJsonTyped(List<RuntimeValue> args, Interpreter? interpreter) =>
        AiPipelineHelpers.ParseJsonTyped(args, interpreter);

    private static RuntimeValue BuiltInLoadDocuments(List<RuntimeValue> args) =>
        AiPipelineHelpers.LoadDocuments(args);

    private static RuntimeValue BuiltInSplitDocuments(List<RuntimeValue> args) =>
        AiPipelineHelpers.SplitDocuments(args);

    private static RuntimeValue BuiltInFormatRetrievedDocs(List<RuntimeValue> args) =>
        AiPipelineHelpers.FormatRetrievedDocs(args);

    private static RuntimeValue BuiltInWithExamples(List<RuntimeValue> args) =>
        AiPipelineHelpers.WithExamples(args);

    private static RuntimeValue BuiltInIndexInto(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (interpreter == null)
            throw new Exception("indexInto() requires an active interpreter context.");
        return AiPipelineHelpers.IndexInto(args, interpreter);
    }

    private static RuntimeValue BuiltInComposePipe(List<RuntimeValue> args, Interpreter? interpreter) =>
        AiPipelineHelpers.ComposePipe(args, interpreter);

    private static RuntimeValue BuiltInMergeRetrievedDocs(List<RuntimeValue> args) =>
        AiPipelineHelpers.MergeRetrievedDocs(args);
}
