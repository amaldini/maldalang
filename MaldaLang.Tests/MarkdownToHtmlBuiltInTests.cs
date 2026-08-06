// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class MarkdownToHtmlBuiltInTests
{
    [Fact]
    public void MarkdownToHtml_IsRegisteredForInterpreterAndTranspiler()
    {
        Assert.True(BuiltInRegistry.IsInterpreterBuiltIn("markdownToHtml"));
        Assert.True(BuiltInRegistry.IsTranspilerBuiltIn("markdownToHtml"));
        var descriptor = Assert.IsType<BuiltInDescriptor>(BuiltInRegistry.GetDescriptor("markdownToHtml"));
        Assert.True(descriptor.IsAlwaysSynchronousForCodegen);
    }

    [Fact]
    public void MarkdownToHtml_RendersEmphasisAndTables()
    {
        var md = "**bold** and a table:\n\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var result = BuiltInFunctions.CallBuiltIn(
            "markdownToHtml",
            new List<RuntimeValue> { RuntimeValue.String(md) },
            null);
        Assert.Equal(ValueType.String, result.Type);
        var html = result.AsString();
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<td>1</td>", html);
    }

    [Fact]
    public void MarkdownToHtml_DisablesRawHtml()
    {
        var result = BuiltInFunctions.CallBuiltIn(
            "markdownToHtml",
            new List<RuntimeValue> { RuntimeValue.String("Hello <script>alert(1)</script> world") },
            null);
        var html = result.AsString();
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void TranspiledMarkdownToHtml_RendersStrong()
    {
        var source = @"
var html = markdownToHtml(""**hi**"");
print(html);
";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.True(result.ExitCode == 0, "stderr: " + result.StdErr + "\nstdout: " + result.StdOut);
        Assert.Contains("<strong>hi</strong>", result.StdOut);
    }
}
