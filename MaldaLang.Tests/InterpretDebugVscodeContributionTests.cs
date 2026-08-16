// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Reflection;
using System.Text.Json;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Guards that vscode-malda contributes debugger type <c>malda</c> and launches
/// <c>malda debug-adapter</c> (D3). Does not require VS Code.
/// </summary>
public class InterpretDebugVscodeContributionTests
{
    private static string FindRepoRoot()
    {
        var starts = new List<string?>
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(InterpretDebugVscodeContributionTests).Assembly.Location),
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
        };

        foreach (var start in starts)
        {
            var dir = start;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "MaldaLang.sln")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        throw new InvalidOperationException(
            "Could not find MaldaLang.sln walking up from AppContext.BaseDirectory or the test assembly location.");
    }

    [Fact]
    public void PackageJson_ContributesMaldaDebuggerAndCliPath()
    {
        var packagePath = Path.Combine(FindRepoRoot(), "vscode-malda", "package.json");
        Assert.True(File.Exists(packagePath), $"Missing {packagePath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(packagePath));
        var root = doc.RootElement;
        Assert.Equal("./src/extension.js", root.GetProperty("main").GetString());

        var contributes = root.GetProperty("contributes");

        var debugger0 = contributes.GetProperty("debuggers")[0];
        Assert.Equal("malda", debugger0.GetProperty("type").GetString());

        var languages = debugger0.GetProperty("languages")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        Assert.Contains("malda", languages);

        var snippetProgram = debugger0
            .GetProperty("configurationSnippets")[0]
            .GetProperty("body")
            .GetProperty("program")
            .GetString();
        Assert.Equal("${file}", snippetProgram);

        var properties = contributes.GetProperty("configuration").GetProperty("properties");
        Assert.True(
            properties.TryGetProperty("malda.cli.path", out _),
            "contributes.configuration.properties must include malda.cli.path");

        var commands = contributes.GetProperty("commands")
            .EnumerateArray()
            .Select(e => e.GetProperty("command").GetString())
            .ToList();
        Assert.Contains("malda.runFile", commands);
    }

    [Fact]
    public void ExtensionJs_RegistersDebugAdapterExecutable()
    {
        var jsPath = Path.Combine(FindRepoRoot(), "vscode-malda", "src", "extension.js");
        Assert.True(File.Exists(jsPath), $"Missing {jsPath}");

        var text = File.ReadAllText(jsPath);
        Assert.Contains("debug-adapter", text, StringComparison.Ordinal);
        Assert.Contains("DebugAdapterExecutable", text, StringComparison.Ordinal);
        Assert.Contains("malda.runFile", text, StringComparison.Ordinal);
        Assert.Contains("ProcessExecution", text, StringComparison.Ordinal);
    }
}
