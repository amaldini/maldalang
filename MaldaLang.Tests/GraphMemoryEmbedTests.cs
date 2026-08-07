// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using MaldaLang.BuiltIns;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class GraphMemoryEmbedTests : TestBase
{
    public GraphMemoryEmbedTests()
    {
        EmbeddedFolderStore.ResetForTests();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            EmbeddedFolderStore.ResetForTests();
        base.Dispose(disposing);
    }

    [Fact]
    public void GraphMemoryEmbed_Load_And_Query_From_Embed_Path()
    {
        var tempDir = CreateTempDirectory("gm_embed_");
        var diskBase = Path.Combine(tempDir, "brain_memory").Replace('\\', '/');
        try
        {
            RunProgram($@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.remember(""Embeddable zebra fact unique 42"");
                memory.remember(""Unrelated cooking pasta recipes"");
                memory.save(""{diskBase}"");
            ");

            var graphPath = diskBase + ".graph.json";
            var metadataPath = diskBase + ".metadata.json";
            var vectordbPath = diskBase + ".vectordb.bin";
            Assert.True(File.Exists(graphPath));
            Assert.True(File.Exists(metadataPath));
            Assert.True(File.Exists(vectordbPath));

            EmbeddedFolderStore.RegisterForTests("gm_brain", new Dictionary<string, byte[]>
            {
                ["brain_memory.graph.json"] = File.ReadAllBytes(graphPath),
                ["brain_memory.metadata.json"] = File.ReadAllBytes(metadataPath),
                ["brain_memory.vectordb.bin"] = File.ReadAllBytes(vectordbPath)
            });

            var output = RunProgram(@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.load(""embed:gm_brain/brain_memory"");
                var s = memory.stats();
                print(""nodes="" + string(s.nodes));
                var results = memory.query(""zebra fact unique 42"", 5, {
                    ""hybridLexical"": true,
                    ""lexicalMode"": ""bm25"",
                    ""minScore"": 0,
                    ""explain"": true
                });
                print(""hits="" + string(results.length));
                if (results.length > 0) {
                    print(string(results[0].fact));
                    print(""vec="" + string(results[0].explain.vectorScore));
                    print(""lex="" + string(results[0].explain.lexicalScore));
                }
                var diag = memory.getLastQueryDiagnostics();
                print(""vecCandidates="" + string(diag.vectorCandidates));
                print(""embedReady="" + string(diag.embedReady));
            ");

            Assert.Contains("nodes=2", output);
            Assert.Contains("zebra fact unique 42", output);
            Assert.True(
                output.Contains("hits=1", StringComparison.Ordinal) ||
                output.Contains("hits=2", StringComparison.Ordinal),
                "Expected hybrid hits after embed load, got:\n" + output);
            Assert.Contains("embedReady=true", output);
            Assert.DoesNotContain("vecCandidates=0", output);
            Assert.DoesNotContain("vec=0\n", output);
            Assert.DoesNotContain("vec=0\r", output);
            Assert.False(
                System.Text.RegularExpressions.Regex.IsMatch(output, @"vec=0(?:\.0+)?(?:\s|$)"),
                "Expected non-zero vectorScore after embed load, got:\n" + output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void GraphMemoryEmbed_Save_Rejects_Embed_Path()
    {
        EmbeddedFolderStore.RegisterForTests("gm_brain", new Dictionary<string, string>
        {
            ["placeholder.txt"] = "x"
        });

        var ex = Assert.ThrowsAny<Exception>(() => RunProgram(@"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""should not persist to embed"");
            memory.save(""embed:gm_brain/brain_memory"");
        "));

        Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
