// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using MaldaLang.Cli;
using Xunit;

namespace MaldaLang.Tests;

public class ReplSessionPatchTests
{
    [Theory]
    [InlineData("editline add 2 return a - b;", "add", 2, "return a - b;", false)]
    [InlineData("EDITLINE add(a, b) 2 return a - b;", "add", 2, "return a - b;", false)]
    [InlineData("editline add 2", "add", 2, null, true)]
    [InlineData("editline <lambda> 1", "<lambda>", 1, null, true)]
    public void TryParseCommand_ReadsEditLine(
        string line, string name, int lineNumber, string? text, bool needsBody)
    {
        Assert.True(ReplSessionPatch.TryParseCommand(line, out var command));
        Assert.False(command.IsUsage);
        Assert.Equal(ReplSessionPatch.EditLineAction, command.Action);
        Assert.Equal(name, command.Name);
        Assert.Equal(lineNumber, command.LineNumber);
        Assert.Equal(text, command.InlineText);
        Assert.Equal(needsBody, command.NeedsBody);
    }

    [Fact]
    public void TryParseCommand_ReadsInsert()
    {
        Assert.True(ReplSessionPatch.TryParseCommand("insert add after 2", out var command));
        Assert.False(command.IsUsage);
        Assert.Equal(ReplSessionPatch.InsertAction, command.Action);
        Assert.Equal("add", command.Name);
        Assert.Equal(2, command.LineNumber);
        Assert.True(command.NeedsBody);
    }

    [Theory]
    [InlineData("editline", "")]
    [InlineData("editline add", "add")]
    [InlineData("insert add", "add")]
    [InlineData("insert add before 2", "add")]
    public void TryParseCommand_MissingLine_IsUsageAndKeepsTheName(string line, string name)
    {
        Assert.True(ReplSessionPatch.TryParseCommand(line, out var command));
        Assert.True(command.IsUsage);
        Assert.Equal(name, command.Name ?? "");
        Assert.False(string.IsNullOrWhiteSpace(command.Usage));
    }

    [Fact]
    public void WriteListing_PrintsNumberedSource()
    {
        var session = new ReplSessionSource();
        session.Record("function add(a, b) {\n    return a + b;\n}");
        var writer = new StringWriter();

        ReplSessionPatch.WriteListing(writer, ReplSessionPatch.EditLineUsage, "add", null, session);

        var text = writer.ToString().Replace("\r", "");
        Assert.Contains("1| function add(a, b) {", text);
        Assert.Contains("2|     return a + b;", text);
        Assert.Contains("3| }", text);
        Assert.DoesNotContain("Usage:", text);
    }

    [Theory]
    [InlineData("insert = 1")]
    [InlineData("edit add")]
    [InlineData("editline = 1")]
    [InlineData("print(1)")]
    public void TryParseCommand_LeavesOrdinaryCodeAlone(string line)
    {
        Assert.False(ReplSessionPatch.TryParseCommand(line, out _));
    }

    [Fact]
    public void EditLine_IsNotTheReplaceAlias()
    {
        Assert.False(ReplSessionEdit.TryParseCommand("editline add 2", out _, out _));
    }

    [Fact]
    public void TrySplice_ReplacesOneLineWithSeveral()
    {
        var source = "function add(a, b) {\n    return a + b;\n}";

        Assert.True(ReplSessionPatch.TrySplice(
            source,
            ReplSessionPatch.EditLineAction,
            2,
            "    if (a < 0) return 0;\n    return a - b;",
            out var patched,
            out var error));

        Assert.Null(error);
        Assert.Equal(
            "function add(a, b) {\n    if (a < 0) return 0;\n    return a - b;\n}",
            patched);
    }

    [Fact]
    public void TrySplice_BlankReplacementDeletesTheLine()
    {
        var source = "function add(a, b) {\n    return a + b;\n}";

        Assert.True(ReplSessionPatch.TrySplice(
            source, ReplSessionPatch.EditLineAction, 2, "", out var patched, out _));

        Assert.Equal("function add(a, b) {\n}", patched);
    }

    [Fact]
    public void TrySplice_InsertsAfterTheLine()
    {
        var source = "function add(a, b) {\n    return a + b;\n}";

        Assert.True(ReplSessionPatch.TrySplice(
            source,
            ReplSessionPatch.InsertAction,
            1,
            "    if (a < 0) return 0;",
            out var patched,
            out _));

        Assert.Equal(
            "function add(a, b) {\n    if (a < 0) return 0;\n    return a + b;\n}",
            patched);
    }

    [Fact]
    public void TrySplice_OutOfRange_ReportsTheLineCount()
    {
        Assert.False(ReplSessionPatch.TrySplice(
            "function add(a, b) a + b;",
            ReplSessionPatch.EditLineAction,
            4,
            "return 1;",
            out _,
            out var error));

        Assert.Contains("Line 4 is outside the definition (1 line).", error);
    }

    [Fact]
    public void TrySplice_InsertIntoCompactFunction_WrapsTheBody()
    {
        Assert.True(ReplSessionPatch.TrySplice(
            "function add(a, b) a + b;",
            ReplSessionPatch.InsertAction,
            1,
            "if (a < 0) return 0;",
            out var patched,
            out var error));

        Assert.Null(error);
        Assert.Equal(
            "function add(a, b) {\n    if (a < 0) return 0;\n    return a + b;\n}",
            patched);
    }

    [Fact]
    public void TrySplice_InsertIntoCompactFunction_KeepsTheReturnType()
    {
        Assert.True(ReplSessionPatch.TrySplice(
            "function add(a, b) -> number a + b;",
            ReplSessionPatch.InsertAction,
            1,
            "    if (a < 0) return 0;",
            out var patched,
            out _));

        Assert.Equal(
            "function add(a, b) -> number {\n    if (a < 0) return 0;\n    return a + b;\n}",
            patched);
    }
}
