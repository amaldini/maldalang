// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.Text;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// <c>--mode esm</c> (J2): MALDA compiled to ES modules that JavaScript imports, with the
/// declarations exported, the program run on evaluation, and one runtime shared by every
/// module beside the same <c>malda-js-runtime.js</c>.
/// </summary>
[Collection("Sequential")]
public class JsEsModuleOutputTests
{
    private const string GeometrySource = """
        export var UNIT = 10;
        var calls = 0;

        export function area(w, h) {
            calls = calls + 1;
            return w * h * UNIT;
        }

        export function callCount() {
            return calls;
        }

        export class Box {
            var w;
            var h;
            function Box(w, h) {
                this.w = w;
                this.h = h;
            }
            function volume(d) {
                return this.w * this.h * d;
            }
        }

        export type Shape = Circle(r) | Square(side);

        function hidden() {
            return 1;
        }
        io.print("geometry loaded");
        """;

    private const string CounterSource = """
        var total = 0;
        function bump(n) {
            total = total + n;
            return total;
        }
        function slow(x) {
            await sleep(5);
            return x * 2;
        }
        """;

    [Fact]
    public void JavaScriptImportsExportedDeclarations_AndHiddenOnesStayPrivate()
    {
        var output = RunScenario(
            new[] { ("geometry.malda", GeometrySource) },
            """
            import { area, callCount, Box, UNIT, Circle } from "./out/geometry.mjs";
            import * as geometry from "./out/geometry.mjs";
            console.log(area(2, 3), callCount(), UNIT);
            console.log(new Box(2, 3).volume(4));
            console.log(Circle(1).tag);
            console.log("hidden" in geometry, "main" in geometry);
            """);

        Assert.Equal(
            "geometry loaded\n60 1 10\n24\nCircle\nfalse false",
            output);
    }

    [Fact]
    public void FileWithoutExport_ExportsEveryTopLevelDeclaration()
    {
        var output = RunScenario(
            new[] { ("counter.malda", CounterSource) },
            """
            import { bump, slow, total } from "./out/counter.mjs";
            console.log(bump(2), bump(3));
            console.log(await slow(21));
            console.log(total);
            """);

        // `total` is a live binding: the importer sees the value the module assigned.
        Assert.Equal("2 5\n42\n5", output);
    }

    [Fact]
    public void TwoModules_ShareOneRuntimeInstance()
    {
        var output = RunScenario(
            new[] { ("geometry.malda", GeometrySource), ("counter.malda", CounterSource) },
            """
            const before = globalThis.mlRuntime;
            await import("./out/geometry.mjs");
            const afterFirst = globalThis.mlRuntime;
            await import("./out/counter.mjs");
            console.log(before === undefined, afterFirst === globalThis.mlRuntime);
            """);

        Assert.Equal("true true", output.Split('\n').Last());
    }

    [Fact]
    public void ImportJs_WorksInsideAnEsModule()
    {
        var output = RunScenario(
            new[] { ("interop.malda", """
                import js { twice } from "./helper.mjs";
                export function quad(x) {
                    return twice(twice(x));
                }
                """) },
            """
            import { quad } from "./out/interop.mjs";
            console.log(quad(3));
            """,
            ("out/helper.mjs", "export function twice(x) { return x * 2; }\n"));

        Assert.Equal("12", output);
    }

    [Fact]
    public void Transpile_EmitsRuntimeImportTopLevelAwaitAndExports()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptModuleFromSource("""
            export function f() {
                return 1;
            }
            function g() {
                return 2;
            }
            """);

        Assert.StartsWith("import \"./malda-js-runtime.js\";", js.TrimStart('﻿'));
        Assert.Contains("await main();", js);
        Assert.Contains("export { f };", js);
        Assert.DoesNotContain("MaldaApp", js);
        Assert.DoesNotContain("module.exports", js);
    }

    [Fact]
    public void Transpile_ExportsOnlyTheCompiledFilesDeclarations()
    {
        var directory = NewDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "lib.malda"), "function fromLib() {\n    return 1;\n}\n");
            var mainPath = Path.Combine(directory, "app.malda");
            var source = "import \"./lib.malda\";\nfunction fromApp() {\n    return fromLib();\n}\n";
            File.WriteAllText(mainPath, source);

            var js = new Compiler.Compiler().TranspileToJavaScriptModuleFromSource(source, mainPath);

            Assert.Contains("function fromLib()", js);
            Assert.Contains("export { fromApp };", js);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CompileToJavaScriptModule_WritesMjsRuntimeAndModuleHostPage()
    {
        var directory = NewDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "app.malda");
            File.WriteAllText(sourcePath, "io.print(\"hi\");\n");

            var result = new Compiler.Compiler().CompileToJavaScriptModule(sourcePath, string.Empty);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(Path.Combine(directory, "app.mjs"), result.OutputPath);
            Assert.True(File.Exists(Path.Combine(directory, "malda-js-runtime.js")));
            var html = File.ReadAllText(Path.Combine(directory, "index.html"));
            Assert.Contains("<script type=\"module\">", html);
            Assert.Contains("await import(\"./app.mjs\");", html);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Compiles each MALDA source to <c>out/&lt;name&gt;.mjs</c>, writes <paramref name="mainScript"/>
    /// as <c>main.mjs</c> and returns what Node prints.
    /// </summary>
    private static string RunScenario(
        (string Name, string Source)[] maldaFiles,
        string mainScript,
        params (string Name, string Content)[] extraFiles)
    {
        InterpretJsPair.SkipIfJavaScriptUnavailable();
        var directory = NewDirectory();
        try
        {
            var outDirectory = Path.Combine(directory, "out");
            Directory.CreateDirectory(outDirectory);
            foreach (var (name, content) in extraFiles)
                File.WriteAllText(Path.Combine(directory, name), content);

            foreach (var (name, source) in maldaFiles)
            {
                var sourcePath = Path.Combine(directory, name);
                File.WriteAllText(sourcePath, source);
                var result = new Compiler.Compiler().CompileToJavaScriptModule(
                    sourcePath,
                    Path.Combine(outDirectory, Path.ChangeExtension(name, ".mjs")));
                Assert.True(result.Success, result.ErrorMessage);
            }

            var mainPath = Path.Combine(directory, "main.mjs");
            File.WriteAllText(mainPath, mainScript);
            return RunNode(mainPath, directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string RunNode(string scriptPath, string workingDirectory)
    {
        var node = Environment.GetEnvironmentVariable("MALDA_NODE_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(node) ? "node" : node,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add(scriptPath);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30_000), "Node timed out.");
        Assert.True(process.ExitCode == 0, stderr.Result);
        return stdout.Result.Replace("\r", string.Empty).TrimEnd('\n');
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "malda_esm", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
