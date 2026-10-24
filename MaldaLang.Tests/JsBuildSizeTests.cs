// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using MaldaLang.Compiler;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// J8 production build: the size budget of the core runtime, <c>--minify</c> (esbuild when
/// installed, else <see cref="JsMinifier.StripCommentsAndIndentation"/> on the runtime files) and
/// the size report every JavaScript build returns.
/// </summary>
[Collection("Sequential")]
public class JsBuildSizeTests
{
    private static string RuntimeSource => File.ReadAllText(PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js"));

    private static string ClientPath => PlanningPaths.ResolveRepoFile("MaldaLang.UIHost", "wwwroot", "malda-ui-client.js");

    [Fact]
    public void CoreRuntime_StaysWithinItsBudget()
    {
        var core = JsRuntimeAreas.Trim(RuntimeSource, new HashSet<string>());

        Assert.True(core.Length <= Compiler.Compiler.CoreRuntimeBudgetCharacters,
            $"The runtime written beside a core-only program is {core.Length} characters, over the budget of " +
            $"{Compiler.Compiler.CoreRuntimeBudgetCharacters} (Compiler.CoreRuntimeBudgetCharacters). Move the new code into an " +
            "optional area (// @malda-runtime-area) or raise the budget on purpose.");
    }

    [Theory]
    [InlineData("var a = 1; // note\n  var b = \"// not a comment\";\n", "var a = 1;\nvar b = \"// not a comment\";\n")]
    [InlineData("/* lead */\n    x = y / 2 / z; /* inline */ w = 1;\n", "x = y / 2 / z; w = 1;\n")]
    [InlineData("if (/a\\/b[/]c/.test(s)) {\n\treturn s.replace(/\\s+/g, \" \");\n}\n", "if (/a\\/b[/]c/.test(s)) {\nreturn s.replace(/\\s+/g, \" \");\n}\n")]
    [InlineData("const t = `line 1\n    // kept ${a + `in ${b}`}\n  end`; // gone\n", "const t = `line 1\n    // kept ${a + `in ${b}`}\n  end`;\n")]
    [InlineData("const o = { a: `${x}` }; const r = /}/;\n", "const o = { a: `${x}` }; const r = /}/;\n")]
    [InlineData("f();\n\n\n//# sourceMappingURL=app.js.map\n", "f();\n//# sourceMappingURL=app.js.map\n")]
    public void Strip_RemovesCommentsAndIndentation_AndKeepsCode(string input, string expected)
    {
        Assert.Equal(expected, JsMinifier.StripCommentsAndIndentation(input));
    }

    /// <summary>The stripped runtime and client run a program the same as the originals.</summary>
    [Fact]
    public void StrippedRuntime_RunsProgramsTheSame()
    {
        var program = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            var b = buf.float64(3);
            b[1] = 2.5;
            io.print(b);
            io.print(str.split("a/b/c", "/"));
            io.print(str.regexReplace("a  b", "\\s+", "-"));
            var t = ui.text({ "value": $"n={1 + 1}" });
            io.print(ui.mount(ui.column({}, [t]), "s")["treeHash"]);
            io.print(nn.relu([-1, 2]));
            """);

        var folder = Path.Combine(Path.GetTempPath(), "malda_js_size", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var stripped in new[] { false, true })
            {
                var target = Path.Combine(folder, stripped ? "stripped" : "original");
                Directory.CreateDirectory(target);
                var runtime = RuntimeSource;
                var client = File.ReadAllText(ClientPath);
                File.WriteAllText(Path.Combine(target, "malda-js-runtime.js"), stripped ? JsMinifier.StripCommentsAndIndentation(runtime) : runtime);
                File.WriteAllText(Path.Combine(target, "malda-ui-client.js"), stripped ? JsMinifier.StripCommentsAndIndentation(client) : client);
                File.WriteAllText(Path.Combine(target, "app.js"), program);
            }

            var original = RunNode(Path.Combine(folder, "original"));
            if (original == null)
                return;
            Assert.Equal(original, RunNode(Path.Combine(folder, "stripped")));
            Assert.Contains("[0, 2.5, 0]", original);
            Assert.True(new FileInfo(Path.Combine(folder, "stripped", "malda-js-runtime.js")).Length
                        < new FileInfo(Path.Combine(folder, "original", "malda-js-runtime.js")).Length * 0.9);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_ReportsSizes_AndMinifiesWithoutEsbuild()
    {
        var previous = Environment.GetEnvironmentVariable("MALDA_ESBUILD");
        var folder = Path.Combine(Path.GetTempPath(), "malda_js_size", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // A configured path that does not exist means "no esbuild", whatever is installed.
            Environment.SetEnvironmentVariable("MALDA_ESBUILD", Path.Combine(folder, "no-esbuild.cmd"));
            var source = Path.Combine(folder, "app.malda");
            File.WriteAllText(source, """ui.mount(ui.text({ "value": "hi" }));""");

            var plain = new Compiler.Compiler().CompileToJavaScript(source, Path.Combine(folder, "plain", "app.js"));
            Assert.True(plain.Success, plain.ErrorMessage);
            Assert.StartsWith("JavaScript size:", plain.Report);
            Assert.Contains("malda-js-runtime.js", plain.Report);
            Assert.Contains("areas: ui", plain.Report);
            Assert.Contains("malda-ui-client.js", plain.Report);
            Assert.Contains("(gzip ", plain.Report);

            var compiler = new Compiler.Compiler { MinifyJavaScript = true };
            var minified = compiler.CompileToJavaScript(source, Path.Combine(folder, "min", "app.js"));
            Assert.True(minified.Success, minified.ErrorMessage);
            Assert.Contains("esbuild not found", minified.Report);
            Assert.Equal(File.ReadAllText(Path.Combine(folder, "plain", "app.js")), File.ReadAllText(Path.Combine(folder, "min", "app.js")));
            var runtime = File.ReadAllText(Path.Combine(folder, "min", "malda-js-runtime.js"));
            Assert.StartsWith("// malda-js-runtime areas: ui", runtime);
            Assert.DoesNotContain("\n    ", runtime);
            Assert.True(runtime.Length < new FileInfo(Path.Combine(folder, "plain", "malda-js-runtime.js")).Length);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MALDA_ESBUILD", previous);
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }

    /// <summary>With esbuild installed (or MALDA_ESBUILD set), every file is minified and the map still names the .malda file.</summary>
    [Fact]
    public void Build_MinifiesWithEsbuild_WhenInstalled()
    {
        var folder = Path.Combine(Path.GetTempPath(), "malda_js_size", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            if (JsMinifier.FindEsbuild(folder) == null)
                return;
            var source = Path.Combine(folder, "app.malda");
            File.WriteAllText(source, """
                function greet(name) {
                    return "hi " + name;
                }
                io.print(greet("ada"));
                """);
            var result = new Compiler.Compiler { MinifyJavaScript = true }.CompileToJavaScript(source, Path.Combine(folder, "out", "app.js"));
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains("minified with esbuild", result.Report);
            Assert.Contains("app.malda", File.ReadAllText(Path.Combine(folder, "out", "app.js.map")));
            Assert.StartsWith("// malda-js-runtime areas: none", File.ReadAllText(Path.Combine(folder, "out", "malda-js-runtime.js")));
            Assert.Equal("hi ada", RunNode(Path.Combine(folder, "out"))?.Trim());
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }

    /// <summary>Runs app.js with the runtime and client beside it; null without Node.</summary>
    private static string? RunNode(string folder)
    {
        const string script = """
            const path = require("path");
            const dir = process.argv[2];
            try { require(path.join(dir, "malda-ui-client.js")); } catch (e) { if (e.code !== "MODULE_NOT_FOUND") throw e; }
            require(path.join(dir, "malda-js-runtime.js"));
            Promise.resolve(require(path.join(dir, "app.js")).main()).catch((e) => { console.error(e); process.exit(1); });
            """;
        var scriptPath = Path.Combine(folder, "run.js");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("MALDA_NODE_PATH") is { Length: > 0 } node ? node : "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(folder);
        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            return null;
        }

        using (process!)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(60_000), "Node timed out.");
            Assert.True(process.ExitCode == 0, "Node failed: " + stderr.Result);
            return stdout.Result.Replace("\r", string.Empty);
        }
    }
}
