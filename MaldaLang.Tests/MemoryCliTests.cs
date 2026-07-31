// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using System.Reflection;
using System.Text.Json;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class MemoryCliTests : TestBase
{
    private static MethodInfo GetMemoryCommand()
    {
        var programType = typeof(Lexer).Assembly.GetType("MaldaLang.Program");
        Assert.NotNull(programType);
        var method = programType!.GetMethod("MemoryCommand", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!;
    }

    [Fact]
    public void MemoryCli_Stats_Json_OnEmptyMemory()
    {
        var tempDir = CreateTempDirectory("memory_cli_stats_");
        var basePath = Path.Combine(tempDir, "assistant").Replace('\\', '/');
        try
        {
            var memoryCommand = GetMemoryCommand();
            lock (_consoleLock)
            {
                var originalOut = Console.Out;
                var originalErr = Console.Error;
                using var output = new StringWriter();
                using var error = new StringWriter();
                Console.SetOut(output);
                Console.SetError(error);
                try
                {
                    memoryCommand.Invoke(null, new object[] { new[] { "memory", "stats", "--path", basePath, "--json" } });
                    using var json = JsonDocument.Parse(output.ToString());
                    Assert.Equal(0, json.RootElement.GetProperty("nodes").GetInt32());
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalErr);
                }
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void MemoryCli_Reindex_Then_ExportBundle_WritesArtifacts()
    {
        var tempDir = CreateTempDirectory("memory_cli_reindex_");
        var kbDir = Path.Combine(tempDir, "kb");
        Directory.CreateDirectory(kbDir);
        File.WriteAllText(Path.Combine(kbDir, "note.md"), "GraphMemory CLI document CLI_TAG_123");
        var basePath = Path.Combine(tempDir, "assistant").Replace('\\', '/');
        var bundleOut = Path.Combine(tempDir, "bundle").Replace('\\', '/');

        try
        {
            var memoryCommand = GetMemoryCommand();
            lock (_consoleLock)
            {
                var originalOut = Console.Out;
                var originalErr = Console.Error;
                using var output = new StringWriter();
                using var error = new StringWriter();
                Console.SetOut(output);
                Console.SetError(error);
                try
                {
                    memoryCommand.Invoke(null, new object[] { new[] { "memory", "reindex", "--path", basePath, "--dir", kbDir, "--pattern", "*.md", "--json" } });
                    output.GetStringBuilder().Clear();
                    memoryCommand.Invoke(null, new object[] { new[] { "memory", "stats", "--path", basePath, "--json" } });
                    using var stats = JsonDocument.Parse(output.ToString());
                    Assert.True(stats.RootElement.GetProperty("nodes").GetInt32() >= 1);
                    output.GetStringBuilder().Clear();
                    memoryCommand.Invoke(null, new object[] { new[] { "memory", "export-bundle", "--path", basePath, "-o", bundleOut } });
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalErr);
                }
            }

            Assert.True(File.Exists(bundleOut + ".bundle.json"));
            Assert.True(File.Exists(bundleOut + ".graph.json"));
            Assert.True(File.Exists(bundleOut + ".metadata.json"));
            Assert.True(File.Exists(bundleOut + ".vectordb.bin"));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
