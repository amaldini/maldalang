// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MaldaLang.Compiler;
using MaldaLang.IDE.Services;
using MaldaLang.Interpreter;
using MaldaLang.LanguageServer;
using MaldaLang.Linting;
using Xunit;
using InterpreterRuntime = MaldaLang.Interpreter.Interpreter;
using ParserRuntime = MaldaLang.Parser.Parser;

namespace MaldaLang.Tests;

/// <summary>
/// Diagnostics raised on code spliced in by <c>include</c> name the included file and use its
/// lines (they were reported against the including file at the included file's line/column).
/// </summary>
[Collection("Sequential")]
public class IncludeDiagnosticsLocationTests : TestBase
{
    // Line 3 of the included file has the syntax error; line 3 of main is a comment.
    private const string MainSource = "// main\n// line 2\n// line 3 is a comment\ninclude \"sub/lib.malda\";\nio.print(\"hi\");\n";
    private const string BrokenLib = "// lib\nfunction f() {\n  var x = ;\n}\n";

    [Fact]
    public void ParseErrorInInclude_IsReportedAtTheIncludedFile()
    {
        WithFiles(dir =>
        {
            var main = Write(dir, "main.malda", MainSource);
            var lib = Write(dir, "sub/lib.malda", BrokenLib);

            var diagnostics = new LanguageService().GetDiagnostics(File.ReadAllText(main), main);

            var parse = Assert.Single(diagnostics, d => d.Source == "parser");
            Assert.True(SameFile(lib, parse.SourceFile), $"expected {lib}, got {parse.SourceFile}");
            Assert.Equal(2, parse.Line); // 0-based: line 3
            Assert.Equal(10, parse.Column);
            Assert.Null(parse.AutoFix);
        });
    }

    [Fact]
    public void ParseErrorInNestedInclude_NamesTheInnermostFile()
    {
        WithFiles(dir =>
        {
            var main = Write(dir, "main.malda", "include \"a.malda\";\nio.print(1);\n");
            Write(dir, "a.malda", "// a\ninclude \"nested/b.malda\";\n");
            var b = Write(dir, "nested/b.malda", "// b\n\n\n\nvar y = (1 + ;\n");

            var diagnostics = new LanguageService().GetDiagnostics(File.ReadAllText(main), main);

            var parse = Assert.Single(diagnostics, d => d.Source == "parser");
            Assert.True(SameFile(b, parse.SourceFile), $"expected {b}, got {parse.SourceFile}");
            Assert.Equal(4, parse.Line);
        });
    }

    [Fact]
    public void ParseErrorInMainFile_HasNoSourceFile()
    {
        WithFiles(dir =>
        {
            var main = Write(dir, "main.malda", "include \"lib.malda\";\nvar z = ;\n");
            Write(dir, "lib.malda", "var ok = 1;\n");

            var diagnostics = new LanguageService().GetDiagnostics(File.ReadAllText(main), main);

            var parse = Assert.Single(diagnostics, d => d.Source == "parser");
            Assert.Null(parse.SourceFile);
            Assert.Equal(1, parse.Line);
        });
    }

    [Fact]
    public void SemanticDiagnosticInInclude_NamesTheIncludedFile()
    {
        WithFiles(dir =>
        {
            var main = Write(dir, "main.malda", "include \"lib.malda\";\nprint(g());\n");
            var lib = Write(dir, "lib.malda", "// lib\n\n\nfunction g() {\n  print(\"a\");\n  return 1;\n}\n");

            var diagnostics = new LanguageService().GetDiagnostics(File.ReadAllText(main), main);
            var style = diagnostics.Where(d => d.Source == "malda-style").ToList();

            Assert.Contains(style, d => d.SourceFile == null && d.Line == 1);
            Assert.Contains(style, d => SameFile(lib, d.SourceFile) && d.Line == 4 && d.Column == 2);
        });
    }

    [Fact]
    public void CheckJson_FileFieldNamesTheIncludedFile()
    {
        WithFiles(dir =>
        {
            var main = Write(dir, "main.malda", MainSource);
            var lib = Write(dir, "sub/lib.malda", BrokenLib);

            var output = new StringWriter();
            var code = new CheckCommandRunner().Run(new[] { main, "--json" }, output, new StringWriter());

            Assert.Equal(CheckCommandRunner.ExitHasErrors, code);
            using var doc = JsonDocument.Parse(output.ToString());
            Assert.Equal(main, doc.RootElement.GetProperty("file").GetString());
            var parse = doc.RootElement.GetProperty("diagnostics").EnumerateArray()
                .Single(d => d.GetProperty("code").GetString() == "parser");
            Assert.True(SameFile(lib, parse.GetProperty("file").GetString()));
            Assert.Equal(3, parse.GetProperty("line").GetInt32());
            Assert.Equal(11, parse.GetProperty("column").GetInt32());
        });
    }

    [Fact]
    public void CheckHuman_PrefixesTheIncludedFile()
    {
        WithFiles(dir =>
        {
            var main = Write(dir, "main.malda", MainSource);
            var lib = Write(dir, "sub/lib.malda", BrokenLib);

            var output = new StringWriter();
            new CheckCommandRunner().Run(new[] { main }, output, new StringWriter());

            var line = output.ToString().Split('\n').Single(l => l.Contains("error[parser]", StringComparison.Ordinal));
            Assert.Contains("lib.malda:3:11:", line, StringComparison.Ordinal);
            Assert.DoesNotContain("main.malda", line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Lint_FindingInInclude_NamesTheIncludedFile()
    {
        WithFiles(dir =>
        {
            var main = Write(dir, "main.malda", "include \"lib.malda\";\nio.print(h(5));\n");
            var lib = Write(dir, "lib.malda", "// lib\n\n\nfunction h(n) {\n  return str.repeat(\"-\", n / 2);\n}\n");

            var result = MaldaLinter.Lint(File.ReadAllText(main), main);
            var finding = Assert.Single(result.Findings);
            Assert.Equal(LintRules.RepeatFractionalCount, finding.Code);
            Assert.True(SameFile(lib, finding.File));
            Assert.Equal(5, finding.Line);

            // Linting both files reports the finding once, at the included file.
            var report = LintCommandRunner.Analyze(new[] { (main, File.ReadAllText(main)), (lib, File.ReadAllText(lib)) });
            var dto = Assert.Single(report.Diagnostics);
            Assert.True(SameFile(lib, dto.File));

            // The LSP does not publish it on the main document's lines.
            Assert.Empty(MaldaTextDocumentSyncHandler.LintDiagnostics(File.ReadAllText(main), main));
        });
    }

    [Fact]
    public void Lint_SuppressionInIncludedFile_Applies()
    {
        WithFiles(dir =>
        {
            var main = Write(dir, "main.malda", "include \"lib.malda\";\nio.print(h(5));\n");
            Write(dir, "lib.malda", "function h(n) {\n  // malda-lint-disable-next-line ML0011\n  return str.repeat(\"-\", n / 2);\n}\n");

            var result = MaldaLinter.Lint(File.ReadAllText(main), main);
            Assert.Empty(result.Findings);
            Assert.Equal(1, result.SuppressedCount);
        });
    }

    [Fact]
    public void Lint_ParseErrorInInclude_NamesTheIncludedFile()
    {
        WithFiles(dir =>
        {
            var main = Write(dir, "main.malda", MainSource);
            var lib = Write(dir, "sub/lib.malda", BrokenLib);

            var finding = Assert.Single(MaldaLinter.Lint(File.ReadAllText(main), main).Findings);
            Assert.Equal(LintRules.ParseFailed, finding.Code);
            Assert.True(SameFile(lib, finding.File));
            Assert.Equal(3, finding.Line);
        });
    }

    [Fact]
    public void Lsp_DropsDiagnosticsFromIncludedFiles()
    {
        WithFiles(dir =>
        {
            var main = Write(dir, "main.malda", MainSource);
            Write(dir, "sub/lib.malda", BrokenLib);

            var diagnostics = new LanguageService().GetDiagnostics(File.ReadAllText(main), main);
            Assert.Contains(diagnostics, d => d.SourceFile != null);
            Assert.All(MaldaTextDocumentSyncHandler.InDocument(diagnostics), d => Assert.Null(d.SourceFile));
            Assert.DoesNotContain(MaldaTextDocumentSyncHandler.InDocument(diagnostics), d => d.Source == "parser");
        });
    }

    [Fact]
    public async Task RuntimeErrorInInclude_ReportsTheIncludedFileAndItsSourceLine()
    {
        var dir = CreateTempDirectory("include_diag_rt_");
        try
        {
            var main = Write(dir, "main.malda", "// main\ninclude \"rt.malda\";\nvar q = 1;\nvar r = 2;\nboom(0);\n");
            var rt = Write(dir, "rt.malda", "// rt\n\nfunction boom(a) {\n  return zz + 1;\n}\n");
            var source = File.ReadAllText(main);
            var statements = new ParserRuntime(new Lexer(source, main).Tokenize(), main).Parse();

            var interpreter = new InterpreterRuntime(currentFile: main);
            interpreter.SetSourceCode(source);
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => interpreter.InterpretAsync(statements));

            var info = RuntimeDiagnostics.CreateDiagnosticInfo(ex, interpreter);
            Assert.True(SameFile(rt, info.File), $"expected {rt}, got {info.File}");
            Assert.Equal(4, info.Line);
            // Line 4 of main is "var r = 2;": the source line must come from the included file.
            Assert.Equal("  return zz + 1;", info.SourceLine);
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void CSharpTranspile_LineDirectivesNameTheIncludedFile()
    {
        WithFiles(dir =>
        {
            var main = Write(dir, "main.malda", "include \"lib.malda\";\nio.print(g());\n");
            var lib = Write(dir, "lib.malda", "// lib\n\nfunction g() {\n  return 1;\n}\n");
            var source = File.ReadAllText(main);
            var statements = new ParserRuntime(new Lexer(source, main).Tokenize(), main).Parse();

            var csharp = new CSharpTranspiler().Transpile(statements, isLibrary: false, sourceFilePath: main);

            Assert.Contains($"#line 3 \"{Path.GetFullPath(lib)}\"", csharp, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void WithFiles(Action<string> body)
    {
        var dir = CreateTempDirectory("include_diag_");
        try
        {
            body(dir);
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    private static string Write(string dir, string relative, string text)
    {
        var path = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static bool SameFile(string expected, string? actual) =>
        actual != null &&
        string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase);
}
