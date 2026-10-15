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
    private static readonly MarkdownPipeline MarkdownToHtmlPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static RuntimeValue BuiltInMarkdownToHtml(List<RuntimeValue> args)
    {
        BuiltInArity.Require("markdownToHtml", args, 1, 1, "markdown");
        if (args[0].Type != ValueType.String)
            throw new Exception("markdownToHtml() expects 1 string argument: (markdown)");

        var markdown = args[0].AsString();
        if (string.IsNullOrEmpty(markdown))
            return RuntimeValue.String("");

        var html = Markdown.ToHtml(markdown, MarkdownToHtmlPipeline);
        return RuntimeValue.String(html);
    }

    /// <summary>
    /// Extract text from a PDF via PdfPig (digital text layer only; no OCR).
    /// Prefer <c>pdf.extractText(path, password?)</c>; flat <c>extractPdfText</c> is the CallBuiltIn name.
    /// </summary>
    private static RuntimeValue BuiltInExtractPdfText(List<RuntimeValue> args)
    {
        BuiltInArity.Require("extractPdfText", args, 1, 2, "path, password?");
        if (args[0].Type != ValueType.String)
            throw new Exception("extractPdfText() expects (string path, string password?)");

        var path = args[0].AsString();
        if (string.IsNullOrWhiteSpace(path))
            throw new Exception("extractPdfText() path cannot be empty");

        string? password = null;
        if (args.Count > 1 && args[1].Type != ValueType.Null)
        {
            if (args[1].Type != ValueType.String)
                throw new Exception("extractPdfText() password must be a string when provided");
            password = args[1].AsString();
        }

        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            throw new Exception($"extractPdfText() invalid path: {ex.Message}");
        }

        if (!File.Exists(path))
            throw new Exception($"extractPdfText() file not found: {path}");

        try
        {
            var options = string.IsNullOrEmpty(password)
                ? new ParsingOptions()
                : new ParsingOptions { Password = password };

            using var document = PdfDocument.Open(path, options);
            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                var pageText = ContentOrderTextExtractor.GetText(page) ?? "";
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(pageText);
            }

            return RuntimeValue.String(sb.ToString());
        }
        catch (Exception ex)
        {
            throw new Exception($"extractPdfText() failed for '{path}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extract text from a Word .docx via Open XML SDK (body paragraphs; not legacy .doc).
    /// Prefer <c>doc.extractText(path)</c>; flat <c>extractDocxText</c> is the CallBuiltIn name.
    /// </summary>
    private static RuntimeValue BuiltInExtractDocxText(List<RuntimeValue> args)
    {
        BuiltInArity.Require("extractDocxText", args, 1, 1, "path");
        if (args[0].Type != ValueType.String)
            throw new Exception("extractDocxText() expects 1 string argument: (path)");

        var path = args[0].AsString();
        if (string.IsNullOrWhiteSpace(path))
            throw new Exception("extractDocxText() path cannot be empty");

        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            throw new Exception($"extractDocxText() invalid path: {ex.Message}");
        }

        if (!File.Exists(path))
            throw new Exception($"extractDocxText() file not found: {path}");

        var ext = Path.GetExtension(path);
        if (!ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            throw new Exception("extractDocxText() expects a .docx file (legacy .doc is not supported)");

        try
        {
            using var document = WordprocessingDocument.Open(path, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body == null)
                return RuntimeValue.String("");

            var sb = new StringBuilder();
            // Fully qualify: DocumentFormat.OpenXml.Wordprocessing.* clashes with Spectre.Console.
            foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
            {
                var line = paragraph.InnerText ?? "";
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(line);
            }

            return RuntimeValue.String(sb.ToString());
        }
        catch (Exception ex)
        {
            throw new Exception($"extractDocxText() failed for '{path}': {ex.Message}", ex);
        }
    }

    private static RuntimeValue BuiltInExtractHTML(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("extractHTML() expects 1 string argument");

        var markdown = args[0].AsString();

        // Extract HTML from markdown code blocks
        var htmlPattern = @"```html\s*(.*?)\s*```";
        var match = System.Text.RegularExpressions.Regex.Match(
            markdown,
            htmlPattern,
            System.Text.RegularExpressions.RegexOptions.Singleline |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        if (match.Success)
            return RuntimeValue.String(match.Groups[1].Value.Trim());

        // If no code block, check if it's already HTML
        if (markdown.Contains("<html") || markdown.Contains("<!DOCTYPE"))
            return RuntimeValue.String(markdown);
        
        // Return as-is if no HTML found
        return RuntimeValue.String(markdown);
    }
}
