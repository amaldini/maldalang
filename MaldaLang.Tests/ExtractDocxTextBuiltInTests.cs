// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class ExtractDocxTextBuiltInTests
{
    [Fact]
    public void ExtractDocxText_IsRegisteredForInterpreterAndTranspiler()
    {
        Assert.True(BuiltInRegistry.IsInterpreterBuiltIn("extractDocxText"));
        Assert.True(BuiltInRegistry.IsTranspilerBuiltIn("extractDocxText"));
        var descriptor = Assert.IsType<BuiltInDescriptor>(BuiltInRegistry.GetDescriptor("extractDocxText"));
        Assert.True(descriptor.IsAlwaysSynchronousForCodegen);
    }

    [Fact]
    public void ExtractDocxText_ReadsBodyParagraphs()
    {
        var path = WriteSampleDocx("Hello OpenXml paragraph");
        try
        {
            var result = BuiltInFunctions.CallBuiltIn(
                "extractDocxText",
                new List<RuntimeValue> { RuntimeValue.String(path) },
                null);
            Assert.Equal(ValueType.String, result.Type);
            Assert.Contains("Hello OpenXml paragraph", result.AsString());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void DocExtractText_NamespaceResolves()
    {
        var path = WriteSampleDocx("Namespaced DOCX extract");
        try
        {
            var env = new MaldaLang.Interpreter.Environment();
            BuiltInFunctions.RegisterBuiltIns(env);
            var doc = env.Get("doc");
            Assert.Equal(ValueType.Object, doc.Type);
            var docInstance = Assert.IsType<DocInstance>(doc.AsObject());
            var result = docInstance.CallMethod(
                "extractText",
                new List<RuntimeValue> { RuntimeValue.String(path) },
                null!);
            Assert.Contains("Namespaced DOCX extract", result.AsString());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ExtractDocxText_RejectsLegacyDocExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), "malda-doc-" + Guid.NewGuid().ToString("N") + ".doc");
        File.WriteAllText(path, "not a real doc");
        try
        {
            var ex = Assert.ThrowsAny<Exception>(() =>
                BuiltInFunctions.CallBuiltIn(
                    "extractDocxText",
                    new List<RuntimeValue> { RuntimeValue.String(path) },
                    null));
            Assert.Contains(".docx", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void TranspiledExtractDocxText_ReadsText()
    {
        var path = WriteSampleDocx("Transpile DOCX ok");
        var escaped = path.Replace("\\", "\\\\");
        try
        {
            var source = $@"
var text = doc.extractText(""{escaped}"");
print(text);
";
            var result = TranspiledTestRunner.CompileAndRunFromSource(source);
            Assert.True(result.ExitCode == 0, "stderr: " + result.StdErr + "\nstdout: " + result.StdOut);
            Assert.Contains("Transpile DOCX ok", result.StdOut);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static string WriteSampleDocx(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), "malda-docx-" + Guid.NewGuid().ToString("N") + ".docx");
        using (var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(
                new Body(
                    new Paragraph(
                        new Run(
                            new Text(text)))));
            mainPart.Document.Save();
        }
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
