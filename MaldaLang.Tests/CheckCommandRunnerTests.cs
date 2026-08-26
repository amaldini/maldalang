// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using System.Text.Json;
using MaldaLang;
using MaldaLang.IDE;
using Xunit;

namespace MaldaLang.Tests;

public class CheckCommandRunnerTests : TestBase
{
    [Fact]
    public void Help_ReturnsZeroAndDoesNotExecute()
    {
        var runner = new CheckCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = runner.Run(new[] { "--help" }, output, error);
        Assert.Equal(CheckCommandRunner.ExitOk, code);
        Assert.Contains("malda check", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Eval_CleanSnippet_JsonOkAndDidNotExecute()
    {
        var runner = new CheckCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = runner.Run(new[] { "-e", "io.print(\"ran\");", "--json" }, output, error);
        Assert.Equal(CheckCommandRunner.ExitOk, code);
        Assert.Equal("", error.ToString());
        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.False(root.GetProperty("executed").GetBoolean());
        Assert.Equal(0, root.GetProperty("errorCount").GetInt32());
        Assert.Equal("<eval>", root.GetProperty("file").GetString());
        Assert.Equal(0, root.GetProperty("diagnostics").GetArrayLength());
        Assert.DoesNotContain("ran", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Eval_FlatPrintAlias_IsWarningStillOk()
    {
        var runner = new CheckCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = runner.Run(new[] { "-e", "print(1);", "--json" }, output, error);
        Assert.Equal(CheckCommandRunner.ExitOk, code);
        using var doc = JsonDocument.Parse(output.ToString());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(
            doc.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == "malda-style" &&
                 d.GetProperty("severity").GetString() == "warning");
    }

    [Fact]
    public void Eval_ParserError_JsonHasOneBasedLineAndExitOne()
    {
        var runner = new CheckCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = runner.Run(new[] { "--json", "-e", "function (" }, output, error);
        Assert.Equal(CheckCommandRunner.ExitHasErrors, code);
        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.False(root.GetProperty("executed").GetBoolean());
        Assert.True(root.GetProperty("errorCount").GetInt32() >= 1);
        var first = root.GetProperty("diagnostics")[0];
        Assert.Equal("error", first.GetProperty("severity").GetString());
        Assert.True(first.GetProperty("line").GetInt32() >= 1);
        Assert.True(first.GetProperty("column").GetInt32() >= 1);
    }

    [Fact]
    public void Eval_UnknownSchemaType_ReportsLineOne()
    {
        var runner = new CheckCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = runner.Run(new[] { "--json", "-e", "schema P { n: NotAType; }" }, output, error);
        Assert.Equal(CheckCommandRunner.ExitHasErrors, code);
        using var doc = JsonDocument.Parse(output.ToString());
        var schema = Assert.Single(
            doc.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == "malda-schema");
        Assert.Equal(1, schema.GetProperty("line").GetInt32());
        Assert.True(schema.GetProperty("column").GetInt32() >= 1);
    }

    [Fact]
    public void File_UnknownSchemaField_IsMaldaSchemaErrorWithoutWriting()
    {
        var dir = CreateTempDirectory("malda_check_schema_");
        try
        {
            var path = Path.Combine(dir, "bad.malda");
            var probe = Path.Combine(dir, "should-not-exist.txt");
            File.WriteAllText(path, """
                schema Person { name: NotAType; }
                io.writeFile("should-not-exist.txt", "nope");
                """);

            var runner = new CheckCommandRunner();
            var output = new StringWriter();
            var error = new StringWriter();
            var code = runner.Run(new[] { path, "--json" }, output, error);

            Assert.Equal(CheckCommandRunner.ExitHasErrors, code);
            Assert.False(File.Exists(probe));
            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;
            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.False(root.GetProperty("executed").GetBoolean());
            var codes = root.GetProperty("diagnostics")
                .EnumerateArray()
                .Select(d => d.GetProperty("code").GetString())
                .ToList();
            Assert.Contains("malda-schema", codes);
            Assert.Contains(
                root.GetProperty("diagnostics").EnumerateArray(),
                d => d.GetProperty("message").GetString()!.Contains("NotAType", StringComparison.Ordinal));
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void File_InterpolationWarning_ExitZero()
    {
        var dir = CreateTempDirectory("malda_check_interp_");
        try
        {
            var path = Path.Combine(dir, "interp.malda");
            File.WriteAllText(path, "var n = 1;\nprint(\"n is {n}\");\n");

            var runner = new CheckCommandRunner();
            var output = new StringWriter();
            var error = new StringWriter();
            var code = runner.Run(new[] { "--json", path }, output, error);

            Assert.Equal(CheckCommandRunner.ExitOk, code);
            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.True(root.GetProperty("warningCount").GetInt32() >= 1);
            Assert.Contains(
                root.GetProperty("diagnostics").EnumerateArray(),
                d => d.GetProperty("code").GetString() == "malda-interp" &&
                     d.GetProperty("severity").GetString() == "warning" &&
                     d.GetProperty("line").GetInt32() == 2);
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Human_CleanFile_PrintsOkLine()
    {
        var dir = CreateTempDirectory("malda_check_human_");
        try
        {
            var path = Path.Combine(dir, "ok.malda");
            File.WriteAllText(path, "io.print(1);\n");
            var runner = new CheckCommandRunner();
            var output = new StringWriter();
            var error = new StringWriter();
            var code = runner.Run(new[] { path }, output, error);
            Assert.Equal(CheckCommandRunner.ExitOk, code);
            Assert.Contains(": ok (0 diagnostics)", output.ToString(), StringComparison.Ordinal);
            Assert.Equal("", error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void MissingFile_JsonUsageExitTwo()
    {
        var runner = new CheckCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var missing = Path.Combine(Path.GetTempPath(), "malda-check-missing-" + Guid.NewGuid().ToString("N") + ".malda");
        var code = runner.Run(new[] { missing, "--json" }, output, error);
        Assert.Equal(CheckCommandRunner.ExitUsage, code);
        using var doc = JsonDocument.Parse(output.ToString());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("not found", doc.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(doc.RootElement.GetProperty("executed").GetBoolean());
    }

    [Fact]
    public void Stdin_TypeMismatch_IsErrorByDefault()
    {
        var runner = new CheckCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var stdin = new StringReader("var n: int = \"abc\";\n");
        var code = runner.Run(new[] { "--stdin", "--json" }, output, error, stdin);
        Assert.Equal(CheckCommandRunner.ExitHasErrors, code);
        using var doc = JsonDocument.Parse(output.ToString());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("<stdin>", doc.RootElement.GetProperty("file").GetString());
        Assert.Contains(
            doc.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("severity").GetString() == "error");
    }

    [Fact]
    public void LenientTypes_TypeMismatch_IsWarningAndOk()
    {
        var runner = new CheckCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = runner.Run(
            new[] { "-e", "var n: int = \"abc\";", "--json", "--lenient-types" },
            output,
            error);
        Assert.Equal(CheckCommandRunner.ExitOk, code);
        using var doc = JsonDocument.Parse(output.ToString());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(
            doc.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("severity").GetString() == "warning");
    }

    [Fact]
    public void NoInput_JsonUsageExitTwo()
    {
        var runner = new CheckCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = runner.Run(new[] { "--json" }, output, error);
        Assert.Equal(CheckCommandRunner.ExitUsage, code);
        using var doc = JsonDocument.Parse(output.ToString());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public void Eval_GatherWithoutReturnType_ReportsLineTwo()
    {
        var runner = new CheckCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var source = """
            prompt research(q) {
                gather: ["read_file"];
                user: q
            }
            """;
        var code = runner.Run(new[] { "--json", "-e", source }, output, error);
        Assert.Equal(CheckCommandRunner.ExitHasErrors, code);
        using var doc = JsonDocument.Parse(output.ToString());
        var prompt = Assert.Single(
            doc.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == "malda-prompt");
        Assert.Equal(2, prompt.GetProperty("line").GetInt32());
        Assert.True(prompt.GetProperty("column").GetInt32() >= 1);
    }

    [Fact]
    public void Eval_StrictTypesNonExhaustiveMatch_ReportsMatchLine()
    {
        var runner = new CheckCommandRunner();
        var output = new StringWriter();
        var error = new StringWriter();
        var source = """
            type Result = Ok(value) | Err(message);
            var r = Ok(1);
            var out = match r {
                case Ok(v): v;
            };
            """;
        var code = runner.Run(new[] { "--json", "--strict-types", "-e", source }, output, error);
        Assert.Equal(CheckCommandRunner.ExitHasErrors, code);
        using var doc = JsonDocument.Parse(output.ToString());
        var match = Assert.Single(
            doc.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == "malda-match");
        Assert.Equal(3, match.GetProperty("line").GetInt32());
        Assert.True(match.GetProperty("column").GetInt32() >= 1);
        Assert.False(doc.RootElement.GetProperty("executed").GetBoolean());
    }

    [Fact]
    public void Analyze_DoesNotRunWriteFile()
    {
        var dir = CreateTempDirectory("malda_check_side_");
        try
        {
            var probe = Path.Combine(dir, "probe.txt");
            var source = $"io.writeFile({JsonSerializer.Serialize(probe)}, \"x\");";
            var report = new CheckCommandRunner().Analyze(source, "<eval>", StrictTypesOptions.Default);
            Assert.True(report.Ok);
            Assert.False(report.Executed);
            Assert.False(File.Exists(probe));
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }
}
