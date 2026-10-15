// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;
using MaldaLang.Formatting;
using MaldaLang.LanguageServer;
using MaldaLang.Tests.Planning;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// I3 <c>malda fmt</c>: lossless trivia scan, idempotent formatting, unchanged semantics,
/// comments and string literals preserved, CLI modes and the <c>malda-lsp</c> formatting handler.
/// </summary>
public class MaldaFormatterTests
{
    private static string Fmt(string source)
    {
        var result = MaldaFormatter.Format(source);
        Assert.True(result.Success, result.Error);
        return result.Text;
    }

    private static IEnumerable<string> ExampleFiles() =>
        Directory.EnumerateFiles(Path.Combine(PlanningPaths.RepoRoot, "Examples"), "*.malda", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void TriviaScanner_IsLossless_OnAllExamples()
    {
        var failures = new List<string>();
        foreach (var file in ExampleFiles())
        {
            var source = File.ReadAllText(file);
            var tokens = SourceTriviaScanner.Scan(source);
            if (!string.Equals(string.Concat(tokens.Select(t => t.Text)), source, StringComparison.Ordinal))
                failures.Add(Path.GetRelativePath(PlanningPaths.RepoRoot, file));
        }

        Assert.True(failures.Count == 0, "Scan is not lossless for:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Format_IsIdempotent_OnAllExamples()
    {
        var files = ExampleFiles().ToList();
        Assert.NotEmpty(files);
        var failures = new List<string>();
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var once = MaldaFormatter.Format(source);
            var relative = Path.GetRelativePath(PlanningPaths.RepoRoot, file);
            if (!once.Success)
            {
                failures.Add($"{relative}: {once.Error}");
                continue;
            }

            var twice = MaldaFormatter.Format(once.Text);
            if (!twice.Success || twice.Changed)
                failures.Add($"{relative}: second pass changed the output");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Theory]
    [InlineData("Examples/Basics/hello_world.malda")]
    [InlineData("Examples/Basics/first_look.malda")]
    [InlineData("Examples/Basics/as_variant.malda")]
    [InlineData("Examples/Basics/errors_and_match.malda")]
    [InlineData("Examples/Basics/sumtype_typed_payloads.malda")]
    [InlineData("Examples/Basics/schema_validate.malda")]
    [InlineData("Examples/OOP/calculator.malda")]
    [InlineData("Examples/OOP/classes_objects.malda")]
    [InlineData("Examples/OOP/inheritance.malda")]
    [InlineData("Examples/OOP/static_members.malda")]
    [InlineData("Examples/Graphs/basic_graph_operations.malda")]
    [InlineData("Examples/Graphs/graph_algorithms.malda")]
    [InlineData("Examples/Graphs/graph_pathfinding.malda")]
    [InlineData("Examples/Graphs/minimum_spanning_tree.malda")]
    [InlineData("Examples/Graphs/topological_sort.malda")]
    [InlineData("Examples/Prompts/context_turns.malda")]
    public async Task Format_KeepsInterpreterStdout(string relativePath)
    {
        var path = Path.Combine(PlanningPaths.RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var source = File.ReadAllText(path);
        var formatted = Fmt(source);

        var before = await TestBase.CaptureInterpretOutcomeAsync(source, path);
        var after = await TestBase.CaptureInterpretOutcomeAsync(formatted, path);

        Assert.Equal(0, before.ExitCode);
        Assert.Equal(before.ExitCode, after.ExitCode);
        Assert.Equal(before.StdOut, after.StdOut);
        Assert.False(string.IsNullOrWhiteSpace(before.StdOut));
    }

    [Fact]
    public void Format_NormalizesIndentSpacingAndBlankLines()
    {
        var source = "function f(a,b){\nif(a<b){\n  return a+b;\n}\n\n\n\nreturn a  -  b ;   \n}";
        var expected = "function f(a, b) {\n    if (a < b) {\n        return a + b;\n    }\n\n    return a - b;\n}\n";
        Assert.Equal(expected, Fmt(source));
    }

    [Fact]
    public void Format_PreservesComments()
    {
        var source =
            "// header comment   \n" +
            "var x = 1;   // trailing\n" +
            "function f() {\n" +
            "/* block\n" +
            "   comment */\n" +
            "return x; /* inline */\n" +
            "}\n";
        var formatted = Fmt(source);
        Assert.Contains("// header comment\n", formatted);
        Assert.Contains("var x = 1;   // trailing\n", formatted);
        Assert.Contains("    /* block\n       comment */\n", formatted);
        Assert.Contains("    return x; /* inline */\n", formatted);
    }

    [Fact]
    public void Format_NeverChangesStringLiterals()
    {
        var source =
            "var a = \"  spaced   {x}  \";\n" +
            "var b = 'single  quoted';\n" +
            "var c = $\"sum {a+b}  and {{literal}}\";\n" +
            "var d = \"\"\"\n" +
            "   triple   \n" +
            "\n" +
            "\n" +
            "\n" +
            "      body  \n" +
            "\"\"\";\n" +
            "var e = $\"\"\"\n" +
            "  {d[\"k\"]}   \n" +
            "\"\"\";\n";
        var formatted = Fmt(source);
        Assert.Contains("\"  spaced   {x}  \"", formatted);
        Assert.Contains("'single  quoted'", formatted);
        Assert.Contains("$\"sum {a+b}  and {{literal}}\"", formatted);
        Assert.Contains("\"\"\"\n   triple   \n\n\n\n      body  \n\"\"\"", formatted);
        Assert.Contains("$\"\"\"\n  {d[\"k\"]}   \n\"\"\"", formatted);
    }

    [Fact]
    public void Format_LeavesPromptBodiesUntouched()
    {
        var source =
            "prompt greet(name) {\n" +
            "system: \"\"\"\n" +
            "  You are   terse.\n" +
            "\"\"\"\n" +
            "user: \"Hello,   {name}!\"\n" +
            "}\n";
        var formatted = Fmt(source);
        Assert.Contains("    system: \"\"\"\n  You are   terse.\n\"\"\"\n", formatted);
        Assert.Contains("    user: \"Hello,   {name}!\"\n", formatted);
    }

    [Fact]
    public void Format_IndentsMatchCaseBodiesAndWrappedConditions()
    {
        var source =
            "match x {\n" +
            "case 1:\n" +
            "print(\"one\");\n" +
            "default: print(\"other\");\n" +
            "}\n" +
            "if (a ||\n" +
            "b) {\n" +
            "c();\n" +
            "}\n";
        var expected =
            "match x {\n" +
            "    case 1:\n" +
            "        print(\"one\");\n" +
            "    default: print(\"other\");\n" +
            "}\n" +
            "if (a ||\n" +
            "        b) {\n" +
            "    c();\n" +
            "}\n";
        Assert.Equal(expected, Fmt(source));
    }

    [Fact]
    public void Format_KeepsPackageNamesAndUnaryMinus()
    {
        var formatted = Fmt("import { clamp } from malda-demo-math;\nvar y = -1;\nvar z = f(-x, a-b);\nvar w = obj.timeout-1;\n");
        Assert.Contains("from malda-demo-math;", formatted);
        Assert.Contains("var y = -1;", formatted);
        Assert.Contains("f(-x, a - b)", formatted);
        Assert.Contains("obj.timeout - 1", formatted);
    }

    [Fact]
    public void Format_PreservesCrLfAndBom()
    {
        var source = "\uFEFFvar x=1;\r\nif (x){\r\nprint(x);\r\n}\r\n";
        var formatted = Fmt(source);
        Assert.Equal("\uFEFFvar x = 1;\r\nif (x) {\r\n    print(x);\r\n}\r\n", formatted);
    }

    [Fact]
    public void Format_LexerError_LeavesSourceUnchanged()
    {
        var source = "var x = \"unterminated\n  if(x){}\n";
        var result = MaldaFormatter.Format(source);
        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Contains("Unterminated string", result.Error);
    }

    [Fact]
    public void Format_KeepsSectionMarkersAtColumnZero()
    {
        var formatted = Fmt("function f() {\n// @malda-section Helpers\nreturn 1;\n}\n");
        Assert.Contains("\n// @malda-section Helpers\n", formatted);
    }

    [Fact]
    public void Format_UsesTabsWhenAsked()
    {
        var result = MaldaFormatter.Format("if (x) {\ny();\n}\n", new MaldaFormatOptions { InsertSpaces = false });
        Assert.Equal("if (x) {\n\ty();\n}\n", result.Text);
    }

    [Fact]
    public void RangeEdits_OnlyTouchRequestedLines()
    {
        var source = "if (x) {\ny();\nz();\n}\n";
        var edits = MaldaFormatter.GetRangeEdits(source, 1, 1);
        var edit = Assert.Single(edits);
        Assert.Equal(1, edit.StartLine);
        Assert.Equal("if (x) {\n    y();\nz();\n}\n", MaldaFormatter.ApplyEdits(source, edits));
    }

    [Fact]
    public void DocumentEdits_ApplyToFormattedText()
    {
        var source = "// keep\nvar a=1;\n\n\n\nvar b=2;";
        var edits = MaldaFormatter.GetDocumentEdits(source);
        var edit = Assert.Single(edits);
        Assert.Equal(1, edit.StartLine);
        Assert.Equal(Fmt(source), MaldaFormatter.ApplyEdits(source, edits));
    }

    [Fact]
    public void Cli_Check_ListsUnformattedFilesAndExitsOne()
    {
        var dir = Path.Combine(Path.GetTempPath(), "malda_fmt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var bad = Path.Combine(dir, "bad.malda");
            var good = Path.Combine(dir, "good.malda");
            File.WriteAllText(bad, "var x=1;\n");
            File.WriteAllText(good, "var x = 1;\n");

            var output = new StringWriter();
            var error = new StringWriter();
            var exit = new FmtCommandRunner().Run(new[] { dir, "--check" }, output, error);
            Assert.Equal(FmtCommandRunner.ExitNeedsFormatting, exit);
            Assert.Contains("bad.malda", output.ToString());
            Assert.DoesNotContain("good.malda", output.ToString());
            Assert.Equal("var x=1;\n", File.ReadAllText(bad));

            exit = new FmtCommandRunner().Run(new[] { dir }, new StringWriter(), new StringWriter());
            Assert.Equal(FmtCommandRunner.ExitOk, exit);
            Assert.Equal("var x = 1;\n", File.ReadAllText(bad));

            exit = new FmtCommandRunner().Run(new[] { dir, "--check" }, new StringWriter(), new StringWriter());
            Assert.Equal(FmtCommandRunner.ExitOk, exit);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Cli_Stdin_WritesFormattedSourceToStdout()
    {
        var output = new StringWriter();
        var exit = new FmtCommandRunner().Run(new[] { "--stdin" }, output, new StringWriter(), new StringReader("if(x){\ny();\n}"));
        Assert.Equal(FmtCommandRunner.ExitOk, exit);
        Assert.Equal("if (x) {\n    y();\n}\n", output.ToString());

        var error = new StringWriter();
        exit = new FmtCommandRunner().Run(new[] { "--stdin" }, new StringWriter(), error, new StringReader("var s = \"open\n"));
        Assert.Equal(FmtCommandRunner.ExitNeedsFormatting, exit);
        Assert.Contains("<stdin>", error.ToString());

        exit = new FmtCommandRunner().Run(new[] { "--stdin", "--check" }, new StringWriter(), new StringWriter(), new StringReader("var x = 1;\n"));
        Assert.Equal(FmtCommandRunner.ExitOk, exit);
    }

    [Fact]
    public void Cli_UnknownOption_IsUsageError()
    {
        var exit = new FmtCommandRunner().Run(new[] { "--nope" }, new StringWriter(), new StringWriter());
        Assert.Equal(FmtCommandRunner.ExitUsage, exit);
    }

    [Fact]
    public async Task Lsp_DocumentFormatting_ReturnsFormatterEdit()
    {
        var store = new DocumentStore();
        var uri = new DocumentUri("file", "", "/fmt_test.malda", null, null, null);
        var source = "function foo() {\nreturn 1+2;\n}";
        store.Set(uri, source);
        var handler = new MaldaDocumentFormattingHandler(store);
        var result = await handler.Handle(new DocumentFormattingParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Options = new FormattingOptions { TabSize = 4, InsertSpaces = true }
        }, CancellationToken.None);

        var edit = Assert.Single(result!);
        Assert.Equal(1, edit.Range.Start.Line);
        Assert.Equal("    return 1 + 2;\n}\n", edit.NewText);
    }
}
