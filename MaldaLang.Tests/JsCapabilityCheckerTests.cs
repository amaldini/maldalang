// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Compiler;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// One small program per <see cref="JsCapabilityChecker"/> rule. Each must raise its code;
/// <see cref="BackendCapabilityMatrixGuardTests"/> also requires one per JavaScript <c>no</c> row.
/// </summary>
public static class JsCapabilityFixtures
{
    public static IReadOnlyDictionary<string, string> ByCode { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["JS0001"] = """
            var text = readFile("notes.txt");
            io.writeFile("copy.txt", text);
            """,
        ["JS0002"] = """
            function work() { return "ok"; }
            workflow Job(input) {
                step result = work();
                return result;
            }
            var id = startWorkflow("Job", null);
            """,
        ["JS0003"] = """
            schema Item {
                id: int;
            }
            prompt makeItem(label) -> Item {
                user: "Make an item labeled {label}.";
            }
            """,
        ["JS0004"] = """
            var team = agents.team([], null);
            var agent = new Agent("writer", "Writer", "Write things");
            """,
        ["JS0005"] = """
            var db = new VectorDB();
            """,
        ["JS0006"] = """
            var model = new OnnxModel("model.onnx");
            """,
        ["JS0007"] = """
            var server = new HttpServer(8080);
            var api = new RestServer(8081);
            """,
        ["JS0008"] = """
            var page = ui.template("<p>{{name}}</p>", { "name": "x" });
            """,
        ["JS0009"] = """
            var job = enqueueJob("emails", {});
            """,
        ["JS0010"] = """
            var asm = loadAssembly("System.Xml");
            """,
        ["JS0011"] = """
            var output = runCommand("ls");
            """,
        ["JS0012"] = """
            api Calc {
                function add(a: number, b: number);
            }
            """,
        ["JS0013"] = """
            within (30s) {
                println("bounded");
            }
            """,
        ["JS0014"] = """
            var html = markdownToHtml("# Title");
            var hash = hashPassword("secret");
            AnsiConsole.markupLine("[green]done[/]");
            """
    };
}

public class JsCapabilityCheckerTests : TestBase
{
    public static IEnumerable<object[]> RuleCodes() =>
        JsCapabilityChecker.Rules.Select(rule => new object[] { rule.Code });

    [Fact]
    public void EveryRule_HasFixture_AndUniqueCode()
    {
        var codes = JsCapabilityChecker.Rules.Select(rule => rule.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches("^JS0[0-9]{3}$", code));

        var missing = codes.Where(code => !JsCapabilityFixtures.ByCode.ContainsKey(code)).ToArray();
        Assert.True(missing.Length == 0, "Rules without a fixture: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(RuleCodes))]
    public void Fixture_RaisesItsCode_WithLineAndColumn(string code)
    {
        var diagnostics = Check(JsCapabilityFixtures.ByCode[code]);

        var hits = diagnostics.Where(d => d.Code == code).ToList();
        Assert.True(hits.Count > 0, $"{code} fixture produced: {string.Join(" | ", diagnostics)}");
        Assert.All(hits, d =>
        {
            Assert.True(d.Line > 0, d.ToString());
            Assert.True(d.Column > 0, d.ToString());
        });
    }

    [Fact]
    public void Compile_WithSeveralViolations_ReportsEveryOne()
    {
        var source = """
            var text = readFile("a.txt");
            var db = new VectorDB();
            prompt ask(q) {
                user: "{q}";
            }
            var server = new HttpServer(8080);
            var job = enqueueJob("q", {});
            println(text);
            """;

        var root = CreateTempDirectory("malda_js_capability_");
        try
        {
            var sourcePath = Path.Combine(root, "app.malda");
            File.WriteAllText(sourcePath, source);
            var outputPath = Path.Combine(root, "out", "app.js");

            var result = new Compiler.Compiler().CompileToJavaScript(sourcePath, outputPath);

            Assert.False(result.Success);
            var message = result.ErrorMessage ?? string.Empty;
            Assert.Contains("Line 1:", message, StringComparison.Ordinal);
            Assert.Contains("JS0001", message, StringComparison.Ordinal);
            Assert.Contains("Line 2:", message, StringComparison.Ordinal);
            Assert.Contains("JS0005", message, StringComparison.Ordinal);
            Assert.Contains("Line 3:", message, StringComparison.Ordinal);
            Assert.Contains("JS0003", message, StringComparison.Ordinal);
            Assert.Contains("Line 6:", message, StringComparison.Ordinal);
            Assert.Contains("JS0007", message, StringComparison.Ordinal);
            Assert.Contains("Line 7:", message, StringComparison.Ordinal);
            Assert.Contains("JS0009", message, StringComparison.Ordinal);
            Assert.False(File.Exists(outputPath), "no JavaScript is written when the pre-check fails");
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Check_ReturnsViolationsInSourceOrder()
    {
        var diagnostics = Check("""
            var a = new VectorDB();
            var b = readFile("x");
            var c = enqueueJob("q", {});
            """);

        Assert.Equal(new[] { "JS0005", "JS0001", "JS0009" }, diagnostics.Select(d => d.Code).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, diagnostics.Select(d => d.Line).ToArray());
        // Calls point at the callee, not at the closing parenthesis.
        Assert.Equal(new[] { 9, 9, 9 }, diagnostics.Select(d => d.Column).ToArray());
    }

    [Fact]
    public void Check_IgnoresNamesTheProgramDeclares()
    {
        var diagnostics = Check("""
            function readFile(path) { return "stub:" + path; }
            class VectorDB {
                function add(x) { return x; }
            }
            var ui = { mount: (x) => x };
            println(readFile("a"));
            var db = new VectorDB();
            ui.mount(1);
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Check_ReportsHostOnlyCallInsideObjectLiteralValue()
    {
        var diagnostics = Check("""
            var handlers = {
                load: function(x) { return readFile(x); },
                nested: { env: dict { "k": getCommandLineArgs() } }
            };
            """);

        var fileIo = Assert.Single(diagnostics, d => d.Code == "JS0001");
        Assert.Equal(2, fileIo.Line);
        Assert.Contains(diagnostics, d => d.Code == "JS0011" && d.Line == 3);
    }

    [Fact]
    public void Check_AcceptsJavaScriptSupportedFeatures()
    {
        var diagnostics = Check("""
            io.print("hi");
            var home = getEnvOr("HOME", "/");
            var layer = new Dense(2, 1);
            var g = grounded.wrap("x", "src");
            var r = result.ok(1);
            actor Counter {
                var n = 0;
                on inc() { n = n + 1; }
            }
            println(math.sqrt(4));
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Check_SkipsPropertiesThatDoNotTargetJavaScript()
    {
        var diagnostics = Check("""
            @requires("file-io")
            @targets("interpreter", "csharp", "js")
            property readsFiles(x) {
                return readFile("a.txt") != null;
            }

            @requires("core")
            property hostOnlyByDefault(x) {
                return hasFile("b.txt") == hasFile("b.txt");
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Check_ReportsHostOnlyDeclarationOnce()
    {
        var diagnostics = Check(JsCapabilityFixtures.ByCode["JS0002"]);

        // The workflow is one diagnostic (its step is not reported again), plus the startWorkflow call.
        Assert.Equal(2, diagnostics.Count(d => d.Code == "JS0002"));
    }

    private static IReadOnlyList<JsCapabilityDiagnostic> Check(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var parser = new MaldaLang.Parser.Parser(tokens);
        List<Statement> statements = parser.Parse();
        Assert.True(parser.Errors.Count == 0, string.Join("; ", parser.Errors.Select(e => e.Message)));
        return JsCapabilityChecker.Check(statements);
    }
}
