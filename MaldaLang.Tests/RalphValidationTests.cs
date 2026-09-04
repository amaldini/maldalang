// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class RalphValidationTests : TestBase
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string ValidationModules(string workDir)
    {
        var root = RepoRoot().Replace("\\", "/");
        return $@"
include ""{root}/Examples/RalphWiggum/ralph/00-env.malda"";
include ""{root}/Examples/RalphWiggum/ralph/03-validation.malda"";
var workDir = ""{workDir.Replace("\\", "/")}"";
";
    }

    [Fact]
    public void ValidateWorkdir_RejectsUnbalancedHtml()
    {
        var tempDir = CreateTempDirectory("ralph_val_");
        try
        {
            var source = ValidationModules(tempDir) + @"
writeFile(pathJoin(workDir, ""bad.html""), ""<html><head></head><body><motion></html>"");
var r = validateWorkdirFile(workDir, pathJoin(workDir, ""bad.html""));
print(string(r.ok));
";
            var output = RunProgram(source);
            Assert.Contains("false", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdir_AcceptsValidJson()
    {
        var tempDir = CreateTempDirectory("ralph_val_");
        try
        {
            var source = ValidationModules(tempDir) + @"
writeFile(pathJoin(workDir, ""good.json""), ""{\""a\"":1}"");
var r = validateWorkdirFile(workDir, pathJoin(workDir, ""good.json""));
print(string(r.ok));
";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdir_RecursiveFindsNestedBadFile()
    {
        var tempDir = CreateTempDirectory("ralph_val_");
        try
        {
            var sub = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "bad.html"), "<html></html></html>");
            var source = ValidationModules(tempDir) + @"
var r = validateWorkdir(workDir, ""PRD.md"");
print(string(r.ok));
";
            var output = RunProgram(source);
            Assert.Contains("false", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdir_AcceptsHtmlScriptWithBracesInStringsWhenNodeAvailable()
    {
        var tempDir = CreateTempDirectory("ralph_val_");
        try
        {
            var html = "<!DOCTYPE html><html><head></head><body><script>\n" +
                       "var hint = \"use { and } in strings\";\n" +
                       "function ok() { return hint; }\n" +
                       "</script></body></html>";
            File.WriteAllText(Path.Combine(tempDir, "game.html"), html);
            var source = ValidationModules(tempDir) + @"
var r = validateWorkdirFile(workDir, pathJoin(workDir, ""game.html""));
print(string(r.ok));
";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdir_HtmlScriptSyntaxErrorIncludesSourceLineContext()
    {
        var tempDir = CreateTempDirectory("ralph_val_");
        try
        {
            var html = "<html><body>\n<script>\nvar x = 1;\nfunction bad() {\n</script>\n</body></html>";
            File.WriteAllText(Path.Combine(tempDir, "game.html"), html);
            var source = ValidationModules(tempDir) + @"
var r = validateWorkdirFile(workDir, pathJoin(workDir, ""game.html""));
print(string(r.ok));
if (!r.ok && length(r.errors) > 0) {
    print(r.errors[0]);
}
";
            var output = RunProgram(source);
            Assert.Contains("false", output);
            Assert.Contains(">>", output);
            Assert.Contains("line", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdir_AcceptsValidMalda()
    {
        var tempDir = CreateTempDirectory("ralph_val_");
        try
        {
            var source = ValidationModules(tempDir) + @"
writeFile(pathJoin(workDir, ""ok.malda""), ""var n = 1;\nprint(n);\n"");
var r = validateWorkdirFile(workDir, pathJoin(workDir, ""ok.malda""));
print(string(r.ok));
";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdir_RejectsMaldaTypeError()
    {
        var tempDir = CreateTempDirectory("ralph_val_");
        try
        {
            var source = ValidationModules(tempDir) + @"
writeFile(pathJoin(workDir, ""bad.malda""), ""var n: int = \""abc\"";\n"");
var r = validateWorkdirFile(workDir, pathJoin(workDir, ""bad.malda""));
print(string(r.ok));
if (!r.ok && length(r.errors) > 0) {
    print(r.errors[0]);
}
";
            var output = RunProgram(source);
            Assert.Contains("false", output);
            Assert.Contains("bad.malda", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ValidateWorkdir_RejectsMaldaParseError()
    {
        var tempDir = CreateTempDirectory("ralph_val_");
        try
        {
            var source = ValidationModules(tempDir) + @"
writeFile(pathJoin(workDir, ""broke.malda""), ""function (\n"");
var r = validateWorkdirFile(workDir, pathJoin(workDir, ""broke.malda""));
print(string(r.ok));
";
            var output = RunProgram(source);
            Assert.Contains("false", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void LoopPrompts_PreferCheckMaldaAndBanPlanTools()
    {
        var loop = File.ReadAllText(Path.Combine(RepoRoot(), "Examples", "RalphWiggum", "ralph", "05-loop.malda"));
        Assert.Contains("check_malda", loop, StringComparison.Ordinal);
        Assert.Contains("test_malda", loop, StringComparison.Ordinal);
        Assert.Contains("delete_file", loop, StringComparison.Ordinal);
        Assert.Contains("update_plan", loop, StringComparison.Ordinal);
        Assert.Contains("mark_step", loop, StringComparison.Ordinal);
        Assert.Contains("Do not use submit_plan, update_plan, mark_step", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("getParseErrors(filePath)", loop, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateOnlyMode_InterpreterSmoke()
    {
        var tempDir = CreateTempDirectory("ralph_val_");
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "PRD.md"), "- [TODO] item\n");
            File.WriteAllText(Path.Combine(tempDir, "app.json"), "{}");
            var source = ValidationModules(tempDir) + @"
var check = validateWorkdir(workDir, ""PRD.md"");
if (check.ok) { print(""Validation: OK""); } else { print(""Validation FAILED""); }
";
            var output = RunProgram(source);
            Assert.Contains("Validation: OK", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
