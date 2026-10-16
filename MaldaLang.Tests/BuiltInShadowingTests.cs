// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using MaldaLang.Parser.AST.Expressions;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// A user binding (function, variable, parameter, import, include) shadows a flat built-in of
/// the same name; <c>math.abs(...)</c> stays the built-in; a class method does not shadow
/// (methods are not unqualified bindings). Interpreter, C# transpile and JS transpile must all
/// print the expected output (each case runs on the three backends).
/// </summary>
[Collection("Sequential")]
public class BuiltInShadowingTests
{
    /// <summary>Top-level function, nested local function, later declaration, namespaced call.</summary>
    private const string FunctionsSource = """
        function join(a) { return "mine"; }
        function abs(x) { return "user-abs"; }
        function outer() {
            function clamp(v) { return "local-clamp"; }
            return clamp(5);
        }
        io.print(join([1]));
        io.print(abs(-3));
        io.print(math.abs(-3));
        io.print(outer());
        io.print(clamp(15, 0, 10));
        io.print(zeros(2));
        function zeros(n) { return "late-zeros"; }
        """;

    [Fact]
    public void UserFunctions_ShadowFlatBuiltIns_OnAllBackends() =>
        AssertAllBackends(
            FunctionsSource,
            "mine\nuser-abs\n3\nlocal-clamp\n10\nlate-zeros",
            "shadow-functions");

    /// <summary>A variable holding a lambda, a parameter, a function used as a value.</summary>
    private const string BindingsSource = """
        var zeros = n => "lam" + string(n);
        function apply(join) { return join([1, 2]); }
        function trim(a) { return "mine"; }
        var f = trim;
        io.print(zeros(3));
        io.print(apply(xs => "param" + string(xs.length)));
        io.print(f([1]));
        io.print([[1], [2]].map(trim));
        function useBuiltIn() { return clamp(15, 0, 10); }
        io.print(useBuiltIn());
        """;

    [Fact]
    public void VariablesParametersAndReferences_ShadowFlatBuiltIns_OnAllBackends() =>
        AssertAllBackends(
            BindingsSource,
            "lam3\nparam2\nmine\n[mine, mine]\n10",
            "shadow-bindings");

    /// <summary>
    /// A parameter shadows only inside its function: the same name outside is the built-in.
    /// </summary>
    private const string ParameterScopeSource = """
        function pick(max) { return max(1, 2); }
        io.print(pick((a, b) => "param-max"));
        io.print(max(1, 2));
        """;

    [Fact]
    public void ParameterShadow_IsLexical_OnAllBackends() =>
        AssertAllBackends(ParameterScopeSource, "param-max\n2", "shadow-parameter-scope");

    private const string PipeSource = """
        function join(a, sep) { return "piped-" + sep; }
        io.print([1, 2] |> join("+"));
        function sum(xs) { return "user-sum"; }
        io.print([1, 2] |> sum);
        """;

    [Fact]
    public void PipeIntoShadowedName_CallsUserFunction_OnAllBackends() =>
        AssertAllBackends(PipeSource, "piped-+\nuser-sum", "shadow-pipe");

    /// <summary>Methods are reached through <c>this.</c>; a bare <c>abs(...)</c> is still the built-in.</summary>
    private const string MethodSource = """
        class Box {
            function abs(x) { return "method-abs"; }
            function run() { return string(abs(-2)) + " " + this.abs(-2); }
        }
        var b = new Box();
        io.print(b.run());
        """;

    [Fact]
    public void ClassMethod_DoesNotShadowBuiltIn_OnAllBackends() =>
        AssertAllBackends(MethodSource, "2 method-abs", "shadow-method");

    /// <summary>Actor helpers are called by bare name from handlers, so they do shadow.</summary>
    private const string ActorSource = """
        actor Bot {
            var n = 0;
            function abs(x) { return "actor-abs"; }
            on ping(d) {
                print(abs(d));
            }
        }
        var b = spawn Bot();
        send b.ping(-3);
        sleep(300);
        """;

    [Fact]
    public void ActorHelper_ShadowsBuiltIn_OnAllBackends() =>
        AssertAllBackends(ActorSource, "actor-abs", "shadow-actor");

    [Fact]
    public void ImportedAndIncludedFunctions_ShadowFlatBuiltIns_OnAllBackends()
    {
        var dir = Path.Combine(Path.GetTempPath(), "malda_shadow", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "shadow_mod.malda"), "export function clamp(v, lo, hi) { return \"mod-clamp\"; }\n");
            File.WriteAllText(Path.Combine(dir, "shadow_inc.malda"), "function dot(a, b) { return \"inc-dot\"; }\n");
            var main = Path.Combine(dir, "shadow_main.malda");
            File.WriteAllText(main, """
                import { clamp } from "shadow_mod.malda";
                include "shadow_inc.malda";
                io.print(clamp(15, 0, 10));
                io.print(dot([1], [2]));
                """);

            var interpreted = TestBase.CaptureInterpretAsync(File.ReadAllText(main), main).GetAwaiter().GetResult();
            Assert.Equal("mod-clamp\ninc-dot", Normalize(interpreted));
            InterpretTranspilePair.AssertSameFromFile(main, "shadow-import-include");
            InterpretJsPair.AssertSameFromFile(main, "shadow-import-include-js");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Analysis_MarksOnlyUsesInScopeOfABinding()
    {
        var statements = Parse("""
            function f(join) { return join(1); }
            var x = join([1]);
            """);

        var result = BuiltInShadowAnalysis.Analyze(statements);

        var use = Assert.Single(result.ShadowedUses);
        Assert.Equal("join", use.Name);
        Assert.Equal(1, use.Line);
        Assert.Empty(result.ProgramScopeNames);
    }

    [Fact]
    public void Analysis_IgnoresShaderFunctionsAndMethods()
    {
        var statements = Parse("""
            @shader()
            function frag(uv) { return dot(uv, uv); }
            class C { function dot(a, b) { return 0; } function g() { return dot(1, 2); } }
            """);

        Assert.Empty(BuiltInShadowAnalysis.Analyze(statements).ShadowedUses);
    }

    [Fact]
    public void Check_DoesNotWarnDeprecatedAlias_ForShadowedCall_AndReportsInfo()
    {
        var diagnostics = new LanguageService().GetDiagnostics(
            "function abs(x) { return x; }\nio.print(abs(-1));\nfunction f(max) { return max(1); }\n",
            "shadow.malda");

        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-style");
        var info = Assert.Single(diagnostics, d => d.Source == StdLibNamespaceDiagnostics.ShadowSource);
        Assert.Equal(DiagnosticSeverity.Info, info.Severity);
        Assert.Contains("`abs`", info.Message, StringComparison.Ordinal);
        Assert.Contains("math.abs", info.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_StillWarnsDeprecatedAlias_WhenNotShadowed()
    {
        var diagnostics = new LanguageService().GetDiagnostics("io.print(abs(-1));\n", "plain.malda");

        Assert.Contains(diagnostics, d => d.Source == "malda-style");
        Assert.DoesNotContain(diagnostics, d => d.Source == StdLibNamespaceDiagnostics.ShadowSource);
    }

    private const string IdeSource = """
        function zeros(n, fill) { return [fill]; }
        var z = zeros(3, 1);
        """;

    [Fact]
    public void Hover_OnShadowedCall_ShowsUserFunction()
    {
        var (line, column) = PositionAfter(IdeSource, "z = zer");
        var hover = new LanguageService().GetHoverInformation(IdeSource, line, column);
        Assert.Equal("function zeros(n, fill)", hover);
    }

    [Fact]
    public void SignatureHelp_OnShadowedCall_ShowsUserParameters()
    {
        var (line, column) = PositionAfter(IdeSource, "zeros(3, ");
        var help = new LanguageService().GetSignatureHelp(IdeSource, line, column);
        Assert.NotNull(help);
        Assert.Equal("zeros(n, fill)", help!.SignatureLabel);
    }

    [Fact]
    public void GoToDefinition_OnShadowedCall_JumpsToUserFunction()
    {
        var (line, column) = PositionAfter(IdeSource, "z = zer");
        var definition = new SymbolNavigationService().GetDefinition(IdeSource, line, column, "ide.malda");
        Assert.NotNull(definition);
        Assert.Equal("zeros", definition!.Name);
        Assert.Equal(0, definition.Span.Line);
    }

    private static void AssertAllBackends(string source, string expected, string label)
    {
        var interpreted = TestBase.CaptureInterpretAsync(source).GetAwaiter().GetResult();
        Assert.Equal(expected, Normalize(interpreted));
        InterpretTranspilePair.AssertSameFromSource(source, label);
        InterpretJsPair.AssertSameFromSource(source, label + "-js");
    }

    private static string Normalize(string text) => InterpretTranspilePair.Normalize(text);

    private static List<MaldaLang.Parser.AST.Statements.Statement> Parse(string source)
    {
        var parser = new MaldaLang.Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }

    private static (int Line, int Column) PositionAfter(string source, string marker)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var index = lines[i].IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
                return (i, index + marker.Length);
        }

        throw new InvalidOperationException($"Marker '{marker}' not found.");
    }
}
