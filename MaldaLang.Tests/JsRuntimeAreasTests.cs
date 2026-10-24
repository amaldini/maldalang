// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Diagnostics;
using System.Text;
using MaldaLang.Compiler;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// J2 runtime split: the runtime written beside a JavaScript program keeps the optional
/// <c>game</c> / <c>three</c> areas only when the program (or another one compiled into the same
/// folder) uses them.
/// </summary>
[Collection("Sequential")]
public class JsRuntimeAreasTests
{
    private static string FullRuntime =>
        File.ReadAllText(PlanningPaths.ResolveRepoFile("Examples", "Web", "wwwroot", "malda-js-runtime.js"));

    [Fact]
    public void Runtime_MarksEveryOptionalArea_WithAStartAndAnEnd()
    {
        var runtime = FullRuntime;
        foreach (var area in JsRuntimeAreas.All)
        {
            Assert.Contains("// @malda-runtime-area " + area, runtime);
            Assert.Contains("// @malda-runtime-area-end " + area, runtime);
        }
    }

    [Fact]
    public void UsedBy_ReadsTheGeneratedCalls()
    {
        Assert.Empty(JsRuntimeAreas.UsedBy("mlRuntime.io.print(1);"));
        Assert.Equal(new[] { "game" }, JsRuntimeAreas.UsedBy("mlRuntime.game.createCanvas(1, 2);").ToArray());
        Assert.Equal(
            new[] { "game", "three" },
            JsRuntimeAreas.UsedBy("mlRuntime.three.createScene(); mlRuntime.game.clear();").OrderBy(a => a).ToArray());
    }

    [Fact]
    public void Trim_KeepingEveryArea_ReturnsTheFullRuntime()
    {
        var full = FullRuntime;
        Assert.Same(full, JsRuntimeAreas.Trim(full, JsRuntimeAreas.All.ToHashSet()));
    }

    [Fact]
    public void CoreRuntime_IsAboutHalfTheSize_AndNamesAMissingArea()
    {
        var full = FullRuntime;
        var core = JsRuntimeAreas.Trim(full, new HashSet<string>());

        Assert.StartsWith("// malda-js-runtime areas: none", core);
        Assert.True(core.Length < full.Length * 0.65, $"core runtime is {core.Length} of {full.Length} characters");

        var output = RunWithRuntime(core, """
            mlRuntime.io.print(mlRuntime.str.upper("core"));
            try { mlRuntime.game.createCanvas(1, 1); } catch (e) { console.log(e.message); }
            """);
        Assert.Equal("CORE", output[0]);
        Assert.StartsWith("malda-js-runtime.js was written without the 'game' area (game.createCanvas).", output[1]);
    }

    [Fact]
    public void GameOnlyRuntime_KeepsGame_AndDropsThree()
    {
        var gameOnly = JsRuntimeAreas.Trim(FullRuntime, new HashSet<string> { "game" });

        var output = RunWithRuntime(gameOnly, """
            console.log(typeof mlRuntime.game.overlapRect, mlRuntime.game.overlapRect(0, 0, 2, 2, 1, 1, 2, 2));
            try { mlRuntime.three.createScene(); } catch (e) { console.log(e.message.split(" (")[0]); }
            """);
        Assert.Equal("function true", output[0]);
        Assert.Equal("malda-js-runtime.js was written without the 'three' area", output[1]);
    }

    [Fact]
    public void ProgramsCompiledIntoOneFolder_KeepEachOthersAreas()
    {
        var directory = Path.Combine(Path.GetTempPath(), "malda_runtime_areas", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var game = Path.Combine(directory, "game.malda");
            var core = Path.Combine(directory, "core.malda");
            File.WriteAllText(game, "game.createCanvas(10, 10);\n");
            File.WriteAllText(core, "io.print(\"core\");\n");
            var runtimePath = Path.Combine(directory, "malda-js-runtime.js");

            Assert.True(new Compiler.Compiler().CompileToJavaScript(core, Path.Combine(directory, "core.js")).Success);
            Assert.Equal(new HashSet<string>(), JsRuntimeAreas.KeptBy(runtimePath));

            Assert.True(new Compiler.Compiler().CompileToJavaScript(game, Path.Combine(directory, "game.js")).Success);
            Assert.Equal(new HashSet<string> { "game" }, JsRuntimeAreas.KeptBy(runtimePath));

            // Compiling the core program again does not drop the game area the other one needs.
            Assert.True(new Compiler.Compiler().CompileToJavaScript(core, Path.Combine(directory, "core.js")).Success);
            Assert.Equal(new HashSet<string> { "game" }, JsRuntimeAreas.KeptBy(runtimePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void KeptBy_AFullRuntimeWithoutHeader_KeepsEveryArea()
    {
        var path = Path.Combine(Path.GetTempPath(), "malda_runtime_full_" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(path, "(function (global) {})(globalThis);\n");
        try
        {
            Assert.Equal(JsRuntimeAreas.All.ToHashSet(), JsRuntimeAreas.KeptBy(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string[] RunWithRuntime(string runtime, string script)
    {
        InterpretJsPair.SkipIfJavaScriptUnavailable();
        var directory = Path.Combine(Path.GetTempPath(), "malda_runtime_areas", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "malda-js-runtime.js"), runtime);
            File.WriteAllText(Path.Combine(directory, "run.cjs"), "require(\"./malda-js-runtime.js\");\n" + script);
            var node = Environment.GetEnvironmentVariable("MALDA_NODE_PATH");
            var startInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(node) ? "node" : node,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory
            };
            startInfo.ArgumentList.Add("run.cjs");
            using var process = Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(30_000), "Node timed out.");
            Assert.True(process.ExitCode == 0, stderr.Result);
            return stdout.Result.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
