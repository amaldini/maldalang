// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

public class AutoFixDiagnosticsTests
{
    [Fact]
    public void GetDiagnostics_MissingCloser_AttachesParserAutofix()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics("print(\"hi\"\n");

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.AutoFix != null &&
            diagnostic.Source == "parser" &&
            !string.IsNullOrEmpty(diagnostic.AutoFix.TextToInsert));
    }

    [Fact]
    public void GetDiagnostics_TypeMismatch_DoesNotAttachParserAutofix()
    {
        var service = new LanguageService();
        var diagnostics = service.GetDiagnostics("var n: int = \"abc\";");

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Source == "malda-types" && diagnostic.AutoFix != null);
    }

    [Fact]
    public void GetDiagnostics_MissingSemicolon_ReportsOnStatementLineNotNextLine()
    {
        const string source = "print(\"hi\")\n// comment\nvar x = 1;\n";
        var diagnostic = GetMissingSemicolon(source);

        Assert.Equal(0, diagnostic.Line);
        Assert.Equal(0, diagnostic.AutoFix!.Line);
        Assert.Equal(";", diagnostic.AutoFix.TextToInsert);
        Assert.Equal("print(\"hi\");\n// comment\nvar x = 1;\n", ApplyAutoFix(source, diagnostic.AutoFix));
    }

    [Fact]
    public void GetDiagnostics_MissingSemicolon_InsertsBeforeTrailingLineComment()
    {
        const string source = "print(\"hi\") // note\nvar x = 1;\n";
        var diagnostic = GetMissingSemicolon(source);

        Assert.Equal(0, diagnostic.Line);
        Assert.Equal("print(\"hi\"); // note\nvar x = 1;\n", ApplyAutoFix(source, diagnostic.AutoFix!));
    }

    [Fact]
    public void GetDiagnostics_MissingSemicolon_InsertsBeforeBlockCommentAndNextStatement()
    {
        const string source = "print(\"hi\")\n/* block */\nvar x = 1;\n";
        var diagnostic = GetMissingSemicolon(source);

        Assert.Equal(0, diagnostic.Line);
        Assert.Equal("print(\"hi\");\n/* block */\nvar x = 1;\n", ApplyAutoFix(source, diagnostic.AutoFix!));
    }

    [Fact]
    public void GetDiagnostics_MissingSemicolon_SameLineInsertsAfterStatement()
    {
        const string source = "print(\"hi\") var x = 1;";
        var diagnostic = GetMissingSemicolon(source);

        Assert.Equal(0, diagnostic.Line);
        Assert.Equal("print(\"hi\"); var x = 1;", ApplyAutoFix(source, diagnostic.AutoFix!));
    }

    [Fact]
    public void GetDiagnostics_MissingSemicolon_AtEndOfFile_InsertsAfterStatement()
    {
        const string source = "print(\"hi\")\n";
        var diagnostic = GetMissingSemicolon(source);

        Assert.Equal(0, diagnostic.Line);
        Assert.Equal("print(\"hi\");\n", ApplyAutoFix(source, diagnostic.AutoFix!));
    }

    [Fact]
    public void Parse_MissingSemicolon_ReportsAtEndOfStatementNotNextToken()
    {
        const string source = "print(\"hi\")\n// comment\nvar x = 1;\n";
        var parser = new MaldaLang.Parser.Parser(new Lexer(source).Tokenize());
        parser.Parse();

        var error = Assert.Single(parser.Errors);
        Assert.Equal(1, error.Line);
        Assert.Contains("';'", error.Details, StringComparison.Ordinal);
    }

    private static Diagnostic GetMissingSemicolon(string source)
    {
        var diagnostics = new LanguageService().GetDiagnostics(source);
        var diagnostic = Assert.Single(diagnostics, item => item.AutoFix?.TextToInsert == ";");
        Assert.NotNull(diagnostic.AutoFix);
        return diagnostic;
    }

    private static string ApplyAutoFix(string source, AutoFixInfo fix)
    {
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Assert.InRange(fix.Line, 0, lines.Length - 1);
        var line = lines[fix.Line];
        var column = Math.Min(Math.Max(0, fix.Column), line.Length);
        var start = column;
        var length = Math.Min(Math.Max(0, fix.LengthToReplace), line.Length - start);
        lines[fix.Line] = line.Remove(start, length).Insert(start, fix.TextToInsert ?? string.Empty);
        return string.Join(newline, lines);
    }
}
