// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class ExtractPdfTextBuiltInTests
{
    [Fact]
    public void ExtractPdfText_IsRegisteredForInterpreterAndTranspiler()
    {
        Assert.True(BuiltInRegistry.IsInterpreterBuiltIn("extractPdfText"));
        Assert.True(BuiltInRegistry.IsTranspilerBuiltIn("extractPdfText"));
        var descriptor = Assert.IsType<BuiltInDescriptor>(BuiltInRegistry.GetDescriptor("extractPdfText"));
        Assert.True(descriptor.IsAlwaysSynchronousForCodegen);
    }

    [Fact]
    public void ExtractPdfText_ReadsDigitalTextLayer()
    {
        var path = WriteSamplePdf("Hello PdfPig text");
        try
        {
            var result = BuiltInFunctions.CallBuiltIn(
                "extractPdfText",
                new List<RuntimeValue> { RuntimeValue.String(path) },
                null);
            Assert.Equal(ValueType.String, result.Type);
            Assert.Contains("Hello PdfPig text", result.AsString());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void PdfExtractText_NamespaceResolves()
    {
        var path = WriteSamplePdf("Namespaced PDF extract");
        try
        {
            var env = new MaldaLang.Interpreter.Environment();
            BuiltInFunctions.RegisterBuiltIns(env);
            var pdf = env.Get("pdf");
            Assert.Equal(ValueType.Object, pdf.Type);
            var pdfInstance = Assert.IsType<PdfInstance>(pdf.AsObject());
            var result = pdfInstance.CallMethod(
                "extractText",
                new List<RuntimeValue> { RuntimeValue.String(path) },
                null!);
            Assert.Contains("Namespaced PDF extract", result.AsString());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ExtractPdfText_MissingFileThrows()
    {
        var missing = Path.Combine(Path.GetTempPath(), "malda-missing-" + Guid.NewGuid().ToString("N") + ".pdf");
        var ex = Assert.ThrowsAny<Exception>(() =>
            BuiltInFunctions.CallBuiltIn(
                "extractPdfText",
                new List<RuntimeValue> { RuntimeValue.String(missing) },
                null));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TranspiledExtractPdfText_ReadsText()
    {
        var path = WriteSamplePdf("Transpile PDF ok");
        var escaped = path.Replace("\\", "\\\\");
        try
        {
            var source = $@"
var text = pdf.extractText(""{escaped}"");
print(text);
";
            var result = TranspiledTestRunner.CompileAndRunFromSource(source);
            Assert.True(result.ExitCode == 0, "stderr: " + result.StdErr + "\nstdout: " + result.StdOut);
            Assert.Contains("Transpile PDF ok", result.StdOut);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static string WriteSamplePdf(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), "malda-pdf-" + Guid.NewGuid().ToString("N") + ".pdf");
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(text, 12, new PdfPoint(50, 750), font);
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
