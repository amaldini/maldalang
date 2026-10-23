// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Scaffolding;
using MaldaLang.Testing;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// <c>import js</c> (JS1 interop): ES modules, CommonJS, WASM through its glue module, globals,
/// callbacks and Promises, run in Node through <c>malda test --target js</c> next to real module
/// files; plus the parser, the host-backend rejection and the diagnostics around it.
/// </summary>
[Collection("Sequential")]
public class JsInteropTests
{
    private const string GeometryModule = """
export class Vec {
  constructor(x, y) { this.x = x; this.y = y; this.log = []; }
  length() { return Math.hypot(this.x, this.y); }
  append(entry) { this.log.push(entry); return this; }
  set(x, y) { this.x = x; this.y = y; return this; }
  map(fn) { return new Vec(fn(this.x), fn(this.y)); }
}
export const shapes = { Vec };
export function applyTwice(fn, value) { return fn(fn(value)); }
export async function later(value) { return value * 2; }
export async function fail() { throw new Error("nope from js"); }
""";

    private const string LegacyModule = """
module.exports = { greet(name) { return "hi " + name; } };
""";

    // add(i32, i32) -> i32, as raw WebAssembly bytes.
    private const string WasmGlueModule = """
const bytes = new Uint8Array([0,97,115,109,1,0,0,0,1,7,1,96,2,127,127,1,127,3,2,1,0,7,7,1,3,97,100,100,0,0,10,9,1,7,0,32,0,32,1,106,11]);
export async function init() {
  const { instance } = await WebAssembly.instantiate(bytes);
  return instance.exports;
}
""";

    private static JavaScriptCompileResult Compile(string sourcePath, string outputPath)
    {
        var result = new Compiler.Compiler().CompileToJavaScript(sourcePath, outputPath);
        return new JavaScriptCompileResult(result.Success, result.OutputPath, result.ErrorMessage);
    }

    /// <summary>Writes the modules and <paramref name="program"/> into a fresh folder and runs it on JavaScript.</summary>
    private static (int Code, string Output) RunOnJavaScript(string program, params (string Name, string Content)[] extraFiles)
    {
        InterpretJsPair.SkipIfJavaScriptUnavailable();
        var directory = Path.Combine(Path.GetTempPath(), "malda_js_interop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "geom.mjs"), GeometryModule);
            File.WriteAllText(Path.Combine(directory, "legacy.cjs"), LegacyModule);
            File.WriteAllText(Path.Combine(directory, "add.mjs"), WasmGlueModule);
            foreach (var (name, content) in extraFiles)
                File.WriteAllText(Path.Combine(directory, name), content);
            var testPath = Path.Combine(directory, "interop.test.malda");
            File.WriteAllText(testPath, program);

            var output = new StringWriter();
            var code = new TestCommandRunner(Compile).Run(new[] { testPath, "--target", "js" }, output, output);
            return (code, output.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertPasses(string program, params (string Name, string Content)[] extraFiles)
    {
        var (code, output) = RunOnJavaScript(program, extraFiles);
        Assert.True(code == 0, output);
    }

    [Fact]
    public void EsModule_NamespaceNamedExportsAndDottedNew()
    {
        AssertPasses("""
            import js geom = "./geom.mjs";
            import js { Vec } from "./geom.mjs";

            var v = new Vec(3, 4);
            assert(v.length() == 5, "method call");
            var w = new geom.shapes.Vec(1, 0);
            assert(w.length() == 1, "new through a namespace member");
            assert(new geom.Vec(6, 8).length() == 10, "new on a namespace export");
            """);
    }

    [Fact]
    public void JsObjects_KeepTheirOwnMethodsNamedLikeMaldaOnes()
    {
        // append / set / map are MALDA array and dict methods; on a JS object they are its own.
        AssertPasses("""
            import js { Vec } from "./geom.mjs";

            var v = new Vec(1, 2);
            v.append("a").append("b");
            assert(v.log.length == 2, "append");
            v.set(6, 8);
            assert(v.x == 6, "set");
            var doubled = v.map(n => n * 2);
            assert(doubled.y == 16, "map");
            """);
    }

    [Fact]
    public void Callbacks_PromisesAndRejections()
    {
        AssertPasses("""
            import js { applyTwice, later, fail } from "./geom.mjs";

            function double(x) {
                return x * 2;
            }
            assert(applyTwice(double, 3) == 12, "MALDA function as a callback");
            assert(applyTwice(x => x + 1, 3) == 5, "lambda as a callback");
            assert(await later(21) == 42, "await a JS Promise");

            var caught = "";
            try {
                await fail();
            } catch (e) {
                caught = e;
            }
            assert(caught == "nope from js", "a rejected Promise is a MALDA error: " + caught);
            """);
    }

    [Fact]
    public void CommonJsModule_NamespaceDefaultAndNamedExport()
    {
        AssertPasses("""
            import js legacy = "./legacy.cjs";
            import js { greet } from "./legacy.cjs";

            assert(legacy.default.greet("ann") == "hi ann", "module.exports is default");
            assert(greet("bob") == "hi bob", "named export of a CommonJS module");
            """);
    }

    [Fact]
    public void WasmModule_ThroughItsGlueModule()
    {
        AssertPasses("""
            import js wasm = "./add.mjs";

            var adder = await wasm.init();
            assert(adder.add(2, 40) == 42, "WASM export");
            """);
    }

    [Fact]
    public void Globals_ReadFromGlobalThis_MissingOnesAreNull()
    {
        AssertPasses("""
            import js global JSON, Math, notDefinedAnywhere;

            assert(JSON.stringify([1, 2]) == "[1,2]", "JSON global");
            assert(Math.max(1, 9) == 9, "Math global");
            assert(notDefinedAnywhere == null, "a missing global reads as null");
            """);
    }

    [Fact]
    public void JsBinding_ShadowsTheRuntimeModuleOfTheSameName()
    {
        // `math` here is the JS module, not MALDA's math.* namespace.
        AssertPasses(
            """
            import js math = "./mymath.mjs";

            assert(math.triple(3) == 9, "the imported module");
            """,
            ("mymath.mjs", "export function triple(x) { return x * 3; }\n"));
    }

    [Fact]
    public void MaldaLibrary_ImportedSelectively_KeepsItsJsImports()
    {
        AssertPasses(
            """
            import { area } from "./shapes.malda";

            assert(area(3, 4) == 5, "library function calling a JS module");
            """,
            ("shapes.malda", """
                import js { Vec } from "./geom.mjs";

                export function area(x, y) {
                    return new Vec(x, y).length();
                }
                """));
    }

    [Fact]
    public void MissingExport_NamesTheModuleAndTheExport()
    {
        var (code, output) = RunOnJavaScript("""
            import js { Nope } from "./geom.mjs";
            io.print("unreachable");
            """);

        Assert.Equal(1, code);
        Assert.Contains("import js \"./geom.mjs\" has no export 'Nope'.", output);
    }

    [Fact]
    public void MissingModule_NamesTheSpecifier()
    {
        var (code, output) = RunOnJavaScript("""
            import js missing = "./does-not-exist.mjs";
            io.print("unreachable");
            """);

        Assert.Equal(1, code);
        Assert.Contains("import js \"./does-not-exist.mjs\" failed:", output);
    }

    [Fact]
    public void Transpile_EmitsLoaderAndAwaitsItInMain()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            import js { Vec } from "./geom.mjs";
            import js global document;
            var v = new Vec(1, 2);
            v.append(3);
            """);

        Assert.Contains("let Vec, document;", js);
        Assert.Contains("document = globalThis[\"document\"];", js);
        Assert.Contains("import(\"./geom.mjs\")", js);
        Assert.Contains("async function main() {", js);
        Assert.Contains("await __maldaJsReady;", js);
    }

    [Fact]
    public void Transpile_CallsRootedAtABinding_ArePlainJavaScript()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            import js global document;
            document.body.append(document.createElement("div"));
            """);

        Assert.Contains("document.body.append(document.createElement(\"div\"));", js);
        Assert.DoesNotContain("arrayAppend", js);
    }

    [Fact]
    public void Transpile_GlobalsOnly_KeepsMainSynchronous()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            import js global console;
            console.log("x");
            """);

        Assert.Contains("function main() {", js);
        Assert.DoesNotContain("__maldaJsReady", js);
    }

    [Fact]
    public void Transpile_ReservedBindingName_IsRefused()
    {
        var ex = Assert.ThrowsAny<Exception>(() => new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            import js { mlRuntime } from "./x.mjs";
            """));

        Assert.Contains("cannot bind 'mlRuntime'", ex.Message);
    }

    [Fact]
    public void Transpile_SameNameBoundToTwoModules_IsRefused()
    {
        var ex = Assert.ThrowsAny<Exception>(() => new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            import js { Vec } from "./a.mjs";
            import js { Vec } from "./b.mjs";
            """));

        Assert.Contains("'Vec' is bound twice", ex.Message);
    }

    [Fact]
    public void CapabilityCheck_ImportedNamesAreTheProgramsOwn()
    {
        // readFile is host file I/O (JS0001) unless the program binds the name itself.
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            import js { readFile } from "./browser-files.mjs";
            var text = await readFile("a.txt");
            """);

        Assert.Contains("readFile(\"a.txt\")", js);
    }

    [Theory]
    [InlineData("import js THREE = \"three\";", JsImportKind.Namespace, "three", "THREE")]
    [InlineData("import js { Vector3, Mesh } from \"three\";", JsImportKind.Named, "three", "Vector3,Mesh")]
    [InlineData("import js global document, window;", JsImportKind.Global, null, "document,window")]
    [InlineData("import js global = \"./global.mjs\";", JsImportKind.Namespace, "./global.mjs", "global")]
    public void Parser_ReadsTheThreeForms(string source, JsImportKind kind, string? specifier, string names)
    {
        var statements = Parse(source);

        var jsImport = Assert.IsType<JsImportStatement>(Assert.Single(statements));
        Assert.Equal(kind, jsImport.Kind);
        Assert.Equal(specifier, jsImport.Specifier);
        Assert.Equal(names, string.Join(",", jsImport.Names));
    }

    [Fact]
    public void Parser_PackageNamedJs_IsStillAPackageImport()
    {
        var statements = Parse("import js;");

        var import = Assert.IsType<ImportStatement>(Assert.Single(statements));
        Assert.Equal("js", import.PackageName);
    }

    [Theory]
    [InlineData("import js { a, a } from \"x\";", "imported twice")]
    [InlineData("import js { a } \"x\";", "Expect 'from'")]
    [InlineData("import js lib \"x\";", "Expect '='")]
    [InlineData("import js lib = x;", "module as a string")]
    [InlineData("import js global ;", "at least one binding")]
    public void Parser_ReportsMalformedImports(string source, string expected)
    {
        var parser = new MaldaLang.Parser.Parser(new Lexer(source).Tokenize());
        parser.Parse();

        Assert.Contains(parser.Errors, error => error.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void NewWithDottedPath_KeepsThePath()
    {
        var statements = Parse("var v = new THREE.math.Vector3(1, 2, 3);");

        var declaration = Assert.IsType<VarDeclStatement>(Assert.Single(statements));
        var newExpression = Assert.IsType<MaldaLang.Parser.AST.Expressions.NewExpression>(declaration.Initializer);
        Assert.Equal("THREE.math.Vector3", newExpression.ClassName);
    }

    [Fact]
    public async Task Interpreter_RefusesImportJs_WithTheJavaScriptHint()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => TestBase.CaptureInterpretAsync("""
            import js THREE = "three";
            io.print("unreachable");
            """));

        Assert.Contains("only on the JavaScript backend", ex.Message);
        Assert.Contains("--mode js", ex.Message);
    }

    [Fact]
    public void CSharpTranspile_RefusesImportJs_WithoutAStackTrace()
    {
        var ex = Assert.ThrowsAny<Exception>(() => new Compiler.Compiler().TranspileToCSharpFromSource("""
            import js THREE = "three";
            io.print("unreachable");
            """));

        Assert.Contains("only on the JavaScript backend", ex.Message);
        Assert.DoesNotContain("   at ", ex.Message);
    }

    [Fact]
    public void StyleDiagnostics_JsBoundMath_IsNotTheDeprecatedAlias()
    {
        var warnings = new LanguageService()
            .GetDiagnostics("import js global Math;\nvar m = Math.max(1, 2);\n", "js-math.malda")
            .Where(d => d.Source == "malda-style" && d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        Assert.DoesNotContain(warnings, d => d.Message.Contains("deprecated module alias", StringComparison.Ordinal));
    }

    private static List<MaldaLang.Parser.AST.Statements.Statement> Parse(string source)
    {
        var parser = new MaldaLang.Parser.Parser(new Lexer(source).Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements;
    }
}
