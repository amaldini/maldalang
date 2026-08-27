// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Scaffolding;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class CapabilityTokenTests : TestBase
{
    private static List<Diagnostic> AnalyzeStrict(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Enabled, diagnostics);
        return diagnostics;
    }

    private static string Slash(string path) => path.Replace("\\", "/");

    [Fact]
    public void Mint_ExposesKindAndPath_AndIsUnforgeable()
    {
        var source = """
            var notes = cap.fileRead("notes.md");
            print(notes.kind);
            print(notes.path);
            print(cap.is(notes));
            print(cap.is(notes, "fileRead"));
            print(cap.is(notes, "fileWrite"));
            print(cap.is({ "kind": "fileRead", "path": "notes.md" }));
            print(cap.is("notes.md"));
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("fileRead", lines[0]);
        Assert.Equal("notes.md", lines[1]);
        Assert.Equal("true", lines[2]);
        Assert.Equal("true", lines[3]);
        Assert.Equal("false", lines[4]);
        Assert.Equal("false", lines[5]);
        Assert.Equal("false", lines[6]);
    }

    [Fact]
    public void Read_AcceptsToken_RejectsStringAndForgedDict()
    {
        var dir = CreateTempDirectory("cap_read_");
        try
        {
            var file = Path.Combine(dir, "notes.md");
            File.WriteAllText(file, "hello-cap");
            var path = Slash(file);
            var source = $$"""
                var notes = cap.fileRead("{{path}}");
                print(cap.read(notes));
                var forged = false;
                try {
                    cap.read({ "kind": "fileRead", "path": "{{path}}" });
                } catch (e) {
                    forged = true;
                }
                print(forged);
                var asString = false;
                try {
                    cap.read("{{path}}");
                } catch (e) {
                    asString = true;
                }
                print(asString);
                """;
            var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("hello-cap", lines[0]);
            Assert.Equal("true", lines[1]);
            Assert.Equal("true", lines[2]);
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void IoReadFile_AcceptsFileReadToken()
    {
        var dir = CreateTempDirectory("cap_io_");
        try
        {
            var file = Path.Combine(dir, "io.md");
            File.WriteAllText(file, "via-io");
            var path = Slash(file);
            var source = $$"""
                var notes = cap.fileRead("{{path}}");
                print(io.readFile(notes));
                """;
            var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("via-io", lines[0]);
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void WriteAndList_RequireMatchingKinds()
    {
        var dir = CreateTempDirectory("cap_write_");
        try
        {
            var file = Path.Combine(dir, "out.txt");
            var path = Slash(file);
            var source = $$"""
                cap.write(cap.fileWrite("{{path}}"), "written");
                print(io.readFile("{{path}}"));
                var items = cap.list(cap.dirList("{{Slash(dir)}}"));
                print(items.length >= 1);
                var wrongKind = false;
                try {
                    cap.read(cap.fileWrite("{{path}}"));
                } catch (e) {
                    wrongKind = true;
                }
                print(wrongKind);
                """;
            var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("written", lines[0]);
            Assert.Equal("true", lines[1]);
            Assert.Equal("true", lines[2]);
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Confine_AttenuatesUnderParent_AndRejectsEscape()
    {
        var dir = CreateTempDirectory("cap_confine_");
        try
        {
            var nested = Path.Combine(dir, "sub");
            Directory.CreateDirectory(nested);
            var file = Path.Combine(nested, "ok.txt");
            File.WriteAllText(file, "nested");
            var root = Slash(dir);
            var source = $$"""
                var parent = cap.fileRead("{{root}}");
                var child = cap.confine(parent, "sub/ok.txt");
                print(cap.read(child));
                var escaped = false;
                try {
                    cap.confine(parent, "../outside.txt");
                } catch (e) {
                    escaped = true;
                }
                print(escaped);
                """;
            var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("nested", lines[0]);
            Assert.Equal("true", lines[1]);
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void JsonRoundTrip_DoesNotRehydrateAToken()
    {
        var source = """
            var notes = cap.fileRead("notes.md");
            var raw = toJSON(notes);
            var parsed = parseJSON(raw);
            print(cap.is(notes));
            print(cap.is(parsed));
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("false", lines[1]);
    }

    [Fact]
    public void TokensAreImmutable_AndHaveNoFlatAlias()
    {
        var source = """
            var notes = cap.fileRead("notes.md");
            var mutated = false;
            try {
                notes.path = "/etc/passwd";
            } catch (e) {
                mutated = true;
            }
            print(mutated);
            print(notes.path);
            var threw = false;
            try {
                fileRead("notes.md");
            } catch (e) {
                threw = true;
            }
            print(threw);
            print(cap != null);
            """;
        var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("notes.md", lines[1]);
        Assert.Equal("true", lines[2]);
        Assert.Equal("true", lines[3]);
    }

    [Fact]
    public void ToolShapedHelper_CannotInventAPath()
    {
        var dir = CreateTempDirectory("cap_tool_");
        try
        {
            var file = Path.Combine(dir, "allowed.txt");
            File.WriteAllText(file, "allowed");
            var path = Slash(file);
            var source = $$"""
                var allowed = cap.fileRead("{{path}}");
                function read_allowed(fileCap) {
                    return cap.read(fileCap);
                }
                print(read_allowed(allowed));
                var invented = false;
                try {
                    read_allowed({ "kind": "fileRead", "path": "{{path}}" });
                } catch (e) {
                    invented = true;
                }
                print(invented);
                """;
            var lines = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("allowed", lines[0]);
            Assert.Equal("true", lines[1]);
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void StrictMode_PureCannotMintOrConsumeCap()
    {
        var diagnostics = AnalyzeStrict("""
            @pure()
            function bad() {
                return cap.fileRead("notes.md");
            }
            """);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-pure" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StrictMode_EffectsCap_AllowsCapNamespace()
    {
        var ok = AnalyzeStrict("""
            @effects("cap")
            function logNotes(fileCap) {
                return cap.read(fileCap);
            }
            """);
        Assert.DoesNotContain(ok, d => d.Source == "malda-effects");

        var bad = AnalyzeStrict("""
            @effects("print")
            function bad(fileCap) {
                return cap.read(fileCap);
            }
            """);
        Assert.Contains(bad, d =>
            d.Source == "malda-effects" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReadWrite_TranspileAgreesWithInterpreter()
    {
        var dir = CreateTempDirectory("cap_transpile_");
        try
        {
            var file = Path.Combine(dir, "t.txt");
            var path = Slash(file);
            var source = $$"""
                cap.write(cap.fileWrite("{{path}}"), "parity");
                var notes = cap.fileRead("{{path}}");
                print(cap.read(notes));
                print(cap.is({ "kind": "fileRead", "path": "{{path}}" }));
                """;
            var interpreted = RunProgram(source).Replace("\r\n", "\n").Trim();
            File.Delete(file);
            var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut.Replace("\r\n", "\n").Trim();
            Assert.Equal(interpreted, transpiled);
            Assert.Contains("parity", transpiled);
            Assert.Contains("false", transpiled);
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void FewShot_ToolCapRead_ConfineAndEscape()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "llm", "few-shot", "26_tool_cap_read.malda");
        var lines = RunProgram(File.ReadAllText(path)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void Scaffold_AgentTemplate_RunsAppAndMaldaTest()
    {
        var root = CreateTempDirectory("malda_scaffold_agent_run_");
        var destination = Path.Combine(root, "run-agent");
        try
        {
            var scaffolder = new TemplateScaffolder();
            Assert.Equal(0, scaffolder.Scaffold("agent", destination, new StringWriter(), new StringWriter()));

            var appPath = Path.Combine(destination, "app.malda");
            var stdout = CaptureInterpretAsync(File.ReadAllText(appPath), appPath).GetAwaiter().GetResult();
            var lines = stdout.Replace("\r\n", "\n").Trim().Split('\n');
            Assert.Equal("Welcome to your MALDA agent workspace.", lines[0]);
            Assert.Equal("true", lines[1]);
            Assert.Equal("true", lines[2]);

            var testOutput = new StringWriter();
            var testError = new StringWriter();
            var testPath = Path.Combine(destination, "tests", "cap_tools.test.malda");
            var testCode = new MaldaLang.Testing.TestCommandRunner().Run(new[] { testPath }, testOutput, testError);
            Assert.Equal(0, testCode);
            Assert.Equal(string.Empty, testError.ToString());
            Assert.Contains("passed", testOutput.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }
}
