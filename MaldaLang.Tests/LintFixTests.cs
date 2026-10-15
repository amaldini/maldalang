// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;
using System.Text.Json;
using MaldaLang.IDE.Services;
using MaldaLang.LanguageServer;
using MaldaLang.Linting;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// <c>malda lint --fix</c> (<see cref="FlatAliasFixer"/>): deprecated flat aliases become their
/// namespaced form, only where the rewrite is mechanical and every other byte is kept.
/// </summary>
public class LintFixTests
{
    private static string Fix(string source, string? path = null) => FlatAliasFixer.Fix(source, path).FixedSource;

    [Fact]
    public void Fix_RewritesFlatAliasCalls()
    {
        const string source = "var a = 3;\nprint(\"m \" + max(a, 2));\nvar s = upper(\"x\");\nvar e = getEnvOr(\"NOPE\", \"d\");\nvar f = Math.floor(2.5);\n";
        var fix = FlatAliasFixer.Fix(source);

        Assert.Equal(
            "var a = 3;\nio.print(\"m \" + math.max(a, 2));\nvar s = str.upper(\"x\");\nvar e = io.getEnvOr(\"NOPE\", \"d\");\nvar f = math.floor(2.5);\n",
            fix.FixedSource);
        Assert.Equal(5, fix.Edits.Count);
        Assert.Equal(new[] { "print", "max", "upper", "getEnvOr", "Math" }, fix.Edits.Select(e => e.OldText));
        Assert.Equal(2, fix.Edits[0].Line);
        Assert.Equal(1, fix.Edits[0].Column);
    }

    [Fact]
    public void Fix_ReachesObjectAndDictionaryLiteralValues()
    {
        const string source = "var o = { a: upper(\"x\"), b: [max(1, 2)] };\nvar d = { \"k\": trim(\" y \") };\n";
        Assert.Equal("var o = { a: str.upper(\"x\"), b: [math.max(1, 2)] };\nvar d = { \"k\": str.trim(\" y \") };\n", Fix(source));
    }

    [Fact]
    public void Fix_ShadowedNames_AreUntouched()
    {
        // A function, parameter, variable, lambda parameter, pattern or class member of the alias name.
        const string source =
            "function join(x) { return x; }\n" +
            "function f(max) { return max(1); }\n" +
            "var upper = (t) => t;\n" +
            "var g = (lower) => lower(\"A\");\n" +
            "class Box { function trim(x) { return x; } }\n" +
            "print(join(1));\n" +
            "print(upper(\"a\"));\n" +
            "print(trim(\" a \"));\n";
        var fixedSource = Fix(source);

        Assert.Contains("function join(x)", fixedSource, StringComparison.Ordinal);
        Assert.Contains("io.print(join(1));", fixedSource, StringComparison.Ordinal);
        Assert.Contains("return max(1);", fixedSource, StringComparison.Ordinal);
        Assert.Contains("lower(\"A\")", fixedSource, StringComparison.Ordinal);
        Assert.Contains("io.print(upper(\"a\"));", fixedSource, StringComparison.Ordinal);
        Assert.Contains("io.print(trim(\" a \"));", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("str.", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("math.", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Fix_BoundModuleName_BlocksRewritesIntoThatModule()
    {
        const string source = "var str = \"abc\";\nprint(length(str));\nprint(max(1, 2));\n";
        Assert.Equal("var str = \"abc\";\nio.print(length(str));\nio.print(math.max(1, 2));\n", Fix(source));
    }

    [Fact]
    public void Fix_ObjectKeysAndNamedArguments_DoNotBlock()
    {
        const string source = "var o = { max: 1, text: \"t\" };\nprint(max(o.max, 2));\n";
        Assert.Equal("var o = { max: 1, text: \"t\" };\nio.print(math.max(o.max, 2));\n", Fix(source));
    }

    [Fact]
    public void Fix_StringsAndComments_AreUntouched()
    {
        const string source =
            "// print(max(1, 2)) in a comment\n" +
            "/* upper(x) */\n" +
            "var s = \"print(max(1, 2))\";\n" +
            "var t = \"\"\"\nupper(\"x\")\n\"\"\";\n" +
            "var i = $\"{{max(1)}} literal\";\n" +
            "io.print(s + t + i);\n";
        Assert.Equal(source, Fix(source));
    }

    [Fact]
    public void Fix_InterpolationHoles_AreRewritten()
    {
        const string source = "var a = 1;\nio.print($\"v={max(a, 2)} u={upper(\"q\")} {{max(9)}}\");\n";
        Assert.Equal("var a = 1;\nio.print($\"v={math.max(a, 2)} u={str.upper(\"q\")} {{max(9)}}\");\n", Fix(source));
    }

    [Fact]
    public void Fix_MembersDeclarationsAndShaders_AreUntouched()
    {
        const string source =
            "var parts = \"a,b\".split(\",\");\n" +
            "var n = parts.length;\n" +
            "@shader()\nfunction frag(uv) { return cos(dot(uv, uv)); }\n" +
            "io.print(parts.join(\"-\"));\n";
        Assert.Equal(source, Fix(source));
    }

    [Fact]
    public void Fix_PreservesCrLfWhitespaceAndComments()
    {
        const string source = "// header\r\nprint(\"a\");   // trailing\r\n\r\n\tprint( max(1,2) );\r\n";
        Assert.Equal("// header\r\nio.print(\"a\");   // trailing\r\n\r\n\tio.print( math.max(1,2) );\r\n", Fix(source));
    }

    [Fact]
    public void Fix_IsIdempotent()
    {
        const string source = "var a = 3;\nprint(max(a, 2));\nprint($\"{upper(\"x\")}\");\n";
        var once = Fix(source);
        Assert.NotEqual(source, once);
        Assert.Equal(once, Fix(once));
        Assert.Empty(FlatAliasFixer.Fix(once).Edits);
    }

    [Fact]
    public void Fix_RespectsSuppressionComments()
    {
        const string source =
            "print(1); // malda-lint-disable-line malda-style\n" +
            "// malda-lint-disable-next-line malda-style\n" +
            "print(2);\n" +
            "print(3); // malda-lint-disable-line ML0001\n" +
            "print(4); // malda-lint-disable-line\n";
        Assert.Equal(
            "print(1); // malda-lint-disable-line malda-style\n" +
            "// malda-lint-disable-next-line malda-style\n" +
            "print(2);\n" +
            "io.print(3); // malda-lint-disable-line ML0001\n" +
            "print(4); // malda-lint-disable-line\n",
            Fix(source));

        Assert.Equal(
            "// malda-lint-disable-file malda-style\nprint(1);\n",
            Fix("// malda-lint-disable-file malda-style\nprint(1);\n"));
    }

    [Fact]
    public void Fix_ParseError_LeavesFileAlone()
    {
        var fix = FlatAliasFixer.Fix("print(max(1, 2);\nvar = ;\n");
        Assert.False(fix.Parsed);
        Assert.Empty(fix.Edits);
    }

    [Fact]
    public void Fix_IncludedFiles_ShadowAndAreNotRewrittenThroughTheIncluder()
    {
        var dir = NewTempDir();
        try
        {
            var lib = Path.Combine(dir, "lib.malda");
            var main = Path.Combine(dir, "main.malda");
            File.WriteAllText(lib, "function join(a) { return a; }\nfunction show(x) { print(x); }\n");
            const string mainSource = "include \"lib.malda\";\nprint(join(1));\nshow(max(1, 2));\n";
            File.WriteAllText(main, mainSource);

            // The included declaration shadows join; the includer's own calls are rewritten,
            // the included file's are not (fixing main does not edit lib).
            var fix = FlatAliasFixer.Fix(mainSource, main);
            Assert.Equal("include \"lib.malda\";\nio.print(join(1));\nshow(math.max(1, 2));\n", fix.FixedSource);
            Assert.All(fix.Edits, e => Assert.True(e.Line >= 2));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FixFiles_LibraryHonoursNamesItsIncluderBinds()
    {
        var dir = NewTempDir();
        try
        {
            var lib = Path.Combine(dir, "lib.malda");
            var main = Path.Combine(dir, "main.malda");
            // lib calls trim(), which main defines; lib's print() is still an alias.
            const string libSource = "function show(x) { print(trim(x)); }\n";
            const string mainSource = "function trim(x) { return x; }\ninclude \"lib.malda\";\nshow(\" a \");\n";
            File.WriteAllText(lib, libSource);
            File.WriteAllText(main, mainSource);

            var fixes = FlatAliasFixer.FixFiles(new[] { (lib, libSource), (main, mainSource) });
            Assert.Equal("function show(x) { io.print(trim(x)); }\n", fixes[0].FixedSource);
            Assert.False(fixes[1].Changed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Fix_WholeFileImport_BindsTheModuleNames()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "helpers.malda"), "export function clamp(x) { return x; }\n");
            var main = Path.Combine(dir, "main.malda");
            const string source = "import \"helpers.malda\";\nprint(clamp(5));\n";
            File.WriteAllText(main, source);
            Assert.Equal("import \"helpers.malda\";\nio.print(clamp(5));\n", Fix(source, main));

            const string selective = "import { clamp } from \"helpers.malda\";\nprint(clamp(5));\n";
            Assert.Equal("import { clamp } from \"helpers.malda\";\nio.print(clamp(5));\n", Fix(selective, main));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Fix_KeepsInterpreterOutput()
    {
        const string source = "var a = [3, 1, 2];\nprint(join(a, \",\"));\nprint(max(4, 9) + min(1, 2));\nprint(upper(\"x\") + trim(\"  y \"));\nprint($\"len={length(\"abcd\")}\");\nprint(Math.floor(2.7));\n";
        var fixedSource = Fix(source);
        Assert.DoesNotContain("print(join", fixedSource.Replace("io.print(", ""), StringComparison.Ordinal);
        Assert.Equal(await TestBase.CaptureInterpretAsync(source), await TestBase.CaptureInterpretAsync(fixedSource));
    }

    [Fact]
    public void FixedSource_HasNoDeprecatedAliasWarnings()
    {
        const string source = "var a = 3;\nprint(max(a, 2));\nvar s = $\"{upper(\"q\")}\";\nprint(Math.round(1.5));\n";
        var diagnostics = new LanguageService().GetDiagnostics(Fix(source));
        Assert.DoesNotContain(diagnostics, d => d.Source == FlatAliasFixer.StyleCode);
    }

    [Fact]
    public void Cli_Fix_RewritesFilesPrintsSummaryAndKeepsBom()
    {
        var dir = NewTempDir();
        try
        {
            var a = Path.Combine(dir, "a.malda");
            var b = Path.Combine(dir, "sub", "b.malda");
            var c = Path.Combine(dir, ".staging", "c.malda");
            Directory.CreateDirectory(Path.GetDirectoryName(b)!);
            Directory.CreateDirectory(Path.GetDirectoryName(c)!);
            File.WriteAllText(a, "print(1);\r\nprint(max(1, 2));\r\n", new UTF8Encoding(true));
            File.WriteAllText(b, "io.print(1);\n");
            File.WriteAllText(c, "print(1);\n");

            var output = new StringWriter();
            var exit = new LintCommandRunner().Run(new[] { "--fix", dir }, output, new StringWriter());

            Assert.Equal(LintCommandRunner.ExitOk, exit);
            var bytes = File.ReadAllBytes(a);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            Assert.Equal("io.print(1);\r\nio.print(math.max(1, 2));\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
            Assert.Equal("io.print(1);\n", File.ReadAllText(b));
            Assert.Equal("print(1);\n", File.ReadAllText(c)); // dot-folders are skipped
            Assert.Contains("lint --fix: 3 replacement(s) in 1 of 2 file(s)", output.ToString(), StringComparison.Ordinal);

            var json = new StringWriter();
            Assert.Equal(LintCommandRunner.ExitOk, new LintCommandRunner().Run(new[] { "--fix", "--json", a }, json, new StringWriter()));
            using var doc = JsonDocument.Parse(json.ToString());
            Assert.Equal(0, doc.RootElement.GetProperty("replacementCount").GetInt32());
            Assert.Equal(0, doc.RootElement.GetProperty("fixedFileCount").GetInt32());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CheckFix_UsesTheSameAliasRewrite()
    {
        // malda check --fix shares FlatAliasFixer: every alias (not a fixed list), never inside
        // strings, never a name the program declares.
        const string source = "function join(x) { return x; }\nprint(\"print(1)\");\nprint(join(1));\nvar m = max(1, 2);\n";
        Assert.Equal(
            "function join(x) { return x; }\nio.print(\"print(1)\");\nio.print(join(1));\nvar m = math.max(1, 2);\n",
            CheckFixer.Apply(source));
    }

    [Fact]
    public void Cli_Fix_WithStdin_IsUsageError()
    {
        var error = new StringWriter();
        Assert.Equal(
            LintCommandRunner.ExitUsage,
            new LintCommandRunner().Run(new[] { "--fix", "--stdin" }, new StringWriter(), error, new StringReader("print(1);\n")));
        Assert.Contains("--fix", error.ToString(), StringComparison.Ordinal);

        var help = new StringWriter();
        new LintCommandRunner().Run(new[] { "--help" }, help, new StringWriter());
        Assert.Contains("--fix", help.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lsp_StyleWarning_OffersQuickFix_OnlyWhereTheFixerRewrites()
    {
        var store = new DocumentStore();
        var uri = new DocumentUri("file", "", "/fix_test.malda", null, null, null);
        const string text = "function join(x) { return x; }\nprint(join(1));\n";
        store.Set(uri, text);
        var handler = new MaldaCodeActionHandler(store, new LanguageService());

        var printDiagnostic = StyleDiagnostic(1, 0, 5);
        var joinDiagnostic = StyleDiagnostic(1, 6, 4);
        var request = new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(1, 0), new Position(1, 10)),
            Context = new CodeActionContext
            {
                Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(printDiagnostic, joinDiagnostic)
            }
        };

        var result = await handler.Handle(request, CancellationToken.None);
        var action = Assert.Single(result!).CodeAction!;
        Assert.Equal(CodeActionKind.QuickFix, action.Kind);
        var edit = Assert.Single(action.Edit!.Changes![uri]);
        Assert.Equal("io.print", edit.NewText);
        Assert.Equal(new Position(1, 0), edit.Range.Start);
        Assert.Equal(new Position(1, 5), edit.Range.End);
    }

    private static OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic StyleDiagnostic(int line, int character, int length) => new()
    {
        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(line, character), new Position(line, character + length)),
        Source = FlatAliasFixer.StyleCode,
        Message = "deprecated flat alias"
    };

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "malda_lint_fix_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
