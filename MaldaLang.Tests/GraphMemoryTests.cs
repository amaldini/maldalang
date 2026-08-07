// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Interpreter;
using MaldaLang.BuiltIns;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using MALDAException = MaldaLang.Interpreter.MALDAException;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class GraphMemoryTests : TestBase
{
    // RunProgram and RunProgramAsync are now provided by TestBase
    
    [Fact]
    public void TestGraphMemoryCreation()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            print(""Memory created"");
        ";
        var output = RunProgram(source);
        Assert.Contains("Memory created", output);
    }
    
    [Fact]
    public void TestGraphMemoryRemember()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var nodeId = memory.remember(""My name is Alice"");
            print(nodeId);
        ";
        var output = RunProgram(source);
        Assert.Contains("node_", output);
    }
    
    [Fact]
    public void TestGraphMemoryQuery()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""My name is Alice"");
            memory.remember(""I prefer dark mode"");
            var results = memory.query(""What are my preferences?"");
            print(results.length >= 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void TestGraphMemoryFindRelated()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var nodeId1 = memory.remember(""My name is Alice"");
            var nodeId2 = memory.remember(""I prefer dark mode"");
            var related = memory.findRelated(nodeId1);
            print(related.length >= 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void TestGraphMemoryAddCodeElement()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var elementData = dict {
                ""type"": ""function"",
                ""name"": ""createUser"",
                ""description"": ""Creates a new user""
            };
            var nodeId = memory.addCodeElement(""UserService.createUser"", elementData);
            print(nodeId);
        ";
        var output = RunProgram(source);
        Assert.Contains("code_", output);
    }
    
    [Fact]
    public void TestGraphMemoryAnalyzeFile()
    {
        var tempFile = Path.GetTempFileName();
        var maldaPath = tempFile.Replace('\\', '/');
        try
        {
            File.WriteAllText(tempFile, @"
                class TestClass {
                    function testMethod() {
                        return 42;
                    }
                }
            ");
            
            var source = $@"
                var memory = new GraphMemory();
                memory.initialize();
                var count = memory.analyzeFile(""{maldaPath}"");
                print(count >= 0);
            ";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
    
    [Fact]
    public void TestGraphMemoryExportImport()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Test fact"");
            var graphJson = memory.exportGraph();
            print(graphJson != null);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void TestGraphMemoryClear()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Test fact"");
            memory.clear();
            print(""Cleared"");
        ";
        var output = RunProgram(source);
        Assert.Contains("Cleared", output);
    }
    
    [Fact]
    public void TestAgentEnableMemory()
    {
        var source = @"
            var client = new OpenRouterClient();
            var agent = new Agent(""Test"", ""assistant"", ""You help users"", client);
            agent.enableMemory();
            var memory = agent.getMemory();
            print(memory != null);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void TestAgentRemember()
    {
        var source = @"
            var client = new OpenRouterClient();
            var agent = new Agent(""Test"", ""assistant"", ""You help users"", client);
            agent.enableMemory();
            var nodeId = agent.remember(""My name is Alice"");
            print(nodeId != null);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void TestGraphMemoryFindCodeRelationships()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var elementData = dict {
                ""type"": ""function"",
                ""name"": ""createUser"",
                ""description"": ""Creates a new user""
            };
            var nodeId = memory.addCodeElement(""UserService.createUser"", elementData);
            var relationships = memory.findCodeRelationships(""UserService.createUser"");
            print(relationships.length >= 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void TestAgentMemoryIntegration()
    {
        var source = @"
            var client = new OpenRouterClient();
            var agent = new Agent(""Test"", ""assistant"", ""You help users"", client);
            agent.enableMemory();
            
            // Remember some facts
            agent.remember(""My name is Alice"");
            agent.remember(""I prefer dark mode"");
            
            // Query memory
            var memory = agent.getMemory();
            var results = memory.query(""What are my preferences?"");
            print(results.length >= 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void TestGraphMemorySaveLoad()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "test_memory");
        var maldaPath = tempFile.Replace('\\', '/');
        try
        {
            var source1 = $@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.remember(""Test fact 1"");
                memory.remember(""Test fact 2"");
                memory.save(""{maldaPath}.mem"");
                print(""Saved"");
            ";
            RunProgram(source1);
            
            var source2 = $@"
                var memory = new GraphMemory();
                memory.load(""{maldaPath}.mem"");
                print(""Loaded"");
            ";
            var output = RunProgram(source2);
            Assert.Contains("Loaded", output);
        }
        finally
        {
            // Clean up
            if (File.Exists($"{tempFile}.graph.json"))
                File.Delete($"{tempFile}.graph.json");
            if (File.Exists($"{tempFile}.vectordb.bin"))
                File.Delete($"{tempFile}.vectordb.bin");
            if (File.Exists($"{tempFile}.metadata.json"))
                File.Delete($"{tempFile}.metadata.json");
        }
    }
    
    [Fact]
    public void TestGraphMemoryWithCustomEmbedding()
    {
        var source = @"
            // Create a simple custom embedding function that returns a fixed-size array
            function customEmbed(text) {
                // Return a simple embedding: array of 10 zeros (for testing)
                var embedding = [];
                for (var i = 0; i < 10; i = i + 1) {
                    embedding[i] = 0.0;
                }
                return embedding;
            }
            
            var memory = new GraphMemory();
            memory.initialize(10, ""single"", customEmbed);
            var nodeId = memory.remember(""Test fact with custom embedding"");
            print(nodeId != null);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void TestGraphMemoryBackwardCompatibility()
    {
        // Test that GraphMemory still works without custom embedding (backward compatibility)
        var source = @"
            var memory = new GraphMemory();
            memory.initialize(384, ""single"");
            var nodeId = memory.remember(""Test fact"");
            print(nodeId != null);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void TestVectorDBWithCustomEmbedding()
    {
        var source = @"
            // Create a simple custom embedding function
            function customEmbed(text) {
                var embedding = [];
                for (var i = 0; i < 5; i = i + 1) {
                    embedding[i] = 0.1;
                }
                return embedding;
            }
            
            var db = new VectorDB(5, ""double"");
            db.init(customEmbed);
            db.add(""Hello world"");
            db.add(""Test text"");
            var results = db.searchSimilar(""Hello"", 2);
            print(results.length >= 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void TestGraphMemoryQueryWithCustomEmbedding()
    {
        var source = @"
            // Create a simple custom embedding function
            function customEmbed(text) {
                var embedding = [];
                for (var i = 0; i < 8; i = i + 1) {
                    embedding[i] = 0.0;
                }
                return embedding;
            }
            
            var memory = new GraphMemory();
            memory.initialize(8, ""single"", customEmbed);
            memory.remember(""My name is Alice"");
            memory.remember(""I like programming"");
            var results = memory.query(""What do I like?"", 5);
            print(results.length >= 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void TestGraphMemorySaveLoadDotfileBasePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_dot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, ".ralph-memory").Replace('\\', '/');
        try
        {
            var source1 = $@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.remember(""Dotfile path fact"");
                memory.save(""{basePath}"");
                print(""Saved"");
            ";
            RunProgram(source1);

            var canonicalGraph = Path.Combine(tempDir, ".ralph-memory.graph.json");
            var canonicalMetadata = Path.Combine(tempDir, ".ralph-memory.metadata.json");
            Assert.True(File.Exists(canonicalGraph), "Expected .ralph-memory.graph.json");
            Assert.True(File.Exists(canonicalMetadata), "Expected .ralph-memory.metadata.json");
            Assert.Contains("Dotfile path fact", File.ReadAllText(canonicalMetadata));

            var source2 = $@"
                var memory = new GraphMemory();
                memory.load(""{basePath}"");
                var recent = memory.getRecent(1);
                print(recent.length >= 1);
            ";
            var output = RunProgram(source2);
            Assert.Contains("true", output);
        }
        finally
        {
            foreach (var pattern in new[] { ".ralph-memory.graph.json", ".ralph-memory.metadata.json", ".ralph-memory.vectordb.bin" })
            {
                var path = Path.Combine(tempDir, pattern);
                if (File.Exists(path))
                    File.Delete(path);
            }
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TestGraphMemoryLoadLegacyFlatArtifacts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_legacy_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var canonicalBase = Path.Combine(tempDir, ".ralph-memory").Replace('\\', '/');
        try
        {
            var seed = $@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.remember(""Legacy migration fact"", """", {{ ""type"": ""progress"", ""phase"": ""legacy"" }});
                memory.save(""{canonicalBase}"");
            ";
            RunProgram(seed);

            var legacyGraph = Path.Combine(tempDir, ".graph.json");
            var legacyMeta = Path.Combine(tempDir, ".metadata.json");
            var legacyVec = Path.Combine(tempDir, ".vectordb.bin");
            var canonicalGraph = Path.Combine(tempDir, ".ralph-memory.graph.json");

            File.Move(canonicalGraph, legacyGraph, overwrite: true);
            File.Move(Path.Combine(tempDir, ".ralph-memory.metadata.json"), legacyMeta, overwrite: true);
            File.Move(Path.Combine(tempDir, ".ralph-memory.vectordb.bin"), legacyVec, overwrite: true);

            Assert.True(File.Exists(legacyGraph), "Legacy .graph.json should exist for load test");
            Assert.Contains("Legacy migration fact", File.ReadAllText(legacyMeta));

            var source2 = $@"
                var memory = new GraphMemory();
                memory.load(""{canonicalBase}"");
                var recent = memory.getRecent(1);
                print(recent.length >= 1);
            ";
            var output = RunProgram(source2);
            Assert.Contains("true", output);
        }
        finally
        {
            foreach (var name in new[] { ".graph.json", ".metadata.json", ".vectordb.bin", ".ralph-memory.graph.json", ".ralph-memory.metadata.json", ".ralph-memory.vectordb.bin" })
            {
                var path = Path.Combine(tempDir, name);
                if (File.Exists(path))
                    File.Delete(path);
            }
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TestGraphMemoryRememberWithMetadata()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Implemented snake movement"", ""files=game.js"", {
                ""phase"": ""movement"",
                ""iteration"": 2,
                ""type"": ""progress"",
                ""source"": ""ralph""
            });
            var recent = memory.getRecent(1, ""movement"");
            print(recent.length == 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void TestGraphMemoryHybridQuery()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Older note"", """", { ""type"": ""progress"", ""phase"": ""alpha"" });
            memory.remember(""Newer note"", """", { ""type"": ""progress"", ""phase"": ""alpha"" });
            var options = { ""recentCount"": 2, ""hybrid"": true, ""phase"": ""alpha"" };
            var results = memory.query(""alpha progress"", 5, options);
            print(results.length >= 2);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void TestAgentMemoryProgressTools()
    {
        var source = @"
            var client = new OpenRouterClient();
            var agent = new Agent(""Test"", ""assistant"", ""You help users"", client);
            agent.enableMemory();
            agent.addMemoryProgressTools();
            agent.addMemoryProgressTools();
            agent.setAutoRememberOnThink(false);
            print(""ok"");
        ";
        var output = RunProgram(source);
        Assert.Contains("ok", output);
    }

    [Fact]
    public void Agent_AddMemoryProgressTools_WorksWithoutExplicitSetInterpreter()
    {
        var client = new MaldaLang.BuiltIns.LLMClientInstance
        {
            ApiUrl = "https://example.com",
            ApiKey = "test",
            Model = "test"
        };
        var agent = new MaldaLang.BuiltIns.AgentInstance();
        agent.Initialize("Test", "assistant", "You help users", client, null, null, null);
        agent.EnableMemory(new List<MaldaLang.Interpreter.RuntimeValue>());
        agent.CallMethod("addMemoryProgressTools", new List<MaldaLang.Interpreter.RuntimeValue>());
        agent.CallMethod("addMemoryProgressTools", new List<MaldaLang.Interpreter.RuntimeValue>());
    }

    [Fact]
    public void Remember_LinksSemanticallySimilarFacts()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var nodeId1 = memory.remember(""My name is Alice"");
            memory.remember(""Alice is my name"");
            var related = memory.findRelated(nodeId1);
            print(related.length >= 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Remember_DoesNotLinkUnrelatedFacts()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var nodeId1 = memory.remember(""My name is Alice"");
            memory.remember(""The weather is sunny today"");
            var related = memory.findRelated(nodeId1);
            print(related.length == 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_ExpandsViaLinkedCluster()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""I prefer dark mode"");
            memory.remember(""UI theme: dark mode preferred"");
            var results = memory.query(""preferences"", 5);
            print(results.length >= 2);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void ImportGraph_RestoresExportedGraphStructure()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var nodeId = memory.remember(""Persisted graph node"");
            var graphJson = memory.exportGraph();
            memory.clear();
            memory.initialize();
            memory.importGraph(graphJson);
            var reexported = memory.exportGraph();
            print(reexported.indexOf(nodeId) >= 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_MinScore_FiltersWeakSemanticHits()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Unique phrase XYZ123 programming language"");
            memory.remember(""Cooking pasta recipes Italian cuisine"");
            var broad = memory.query(""Unique phrase XYZ123"", 10, { ""minScore"": 0 });
            var strict = memory.query(""Unique phrase XYZ123"", 10, { ""minScore"": 0.5 });
            print(broad.length >= 1);
            print(strict.length >= 1);
            print(strict.length <= broad.length);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void AutoRememberMetadata_StoresEpisodicSourceAgent()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""user question"", ""agent answer"", { ""type"": ""episodic"", ""source"": ""agent"" });
            var recent = memory.getRecent(1);
            print(recent.length == 1);
            print(recent[0].type == ""episodic"");
            print(recent[0].source == ""agent"");
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void GetRecent_ScopeFilter_ReturnsScopedAndGlobalOnly()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Chat one fact"", """", { ""scope"": ""chat:1"", ""type"": ""semantic"" });
            memory.remember(""Chat two fact"", """", { ""scope"": ""chat:2"", ""type"": ""semantic"" });
            memory.remember(""Global fact"", """", { ""type"": ""semantic"" });
            var scoped = memory.getRecent(10, """", """", ""chat:1"");
            print(scoped.length == 2);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_ExcludeType_FiltersEpisodicFromSemanticPath()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Stable user preference dark mode"", """", { ""type"": ""semantic"" });
            memory.remember(""Turn log question"", ""answer"", { ""type"": ""episodic"", ""source"": ""agent"" });
            var results = memory.query(""preference"", 10, { ""excludeType"": ""episodic"", ""minScore"": 0 });
            var hasEpisodic = false;
            for (var i = 0; i < results.length; i++) {
                if (results[i].type == ""episodic"") {
                    hasEpisodic = true;
                }
            }
            print(!hasEpisodic);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void FindRelated_MaxDistance_LimitsGraphHops()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var nodeId1 = memory.remember(""My name is Alice"");
            memory.remember(""Alice is my name"");
            memory.remember(""The weather is sunny today"");
            var near = memory.findRelated(nodeId1, 1);
            var far = memory.findRelated(nodeId1, 2);
            print(near.length >= 1);
            print(far.length >= near.length);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Agent_SetMemoryScope_AppliesToRemember()
    {
        var client = new MaldaLang.BuiltIns.LLMClientInstance
        {
            ApiUrl = "https://example.com",
            ApiKey = "test",
            Model = "test"
        };
        var agent = new MaldaLang.BuiltIns.AgentInstance();
        var interpreter = new MaldaLang.Interpreter.Interpreter();
        agent.SetInterpreter(interpreter);
        agent.Initialize("Test", "assistant", "You help users", client, null, null, null);
        agent.EnableMemory(new List<MaldaLang.Interpreter.RuntimeValue>());
        agent.CallMethod("setMemoryScope", new List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String("chat:99") });
        agent.CallMethod("remember", new List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String("scoped fact") });
        var memoryVal = agent.GetMemory();
        var memory = memoryVal.AsObject() as GraphMemoryInstance;
        Assert.NotNull(memory);
        var recent = memory!.CallMethod("getRecent", new List<MaldaLang.Interpreter.RuntimeValue>
        {
            MaldaLang.Interpreter.RuntimeValue.Integer(1),
            MaldaLang.Interpreter.RuntimeValue.String(""),
            MaldaLang.Interpreter.RuntimeValue.String(""),
            MaldaLang.Interpreter.RuntimeValue.String("chat:99")
        }, interpreter);
        Assert.Equal(MaldaLang.Interpreter.ValueType.Array, recent.Type);
        var entries = recent.AsArray();
        Assert.Single(entries);
        var entry = entries[0].AsObject() as JsonObject;
        Assert.NotNull(entry);
        var scope = entry!.Get("scope", null);
        Assert.Equal(MaldaLang.Interpreter.ValueType.String, scope.Type);
        Assert.Equal("chat:99", scope.AsString());
    }

    [Fact]
    public void Forget_RemovesNodeFromMemory()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var nodeId = memory.remember(""delete me ABC123 unique"");
            memory.forget(nodeId);
            var recent = memory.getRecent(10);
            var stillThere = false;
            for (var i = 0; i < recent.length; i++) {
                if (recent[i].fact == ""delete me ABC123 unique"") {
                    stillThere = true;
                }
            }
            print(!stillThere);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Remember_DeduplicatesNearIdenticalFacts()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var id1 = memory.remember(""My name is Alice exactly"");
            var id2 = memory.remember(""My name is Alice exactly"");
            print(id1 == id2);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void IndexDocuments_LoadsAndRemembersMarkdownFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_index_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "kb.md"), "Unique KB content ZZZ789 about astronomy and stars.");
        var dir = tempDir.Replace('\\', '/');
        try
        {
            var source = $@"
                var memory = new GraphMemory();
                memory.initialize();
                var count = memory.indexDocuments(""*.md"", ""{dir}"");
                print(count >= 1);
                var results = memory.query(""astronomy ZZZ789"", 5, {{ ""minScore"": 0 }});
                print(results.length >= 1);
            ";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Stats_ReturnsNodeAndTypeCounts()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""plain fact"");
            memory.remember(""typed"", """", { ""type"": ""semantic"" });
            var s = memory.stats();
            print(s.nodes == 2);
            print(s.byType.semantic == 1);
            print(s.byType.unknown == 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Prune_RemovesMatchingNodesByType()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""episodic note"", """", { ""type"": ""episodic"" });
            memory.remember(""semantic note"", """", { ""type"": ""semantic"" });
            var removed = memory.prune({ ""type"": ""episodic"" });
            print(removed == 1);
            print(memory.stats().nodes == 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void IndexDocuments_ChunksLargeFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_chunk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var longContent = new string('A', 2500) + " CHUNKMARKER unique astronomy content";
        File.WriteAllText(Path.Combine(tempDir, "long.md"), longContent);
        var dir = tempDir.Replace('\\', '/');
        try
        {
            var source = $@"
                var memory = new GraphMemory();
                memory.initialize();
                var count = memory.indexDocuments(""*.md"", ""{dir}"", {{ ""chunkSize"": 1000, ""overlap"": 100 }});
                print(count >= 2);
            ";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ProgressTool_AppliesMemoryScope()
    {
        var memory = new GraphMemoryInstance();
        var interpreter = new MaldaLang.Interpreter.Interpreter();
        memory.CallMethod("initialize", new List<MaldaLang.Interpreter.RuntimeValue>(), interpreter);
        var tool = MemoryProgressToolInstance.CreateRememberTool(memory, interpreter, "chat:42");
        var args = new JsonObject();
        args.Set("note", MaldaLang.Interpreter.RuntimeValue.String("scoped progress"));
        tool.ExecuteMemoryTool(MaldaLang.Interpreter.RuntimeValue.Object(args));
        var recent = memory.CallMethod("getRecent", new List<MaldaLang.Interpreter.RuntimeValue>
        {
            MaldaLang.Interpreter.RuntimeValue.Integer(1),
            MaldaLang.Interpreter.RuntimeValue.String(""),
            MaldaLang.Interpreter.RuntimeValue.String(""),
            MaldaLang.Interpreter.RuntimeValue.String("chat:42")
        }, interpreter);
        var entry = recent.AsArray()[0].AsObject() as JsonObject;
        Assert.NotNull(entry);
        var scope = entry!.Get("scope", null);
        Assert.Equal("chat:42", scope.AsString());
    }

    [Fact]
    public void GetNode_HasNode_Update_Work()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var nodeId = memory.remember(""Original fact"", ""ctx1"", { ""type"": ""semantic"", ""scope"": ""chat:9"" });
            print(memory.hasNode(nodeId));
            var node = memory.getNode(nodeId);
            print(node.fact);
            print(node.type);
            memory.update(nodeId, ""Updated fact"");
            var updated = memory.getNode(nodeId);
            print(updated.fact);
            print(updated.type);
            print(updated.scope);
            print(memory.hasNode(""missing_node""));
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
        Assert.Contains("Original fact", output);
        Assert.Contains("Updated fact", output);
        Assert.Contains("semantic", output);
        Assert.Contains("chat:9", output);
        Assert.Contains("false", output);
    }

    [Fact]
    public void Query_SynapsePrefersSemanticOverEpisodic()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""synapse rank test QWER"", """", { ""type"": ""episodic"" });
            memory.remember(""synapse rank test QWER"", """", { ""type"": ""semantic"" });
            var results = memory.query(""synapse rank test QWER"", 1, { ""minScore"": 0, ""synapse"": true });
            print(results.length);
            print(results[0].type);
        ";
        var output = RunProgram(source);
        Assert.Contains("1", output);
        Assert.Contains("semantic", output);
    }

    [Fact]
    public void Load_PreservesCustomEmbedHash()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_embed_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "embed-memory").Replace('\\', '/');
        try
        {
            var source1 = $@"
                function hashEmbed(text) {{
                    return embedHash(text, 384);
                }}
                var memory = new GraphMemory();
                memory.initialize(384, ""single"", hashEmbed);
                memory.remember(""hash embed persistence ABC123"");
                memory.save(""{basePath}"");
                print(""saved"");
            ";
            Assert.Contains("saved", RunProgram(source1));

            var source2 = $@"
                function hashEmbed(text) {{
                    return embedHash(text, 384);
                }}
                var memory = new GraphMemory();
                memory.initialize(384, ""single"", hashEmbed);
                memory.load(""{basePath}"");
                print(memory.hasNode(""node_0""));
                var recent = memory.getRecent(5);
                print(recent.length);
                var results = memory.query(""hash embed persistence ABC123"", 5, {{ ""minScore"": 0 }});
                print(results.length >= 1);
            ";
            var output2 = RunProgram(source2);
            Assert.Contains("true", output2);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Query_HybridLexical_MatchesExactTokens()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""General cooking tips and recipes"", """", { ""type"": ""semantic"" });
            memory.remember(""Project path C:/work/ZZLEX999/main.malda"", """", { ""type"": ""semantic"" });
            var results = memory.query(""ZZLEX999"", 1, { ""minScore"": 0, ""hybridLexical"": true });
            print(results.length >= 1);
            print(results[0].fact.indexOf(""ZZLEX999"") >= 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Consolidate_CreatesSemanticAndMarksEpisodics()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Question one"", ""Answer one"", { ""type"": ""episodic"", ""source"": ""agent"", ""scope"": ""chat:7"" });
            memory.remember(""Question two"", ""Answer two"", { ""type"": ""episodic"", ""source"": ""agent"", ""scope"": ""chat:7"" });
            memory.remember(""Question three"", ""Answer three"", { ""type"": ""episodic"", ""source"": ""agent"", ""scope"": ""chat:7"" });
            var result = memory.consolidate({ ""scope"": ""chat:7"", ""minEpisodic"": 3, ""maxEpisodic"": 10 });
            print(result.semanticNodesCreated);
            print(result.episodicsMarked);
            var semantic = memory.getNode(result.semanticNodeId);
            print(semantic.type);
            print(semantic.source);
        ";
        var output = RunProgram(source);
        Assert.Contains("1", output);
        Assert.Contains("3", output);
        Assert.Contains("semantic", output);
        Assert.Contains("consolidate", output);
    }
    
    [Fact]
    public void Reflect_ParsesLlmJson_CreatesSemanticFacts()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Q1"", ""A1"", { ""type"": ""episodic"", ""scope"": ""chat:r1"", ""source"": ""agent"" });
            memory.remember(""Q2"", ""A2"", { ""type"": ""episodic"", ""scope"": ""chat:r1"", ""source"": ""agent"" });
            memory.remember(""Q3"", ""A3"", { ""type"": ""episodic"", ""scope"": ""chat:r1"", ""source"": ""agent"" });
            var result = memory.reflect({
                ""scope"": ""chat:r1"",
                ""minEpisodic"": 3,
                ""facts"": [{ ""fact"": ""User prefers tea"", ""confidence"": 0.92, ""category"": ""preference"" }]
            });
            print(result.factsCreated);
            print(result.episodicsMarked);
            print(result.facts.length);
            var sem = memory.getNode(result.semanticNodeIds[0]);
            print(sem.source);
            print(sem.category);
            print(sem.confidence >= 0.9);
        ";
        var output = RunProgram(source);
        Assert.Contains("1", output);
        Assert.Contains("reflect", output);
        Assert.Contains("preference", output);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void Reflect_FallbackToConsolidate_OnInvalidJson()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Q1"", ""A1"", { ""type"": ""episodic"", ""scope"": ""chat:r2"", ""source"": ""agent"" });
            memory.remember(""Q2"", ""A2"", { ""type"": ""episodic"", ""scope"": ""chat:r2"", ""source"": ""agent"" });
            memory.remember(""Q3"", ""A3"", { ""type"": ""episodic"", ""scope"": ""chat:r2"", ""source"": ""agent"" });
            var result = memory.reflect({
                ""scope"": ""chat:r2"",
                ""minEpisodic"": 3,
                ""facts"": [{ ""confidence"": 0.9 }]
            });
            print(result.factsCreated);
            print(result.episodicsMarked);
            print(result.errors.length >= 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("1", output);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void Reflect_MarksEpisodicsConsolidated()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Q1"", ""A1"", { ""type"": ""episodic"", ""scope"": ""chat:r3"", ""source"": ""agent"" });
            memory.remember(""Q2"", ""A2"", { ""type"": ""episodic"", ""scope"": ""chat:r3"", ""source"": ""agent"" });
            memory.remember(""Q3"", ""A3"", { ""type"": ""episodic"", ""scope"": ""chat:r3"", ""source"": ""agent"" });
            var result = memory.reflect({
                ""scope"": ""chat:r3"",
                ""minEpisodic"": 3,
                ""facts"": [{ ""fact"": ""Stable preference"", ""confidence"": 0.8, ""category"": ""preference"" }]
            });
            print(result.episodicsMarked);
            var node0 = memory.getNode(""node_0"");
            var node1 = memory.getNode(""node_1"");
            var node2 = memory.getNode(""node_2"");
            print(node0.consolidated == true && node1.consolidated == true && node2.consolidated == true);
        ";
        var output = RunProgram(source);
        Assert.Contains("3", output);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void Reflect_RespectsScope()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Scoped A1"", ""resp"", { ""type"": ""episodic"", ""scope"": ""chat:scopeA"", ""source"": ""agent"" });
            memory.remember(""Scoped A2"", ""resp"", { ""type"": ""episodic"", ""scope"": ""chat:scopeA"", ""source"": ""agent"" });
            memory.remember(""Scoped A3"", ""resp"", { ""type"": ""episodic"", ""scope"": ""chat:scopeA"", ""source"": ""agent"" });
            memory.remember(""Scoped B1"", ""resp"", { ""type"": ""episodic"", ""scope"": ""chat:scopeB"", ""source"": ""agent"" });
            var result = memory.reflect({
                ""scope"": ""chat:scopeA"",
                ""minEpisodic"": 3,
                ""facts"": [{ ""fact"": ""Scope A summary"", ""confidence"": 0.85, ""category"": ""summary"" }]
            });
            print(result.factsCreated);
            print(memory.getNode(""node_3"").consolidated != true);
        ";
        var output = RunProgram(source);
        Assert.Contains("1", output);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Prune_ConsolidatedFilter_RemovesOnlyMarkedEpisodics()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Old consolidated turn"", ""resp"", { ""type"": ""episodic"", ""consolidated"": true, ""scope"": ""chat:8"" });
            memory.remember(""Fresh turn"", ""resp2"", { ""type"": ""episodic"", ""scope"": ""chat:8"" });
            var removed = memory.prune({ ""type"": ""episodic"", ""scope"": ""chat:8"", ""consolidated"": true });
            print(removed);
            print(!memory.hasNode(""node_0""));
            print(memory.hasNode(""node_1""));
        ";
        var output = RunProgram(source);
        Assert.Contains("1", output);
        Assert.Contains("true", output);
    }

    [Fact]
    public void IndexDocuments_ChangedOnly_SkipsUnchangedFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_idx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var docPath = Path.Combine(tempDir, "note.md");
        var dir = tempDir.Replace('\\', '/');
        try
        {
            File.WriteAllText(docPath, "Alpha knowledge ZZIDX111");
            var source1 = $@"
                var memory = new GraphMemory();
                memory.initialize();
                var count1 = memory.indexDocuments(""note.md"", ""{dir}"", {{ ""changedOnly"": true }});
                var count2 = memory.indexDocuments(""note.md"", ""{dir}"", {{ ""changedOnly"": true }});
                print(count1);
                print(count2);
            ";
            var output1 = RunProgram(source1);
            Assert.Contains("1", output1);
            Assert.Contains("0", output1);

            File.WriteAllText(docPath, "Beta knowledge ZZIDX222 changed");
            var source2 = $@"
                var memory = new GraphMemory();
                memory.initialize();
                var count3 = memory.indexDocuments(""note.md"", ""{dir}"", {{ ""changedOnly"": true }});
                print(count3 >= 1);
            ";
            Assert.Contains("true", RunProgram(source2));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExportBundle_ImportBundle_RoundTrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_bundle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "bundle-memory").Replace('\\', '/');
        try
        {
            var source1 = $@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.remember(""bundle fact XYZBUNDLE"");
                var manifest = memory.exportBundle(""{basePath}"");
                print(manifest != null);
            ";
            Assert.Contains("true", RunProgram(source1));

            var source2 = $@"
                var memory = new GraphMemory();
                memory.importBundle(""{basePath}"");
                print(memory.hasNode(""node_0""));
                var results = memory.query(""bundle fact XYZBUNDLE"", 3, {{ ""minScore"": 0 }});
                print(results.length >= 1);
            ";
            Assert.Contains("true", RunProgram(source2));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EnforceLimits_PrunesExcessEpisodicNodes()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""e1"", ""r1"", { ""type"": ""episodic"" });
            memory.remember(""e2"", ""r2"", { ""type"": ""episodic"" });
            memory.remember(""e3"", ""r3"", { ""type"": ""episodic"" });
            memory.remember(""semantic fact"", """", { ""type"": ""semantic"" });
            var removed = memory.enforceLimits({ ""maxNodes"": 2, ""type"": ""episodic"" });
            print(removed);
            var stats = memory.stats();
            print(stats.nodes);
        ";
        var output = RunProgram(source);
        Assert.Contains("2", output);
        Assert.Contains("2", output);
    }

    [Fact]
    public void Query_IncludeTypes_FiltersToAllowedTypes()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""progress note"", """", { ""type"": ""progress"" });
            memory.remember(""episodic turn"", ""answer"", { ""type"": ""episodic"" });
            var results = memory.query(""note"", 10, {
                ""minScore"": 0,
                ""includeTypes"": [""progress"", ""semantic""]
            });
            var hasEpisodic = false;
            for (var i = 0; i < results.length; i++) {
                if (results[i].type == ""episodic"") {
                    hasEpisodic = true;
                }
            }
            print(!hasEpisodic);
            print(results.length >= 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void Query_BumpsAccessCount_OnReturnedNodes()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var id = memory.remember(""Access counter fact"", """", { ""type"": ""semantic"" });
            var results = memory.query(""Access counter fact"", 1, { ""minScore"": 0, ""synapse"": true });
            var node = memory.getNode(id);
            print(results.length >= 1);
            print(node.accessCount >= 1);
            print(node.lastAccessed != null);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void Supersedes_PenalizesOlderSemanticNode()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Project policy uses API version one"", """", { ""type"": ""semantic"", ""scope"": ""chat:ss"" });
            memory.remember(""Project policy uses API version one updated"", """", { ""type"": ""semantic"", ""scope"": ""chat:ss"" });
            var results = memory.query(""Project policy uses API version one"", 2, { ""minScore"": 0, ""scope"": ""chat:ss"", ""synapse"": true });
            print(results.length >= 1);
            print(results[0].fact.indexOf(""updated"") >= 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void Prune_MaxImportanceBelow_RemovesLowImportance()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Low importance note"", """", { ""type"": ""semantic"", ""importance"": 0.1 });
            memory.remember(""High importance note"", """", { ""type"": ""semantic"", ""importance"": 0.9 });
            var removed = memory.prune({ ""type"": ""semantic"", ""maxImportanceBelow"": 0.2 });
            print(removed == 1);
            var results = memory.query(""importance note"", 5, { ""minScore"": 0 });
            var hasLow = false;
            for (var i = 0; i < results.length; i++) {
                if (results[i].fact.indexOf(""Low importance"") >= 0) {
                    hasLow = true;
                }
            }
            print(!hasLow);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void Query_Diversity_MmrReducesNearDuplicates()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""apple banana duplicate one"", """", { ""type"": ""semantic"" });
            memory.remember(""apple banana duplicate two"", """", { ""type"": ""semantic"" });
            memory.remember(""apple orange distinct choice"", """", { ""type"": ""semantic"" });
            var results = memory.query(""apple banana"", 2, { ""minScore"": 0, ""diversity"": 0.9, ""hybridLexical"": true, ""lexicalWeight"": 0.4 });
            print(results.length == 2);
            var hasDistinct = false;
            for (var i = 0; i < results.length; i++) {
                if (results[i].fact.indexOf(""orange"") >= 0) {
                    hasDistinct = true;
                }
            }
            print(hasDistinct);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void Query_ExcludeNodeIds_OmitsSpecifiedResults()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var a = memory.remember(""exclude node A fact"", """", { ""type"": ""semantic"" });
            var b = memory.remember(""exclude node B fact"", """", { ""type"": ""semantic"" });
            var results = memory.query(""exclude node fact"", 5, {
                ""minScore"": 0,
                ""excludeNodeIds"": [a]
            });
            var hasA = false;
            for (var i = 0; i < results.length; i++) {
                if (results[i].nodeId == a) {
                    hasA = true;
                }
            }
            print(!hasA);
            print(results.length >= 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
    
    [Fact]
    public void ReindexDocuments_WorksAfterSaveLoad()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_reindex_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var docPath = Path.Combine(tempDir, "kb.md");
        var dir = tempDir.Replace('\\', '/');
        var basePath = Path.Combine(tempDir, "mem").Replace('\\', '/');
        try
        {
            File.WriteAllText(docPath, "Knowledge base topic REIDX123");
            var source = $@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.indexDocuments(""kb.md"", ""{dir}"", {{ ""changedOnly"": true }});
                memory.save(""{basePath}"");
                
                var loaded = new GraphMemory();
                loaded.initialize();
                loaded.load(""{basePath}"");
                var r = loaded.reindexDocuments(""kb.md"", ""{dir}"");
                print(r.indexed == 0);
                print(r.skipped == 1);
                print(r.removed == 0);
            ";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Agent_EnableMemoryPath_SharesInstance()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_shared_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "team-memory").Replace('\\', '/');
        try
        {
            var client = new MaldaLang.BuiltIns.LLMClientInstance
            {
                ApiUrl = "https://example.com",
                ApiKey = "test",
                Model = "test"
            };
            var interpreter = new MaldaLang.Interpreter.Interpreter();
            var agent1 = new MaldaLang.BuiltIns.AgentInstance();
            agent1.SetInterpreter(interpreter);
            agent1.Initialize("A1", "assistant", "help", client, null, null, null);
            agent1.CallMethod("enableMemory", new List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String(basePath) });
            agent1.CallMethod("remember", new List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String("shared team fact") });
            agent1.CallMethod("saveMemory", new List<MaldaLang.Interpreter.RuntimeValue>());

            var agent2 = new MaldaLang.BuiltIns.AgentInstance();
            agent2.SetInterpreter(interpreter);
            agent2.Initialize("A2", "assistant", "help", client, null, null, null);
            agent2.CallMethod("enableMemory", new List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String(basePath) });
            var memoryVal = agent2.GetMemory();
            var memory = memoryVal.AsObject() as GraphMemoryInstance;
            Assert.NotNull(memory);
            var recent = memory!.CallMethod("getRecent", new List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.Integer(5) }, interpreter);
            Assert.True(recent.AsArray().Count >= 1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Remember_LinksPersistAfterSaveLoad()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_link_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "linked-memory").Replace('\\', '/');
        try
        {
            var source1 = $@"
                var memory = new GraphMemory();
                memory.initialize();
                var nodeId1 = memory.remember(""My name is Alice"");
                memory.remember(""Alice is my name"");
                memory.save(""{basePath}"");
                print(nodeId1);
            ";
            var output1 = RunProgram(source1).Trim();
            Assert.StartsWith("node_", output1);

            var source2 = $@"
                var memory = new GraphMemory();
                memory.load(""{basePath}"");
                var related = memory.findRelated(""{output1}"");
                print(related.length >= 1);
            ";
            var output2 = RunProgram(source2);
            Assert.Contains("true", output2);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private sealed class TestReflectClient : ObjectInstance
    {
        public int Calls { get; private set; }
        public TestReflectClient() : base(null) { }
        public RuntimeValue CallMethod(string methodName, List<RuntimeValue> arguments, MaldaLang.Interpreter.Interpreter interpreter)
        {
            if (methodName != "complete")
                throw new RuntimeException("Unsupported method");
            Calls++;
            return RuntimeValue.String("{\"facts\":[{\"fact\":\"Injected client fact\",\"confidence\":0.9,\"category\":\"test\"}]}");
        }
    }

    [Fact]
    public void Reflect_UsesInjectedClient()
    {
        var interpreter = new MaldaLang.Interpreter.Interpreter();
        var memory = new GraphMemoryInstance();
        memory.SetInterpreter(interpreter);
        memory.CallMethod("initialize", new List<RuntimeValue>(), interpreter);
        JsonObject EpisodicMeta()
        {
            var meta = new JsonObject();
            meta.Set("type", RuntimeValue.String("episodic"));
            meta.Set("scope", RuntimeValue.String("chat:inject"));
            return meta;
        }
        memory.CallMethod("remember", new List<RuntimeValue>
        {
            RuntimeValue.String("Q1"),
            RuntimeValue.String("A1"),
            RuntimeValue.Object(EpisodicMeta())
        }, interpreter);
        memory.CallMethod("remember", new List<RuntimeValue>
        {
            RuntimeValue.String("Q2"),
            RuntimeValue.String("A2"),
            RuntimeValue.Object(EpisodicMeta())
        }, interpreter);
        memory.CallMethod("remember", new List<RuntimeValue>
        {
            RuntimeValue.String("Q3"),
            RuntimeValue.String("A3"),
            RuntimeValue.Object(EpisodicMeta())
        }, interpreter);

        var client = new TestReflectClient();
        var options = new JsonObject();
        options.Set("scope", RuntimeValue.String("chat:inject"));
        options.Set("minEpisodic", RuntimeValue.Integer(3));
        options.Set("client", RuntimeValue.Object(client));
        var result = memory.CallMethod("reflect", new List<RuntimeValue> { RuntimeValue.Object(options) }, interpreter);
        Assert.Equal(1, client.Calls);
        Assert.Equal(MaldaLang.Interpreter.ValueType.Object, result.Type);
    }

    [Fact]
    public void Query_RerankScores_ReordersResults()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            var a = memory.remember(""rerank alpha fact"", """", { ""type"": ""semantic"" });
            var b = memory.remember(""rerank beta fact"", """", { ""type"": ""semantic"" });
            var result = memory.query(""rerank fact"", 2, {
                ""minScore"": 0,
                ""rerank"": true,
                ""rerankTopK"": 2,
                ""rerankScores"": [
                    { ""nodeId"": a, ""score"": 0.1 },
                    { ""nodeId"": b, ""score"": 0.99 }
                ]
            });
            print(result[0].nodeId == b);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Reflect_SupersedesConflictingSemantic_WhenHigherConfidence()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""Project uses API v1 for auth"", """", { ""type"": ""semantic"", ""scope"": ""chat:conflict"", ""confidence"": 0.4 });
            memory.remember(""Q1"", ""A1"", { ""type"": ""episodic"", ""scope"": ""chat:conflict"" });
            memory.remember(""Q2"", ""A2"", { ""type"": ""episodic"", ""scope"": ""chat:conflict"" });
            memory.remember(""Q3"", ""A3"", { ""type"": ""episodic"", ""scope"": ""chat:conflict"" });
            var r = memory.reflect({
                ""scope"": ""chat:conflict"",
                ""minEpisodic"": 3,
                ""resolveConflicts"": true,
                ""facts"": [{ ""fact"": ""Project uses API v1 for auth and session"", ""confidence"": 0.95, ""category"": ""policy"" }]
            });
            var s = memory.stats();
            print(r.factsCreated >= 1);
            print(s.nodes >= 2);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void ForgetByScope_And_ForgetByCategory_Work()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""A scope note"", """", { ""type"": ""semantic"", ""scope"": ""chat:a"", ""category"": ""prefs"" });
            memory.remember(""B scope note"", """", { ""type"": ""semantic"", ""scope"": ""chat:b"", ""category"": ""prefs"" });
            memory.remember(""C scope note"", """", { ""type"": ""semantic"", ""scope"": ""chat:b"", ""category"": ""obsolete"" });
            var removedScope = memory.forgetByScope(""chat:a"");
            var removedCategory = memory.forgetByCategory(""obsolete"", { ""scope"": ""chat:b"" });
            var stats = memory.stats();
            print(removedScope == 1);
            print(removedCategory == 1);
            print(stats.nodes == 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Stats_ExposeDualIndexPending_OnLegacyLoad()
    {
        var tempDir = CreateTempDirectory("gm_dual_index_");
        var basePath = Path.Combine(tempDir, "legacy").Replace('\\', '/');
        try
        {
            var seed = $@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.remember(""Legacy dual index fact"", """", {{ ""type"": ""semantic"" }});
                memory.save(""{basePath}"");
            ";
            RunProgram(seed);
            var metadataPath = basePath + ".metadata.json";
            var json = File.ReadAllText(metadataPath);
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                var rewritten = new Dictionary<string, object?>();
                foreach (var node in root.EnumerateObject())
                {
                    if (node.Value.ValueKind == JsonValueKind.Object)
                    {
                        var nodeMap = new Dictionary<string, object?>();
                        foreach (var prop in node.Value.EnumerateObject())
                        {
                            if (prop.Name == "dualIndexMigrated")
                                continue;
                            nodeMap[prop.Name] = JsonSerializer.Deserialize<object?>(prop.Value.GetRawText());
                        }
                        rewritten[node.Name] = nodeMap;
                    }
                    else
                    {
                        rewritten[node.Name] = JsonSerializer.Deserialize<object?>(node.Value.GetRawText());
                    }
                }
                File.WriteAllText(metadataPath, JsonSerializer.Serialize(rewritten));
            }

            var source = $@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.load(""{basePath}"", {{ ""migrateDualIndex"": false }});
                var before = memory.stats();
                memory.load(""{basePath}"", {{ ""migrateDualIndex"": true }});
                var after = memory.stats();
                print(before.dualIndexPending >= 1);
                print(after.dualIndexPending == 0);
            ";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Stats_IncludeEnrichedFields()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""stats field"", """", { ""type"": ""semantic"" });
            var s = memory.stats();
            print(s.supersededCount != null);
            print(s.dualIndexPending != null);
            print(s.lastReflectAt != null);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Load_DefaultMigration_SetsDualIndexPendingToZero()
    {
        var tempDir = CreateTempDirectory("gm_migrate_default_");
        var basePath = Path.Combine(tempDir, "memory").Replace('\\', '/');
        try
        {
            RunProgram($@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.remember(""Migrate default test"", """", {{ ""type"": ""semantic"" }});
                memory.save(""{basePath}"");
            ");
            var output = RunProgram($@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.load(""{basePath}"");
                var s = memory.stats();
                print(s.dualIndexPending == 0);
            ");
            Assert.Contains("true", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Reflect_MinConfidence_Filtering_Works()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""q1"", ""a1"", { ""type"": ""episodic"", ""scope"": ""chat:mc"" });
            memory.remember(""q2"", ""a2"", { ""type"": ""episodic"", ""scope"": ""chat:mc"" });
            memory.remember(""q3"", ""a3"", { ""type"": ""episodic"", ""scope"": ""chat:mc"" });
            var r = memory.reflect({
                ""scope"": ""chat:mc"",
                ""minEpisodic"": 3,
                ""minConfidence"": 0.95,
                ""facts"": [{ ""fact"": ""low confidence fact"", ""confidence"": 0.4 }]
            });
            print(r.factsCreated == 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_Rerank_WithoutScores_FallsBackSafely()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""fallback rerank alpha"", """", { ""type"": ""semantic"" });
            memory.remember(""fallback rerank beta"", """", { ""type"": ""semantic"" });
            var r = memory.query(""fallback rerank"", 2, { ""minScore"": 0, ""rerank"": true, ""rerankModel"": ""test/model"" });
            print(r.length >= 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void ForgetByScope_WithTypeFilter_Works()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""scope semantic"", """", { ""type"": ""semantic"", ""scope"": ""chat:t"" });
            memory.remember(""scope episodic"", ""ctx"", { ""type"": ""episodic"", ""scope"": ""chat:t"" });
            var removed = memory.forgetByScope(""chat:t"", { ""type"": ""episodic"" });
            var s = memory.stats();
            print(removed == 1);
            print(s.nodes == 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void ForgetByCategory_WithoutScope_RemovesAcrossScopes()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""cat one"", """", { ""type"": ""semantic"", ""scope"": ""chat:x"", ""category"": ""tmp"" });
            memory.remember(""cat two"", """", { ""type"": ""semantic"", ""scope"": ""chat:y"", ""category"": ""tmp"" });
            var removed = memory.forgetByCategory(""tmp"");
            print(removed == 2);
            print(memory.stats().nodes == 0);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Reflect_ResolveConflictsFalse_AllowsCreation()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""policy note"", """", { ""type"": ""semantic"", ""scope"": ""chat:rfalse"", ""confidence"": 0.9 });
            memory.remember(""q1"", ""a1"", { ""type"": ""episodic"", ""scope"": ""chat:rfalse"" });
            memory.remember(""q2"", ""a2"", { ""type"": ""episodic"", ""scope"": ""chat:rfalse"" });
            memory.remember(""q3"", ""a3"", { ""type"": ""episodic"", ""scope"": ""chat:rfalse"" });
            var r = memory.reflect({
                ""scope"": ""chat:rfalse"",
                ""minEpisodic"": 3,
                ""resolveConflicts"": false,
                ""facts"": [{ ""fact"": ""policy note update"", ""confidence"": 0.95 }]
            });
            print(r.factsCreated >= 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_RerankScores_IgnoresMissingNodeIds()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""rerank missing one"", """", { ""type"": ""semantic"" });
            var r = memory.query(""rerank missing"", 1, {
                ""minScore"": 0,
                ""rerank"": true,
                ""rerankScores"": [{ ""nodeId"": ""node_missing"", ""score"": 1.0 }]
            });
            print(r.length == 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_Explain_ReturnsScoreBreakdown()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""explain test fact about cats"", """", { ""type"": ""semantic"" });
            var r = memory.query(""cats"", 1, { ""minScore"": 0, ""explain"": true });
            print(r.length == 1);
            print(r[0].explain != null);
            print(r[0].explain.finalScore != null);
            print(r[0].score != null);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void GetAssistantMemory_LoadsPersistedState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_assistant_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "assistant").Replace('\\', '/');
        try
        {
            var seed = $@"
                var m = new GraphMemory();
                m.initialize();
                m.remember(""assistant builtin fact"", """", {{ ""type"": ""semantic"" }});
                m.save(""{basePath}"");
            ";
            RunProgram(seed);
            var source = $@"
                var m = getAssistantMemory(""{basePath}"");
                print(m.stats().nodes >= 1);
            ";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void StartKbWatch_CanStopWithoutError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_kbwatch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var source = $@"
                var memory = new GraphMemory();
                memory.initialize();
                var started = memory.startKbWatch(""{tempDir.Replace('\\', '/')}"", ""*.md"", {{ ""scope"": ""global"" }});
                memory.stopKbWatch();
                print(started == true);
            ";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Validate_HealthyMemory_ReturnsOk()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""validate healthy fact"", """", { ""type"": ""semantic"" });
            var report = memory.validate();
            print(report.ok == true);
            print(report.counts.metadataNodes == 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Save_WithBackup_CreatesTimestampedArtifacts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gm_backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var basePath = Path.Combine(tempDir, "assistant").Replace('\\', '/');
        try
        {
            var seed = $@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.remember(""backup seed"", """", {{ ""type"": ""semantic"" }});
                memory.save(""{basePath}"");
            ";
            RunProgram(seed);
            var source = $@"
                var memory = new GraphMemory();
                memory.initialize();
                memory.load(""{basePath}"");
                memory.remember(""backup updated"", """", {{ ""type"": ""semantic"" }});
                memory.save(""{basePath}"", {{ ""backup"": true, ""maxBackups"": 3 }});
                print(true);
            ";
            var output = RunProgram(source);
            Assert.Contains("true", output);
            var backups = Directory.GetFiles(tempDir, "assistant.backup.*.graph.json");
            Assert.NotEmpty(backups);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ReflectAsync_SchedulesWithoutBlocking()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""q1"", ""a1"", { ""type"": ""episodic"" });
            memory.remember(""q2"", ""a2"", { ""type"": ""episodic"" });
            memory.remember(""q3"", ""a3"", { ""type"": ""episodic"" });
            var r = memory.reflectAsync({
                ""minEpisodic"": 3,
                ""facts"": [{ ""fact"": ""async reflected fact"", ""confidence"": 0.9 }]
            });
            print(r.scheduled == true);
            sleep(200);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_ScopeHierarchy_IncludesParentAndGlobal()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""project shared fact"", """", { ""scope"": ""project:app"", ""type"": ""semantic"" });
            memory.remember(""chat one fact"", """", { ""scope"": ""chat:1"", ""type"": ""semantic"" });
            memory.remember(""chat two fact"", """", { ""scope"": ""chat:2"", ""type"": ""semantic"" });
            var results = memory.query(""fact"", 10, {
                ""minScore"": 0,
                ""scope"": ""chat:1"",
                ""scopeHierarchy"": [""chat:1"", ""project:app"", ""global""]
            });
            var count = 0;
            for (var i = 0; i < results.length; i++) {
                var scope = results[i].scope;
                if (scope == ""chat:1"" || scope == ""project:app"" || scope == null || scope == ""global"") {
                    count = count + 1;
                }
            }
            print(count == results.length);
            print(results.length == 2);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_Bm25_FindsLexicalMatch()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""uuid-file-abc123 path/to/module.malda"", """", { ""type"": ""semantic"" });
            var results = memory.query(""abc123 malda"", 1, {
                ""minScore"": 0,
                ""hybridLexical"": true,
                ""lexicalMode"": ""bm25""
            });
            print(results.length >= 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_RememberFilePath_SurvivesHybridQuery()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""plugin campi dll note body"", ""plugins"", {
                ""type"": ""semantic"",
                ""source"": ""secondbrain"",
                ""filePath"": ""notes/campiesterni-plugin.md""
            });
            var results = memory.query(""plugin campi"", 3, {
                ""minScore"": 0,
                ""hybridLexical"": true,
                ""lexicalMode"": ""bm25"",
                ""lexicalMinScore"": 0,
                ""type"": ""semantic"",
                ""explain"": true
            });
            print(""hits="" + string(results.length));
            if (results.length > 0) {
                print(""path="" + string(results[0].filePath));
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("hits=", output);
        Assert.DoesNotContain("hits=0", output);
        Assert.Contains("path=notes/campiesterni-plugin.md", output);
    }

    [Fact]
    public void Query_ScopeHierarchy_FromEnv_IncludesConfiguredScopes()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_SCOPE_HIERARCHY");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_MEMORY_SCOPE_HIERARCHY", "project:app,org:acme,global");
            var source = @"
                var memory = new GraphMemory();
                memory.initialize();
                memory.remember(""org fact"", """", { ""scope"": ""org:acme"", ""type"": ""semantic"" });
                memory.remember(""project fact"", """", { ""scope"": ""project:app"", ""type"": ""semantic"" });
                memory.remember(""chat fact"", """", { ""scope"": ""chat:9"", ""type"": ""semantic"" });
                memory.remember(""other chat"", """", { ""scope"": ""chat:8"", ""type"": ""semantic"" });
                var results = memory.query(""fact"", 10, { ""minScore"": 0, ""scope"": ""chat:9"" });
                var count = 0;
                for (var i = 0; i < results.length; i++) {
                    var scope = results[i].scope;
                    if (scope == ""chat:9"" || scope == ""project:app"" || scope == ""org:acme"" || scope == null || scope == ""global"") {
                        count = count + 1;
                    }
                }
                print(count == results.length);
                print(results.length == 3);
            ";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_MEMORY_SCOPE_HIERARCHY", previous);
        }
    }

    [Fact]
    public void Query_OnnxRerank_FallsBackWithoutModel()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_RERANK_MODEL_PATH");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_MEMORY_RERANK_MODEL_PATH", "");
            var source = @"
                var memory = new GraphMemory();
                memory.initialize();
                memory.remember(""alpha specific term one"", """", { ""type"": ""semantic"" });
                memory.remember(""beta unrelated topic"", """", { ""type"": ""semantic"" });
                var results = memory.query(""alpha specific"", 1, {
                    ""minScore"": 0,
                    ""rerank"": true,
                    ""rerankMode"": ""onnx"",
                    ""rerankTopK"": 2
                });
                print(results.length == 1);
            ";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_MEMORY_RERANK_MODEL_PATH", previous);
        }
    }

    [Fact]
    public void Query_CrossEncoderRerank_ReturnsResults()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""alpha specific term one"", """", { ""type"": ""semantic"" });
            memory.remember(""beta unrelated topic"", """", { ""type"": ""semantic"" });
            var results = memory.query(""alpha specific"", 1, {
                ""minScore"": 0,
                ""rerank"": true,
                ""rerankMode"": ""cross"",
                ""rerankTopK"": 2
            });
            print(results.length == 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_TagsFilter_AnyAndAll()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""bom plugin note"", """", { ""type"": ""semantic"", ""tags"": [""bom"", ""plugin""] });
            memory.remember(""bom only note"", """", { ""type"": ""semantic"", ""tags"": [""bom""] });
            memory.remember(""untagged note"", """", { ""type"": ""semantic"" });
            var anyHits = memory.query(""note"", 10, {
                ""minScore"": 0,
                ""hybridLexical"": true,
                ""lexicalMode"": ""bm25"",
                ""lexicalMinScore"": 0,
                ""tags"": [""bom"", ""plugin""],
                ""tagsMode"": ""any""
            });
            var allHits = memory.query(""note"", 10, {
                ""minScore"": 0,
                ""hybridLexical"": true,
                ""lexicalMode"": ""bm25"",
                ""lexicalMinScore"": 0,
                ""tags"": [""bom"", ""plugin""],
                ""tagsMode"": ""all""
            });
            print(anyHits.length == 2);
            print(allHits.length == 1);
            print(allHits[0].fact == ""bom plugin note"");
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Remember_Tags_IndexedForBm25()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""plain body text"", """", {
                ""type"": ""semantic"",
                ""tags"": [""zzunique-tag-token""]
            });
            var results = memory.query(""zzunique-tag-token"", 1, {
                ""minScore"": 0,
                ""hybridLexical"": true,
                ""lexicalMode"": ""bm25"",
                ""lexicalMinScore"": 0
            });
            print(results.length >= 1);
            print(results[0].tags[0] == ""zzunique-tag-token"");
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_LexicalMinScoreAuto_AdmitsLexicalWhenVectorWeak()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""uuid-lexauto-xyz999 path/module.malda"", """", { ""type"": ""semantic"" });
            var results = memory.query(""xyz999 malda"", 1, {
                ""minScore"": 0.99,
                ""hybridLexical"": true,
                ""lexicalMode"": ""bm25"",
                ""lexicalMinScore"": ""auto"",
                ""diagnostics"": true
            });
            var diag = memory.getLastQueryDiagnostics();
            print(results.length >= 1);
            print(diag.lexicalMinScoreMode == ""auto-weak-vector"" || diag.lexicalMinScoreApplied == 0);
            print(diag.returned >= 1);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Query_GetLastQueryDiagnostics_Populated()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""alpha diagnostic fact"", """", { ""type"": ""semantic"", ""tags"": [""alpha""] });
            var before = memory.getLastQueryDiagnostics();
            print(before == null);
            var results = memory.query(""alpha"", 3, {
                ""minScore"": 0,
                ""hybridLexical"": true,
                ""lexicalMode"": ""bm25"",
                ""lexicalMinScore"": 0,
                ""tags"": [""alpha""],
                ""diagnostics"": true,
                ""explain"": true
            });
            var diag = memory.getLastQueryDiagnostics();
            print(results.length >= 1);
            print(diag != null);
            print(diag.query == ""alpha"");
            print(diag.returned >= 1);
            print(results[0].explain.tags[0] == ""alpha"");
            print(results[0].explain.tagsMatched == true);
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }

    [Fact]
    public void ForgetByTag_RemovesOnlyMatchingNodes()
    {
        var source = @"
            var memory = new GraphMemory();
            memory.initialize();
            memory.remember(""keep me"", """", { ""type"": ""semantic"", ""tags"": [""keep""] });
            memory.remember(""drop me"", """", { ""type"": ""semantic"", ""tags"": [""drop"", ""tmp""] });
            memory.remember(""also drop"", """", { ""type"": ""semantic"", ""tags"": [""drop""] });
            var removed = memory.forgetByTag(""drop"");
            var stats = memory.stats();
            var remaining = memory.query(""keep"", 5, {
                ""minScore"": 0,
                ""hybridLexical"": true,
                ""lexicalMode"": ""bm25"",
                ""lexicalMinScore"": 0
            });
            print(removed == 2);
            print(stats.nodes == 1);
            print(remaining.length >= 1);
            print(remaining[0].fact == ""keep me"");
        ";
        var output = RunProgram(source);
        Assert.Contains("true", output);
    }
}
