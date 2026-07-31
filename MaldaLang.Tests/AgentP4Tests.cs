// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class AgentP4Tests : TestBase
{
    [Fact]
    public void LLMClient_BuildRequestBody_AppliesPromptOverrides()
    {
        var client = new LLMClientInstance
        {
            ApiUrl = "https://example.com",
            ApiKey = "test",
            Model = "base-model",
            Temperature = 0.7,
            MaxTokens = 100
        };

        var messages = RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.Object(new JsonObject())
        });

        var body = client.BuildRequestBody(
            messages,
            null,
            null,
            new LlmRequestOverrides
            {
                Model = "override-model",
                Temperature = 0.2,
                MaxTokens = 50
            });

        Assert.Equal("override-model", body["model"]);
        Assert.Equal(0.2, body["temperature"]);
        Assert.Equal(50, body["max_tokens"]);
    }

    [Fact]
    public void DevAgent_EnableCodeMemory_SetsScopeAndIndexesFile()
    {
        var tempDir = CreateTempDirectory("devagent_code_");
        try
        {
            var sourcePath = Path.Combine(tempDir, "Sample.cs");
            File.WriteAllText(sourcePath, "class Sample { void Run() {} }");

            var client = new LLMClientInstance
            {
                ApiUrl = "https://example.com",
                ApiKey = "test",
                Model = "test"
            };
            var agent = new DevAgentInstance("Dev", "developer", "Build things", client, tempDir);
            var interpreter = new Interpreter.Interpreter();
            agent.SetInterpreter(interpreter);

            agent.CallMethod("enableCodeMemory", new List<RuntimeValue>());
            var indexed = agent.CallMethod("indexCodebase", new List<RuntimeValue> { RuntimeValue.String(".cs") });
            Assert.Equal(MaldaLang.Interpreter.ValueType.Integer, indexed.Type);
            Assert.True(indexed.AsInteger() >= 1);

            var memoryVal = agent.GetMemory();
            Assert.Equal(MaldaLang.Interpreter.ValueType.Object, memoryVal.Type);
            Assert.IsType<GraphMemoryInstance>(memoryVal.AsObject());
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Agent_SetMemoryScopeParent_AcceptsParentScope()
    {
        var client = new LLMClientInstance
        {
            ApiUrl = "https://example.com",
            ApiKey = "test",
            Model = "test"
        };
        var agent = new AgentInstance();
        var interpreter = new Interpreter.Interpreter();
        agent.SetInterpreter(interpreter);
        agent.Initialize("Test", "assistant", "You help users", client, null, null, null);
        agent.EnableMemory(new List<RuntimeValue>());
        agent.CallMethod("setMemoryScopeParent", new List<RuntimeValue> { RuntimeValue.String("project:app") });
        agent.CallMethod("setMemoryScope", new List<RuntimeValue> { RuntimeValue.String("chat:7") });
        agent.CallMethod("remember", new List<RuntimeValue> { RuntimeValue.String("scoped fact") });

        var memoryVal = agent.GetMemory();
        var memory = memoryVal.AsObject() as GraphMemoryInstance;
        Assert.NotNull(memory);
        var recent = memory!.CallMethod("getRecent", new List<RuntimeValue>
        {
            RuntimeValue.Integer(1),
            RuntimeValue.String(""),
            RuntimeValue.String(""),
            RuntimeValue.String("chat:7")
        }, interpreter);
        var entries = recent.AsArray();
        Assert.Single(entries);
    }

    [Fact]
    public void Agent_SetMemoryScopeHierarchy_PrependsActiveScope()
    {
        var client = new LLMClientInstance
        {
            ApiUrl = "https://example.com",
            ApiKey = "test",
            Model = "test"
        };
        var agent = new AgentInstance();
        var interpreter = new Interpreter.Interpreter();
        agent.SetInterpreter(interpreter);
        agent.Initialize("Test", "assistant", "You help users", client, null, null, null);
        agent.CallMethod("setMemoryScopeHierarchy", new List<RuntimeValue>
        {
            RuntimeValue.Array(new List<RuntimeValue>
            {
                RuntimeValue.String("project:app"),
                RuntimeValue.String("org:acme"),
                RuntimeValue.String("global")
            })
        });
        agent.CallMethod("setMemoryScope", new List<RuntimeValue> { RuntimeValue.String("chat:42") });
        agent.EnableMemory(new List<RuntimeValue>());
        agent.CallMethod("remember", new List<RuntimeValue> { RuntimeValue.String("chat scoped") });
        var projectMeta = new JsonObject();
        projectMeta.Set("scope", RuntimeValue.String("project:app"));
        projectMeta.Set("type", RuntimeValue.String("semantic"));
        agent.CallMethod("remember", new List<RuntimeValue>
        {
            RuntimeValue.String("project scoped"),
            RuntimeValue.String(""),
            RuntimeValue.Object(projectMeta)
        });

        var memoryVal = agent.GetMemory();
        var memory = memoryVal.AsObject() as GraphMemoryInstance;
        Assert.NotNull(memory);
        var queryOpts = new JsonObject();
        queryOpts.Set("minScore", RuntimeValue.Float(0));
        queryOpts.Set("scope", RuntimeValue.String("chat:42"));
        queryOpts.Set("scopeHierarchy", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("chat:42"),
            RuntimeValue.String("project:app"),
            RuntimeValue.String("org:acme"),
            RuntimeValue.String("global")
        }));
        var results = memory!.CallMethod("query", new List<RuntimeValue>
        {
            RuntimeValue.String("scoped"),
            RuntimeValue.Integer(10),
            RuntimeValue.Object(queryOpts)
        }, interpreter);
        Assert.Equal(MaldaLang.Interpreter.ValueType.Array, results.Type);
        Assert.True(results.AsArray().Count >= 2);
    }

    [Fact]
    public void DevAgent_EnableCodeMemory_OnExistingMemory_AttachesTools()
    {
        var tempDir = CreateTempDirectory("ralph_code_mem_");
        try
        {
            var client = new LLMClientInstance
            {
                ApiUrl = "https://example.com",
                ApiKey = "test",
                Model = "test"
            };
            var interpreter = new Interpreter.Interpreter();
            var memory = new GraphMemoryInstance();
            memory.SetInterpreter(interpreter);
            memory.CallMethod("initialize", new List<RuntimeValue>(), interpreter);

            var agent = new DevAgentInstance("Ralph", "developer", "Build", client, tempDir);
            agent.SetInterpreter(interpreter);
            agent.CallMethod("useMemory", new List<RuntimeValue> { RuntimeValue.Object(memory) });
            agent.CallMethod("enableCodeMemory", new List<RuntimeValue>());
            var indexed = agent.CallMethod("indexCodebase", new List<RuntimeValue>());
            Assert.Equal(MaldaLang.Interpreter.ValueType.Integer, indexed.Type);
            Assert.Same(memory, agent.GetMemory().AsObject());
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Agent_SetMemoryRerank_AcceptsModeAndPath()
    {
        var client = new LLMClientInstance
        {
            ApiUrl = "https://example.com",
            ApiKey = "test",
            Model = "test"
        };
        var agent = new AgentInstance();
        var interpreter = new Interpreter.Interpreter();
        agent.SetInterpreter(interpreter);
        agent.Initialize("Test", "assistant", "Help", client, null, null, null);
        var result = agent.CallMethod("setMemoryRerank", new List<RuntimeValue>
        {
            RuntimeValue.Boolean(true),
            RuntimeValue.String("onnx"),
            RuntimeValue.String("C:/models/cross-encoder"),
            RuntimeValue.Integer(15)
        });
        Assert.Equal(MaldaLang.Interpreter.ValueType.Null, result.Type);
    }

    [Fact]
    public void MultiAgentShared_ScopesIsolateTeamFacts()
    {
        var source = @"
var memory = new GraphMemory();
memory.initialize();
memory.remember(""Alpha prefers TS"", """", { ""type"": ""semantic"", ""scope"": ""team:alpha"" });
memory.remember(""Beta prefers Python"", """", { ""type"": ""semantic"", ""scope"": ""team:beta"" });
memory.remember(""Shared CI rule"", """", { ""type"": ""semantic"", ""scope"": ""global"" });
var alpha = memory.query(""preference"", 10, { ""scope"": ""team:alpha"", ""scopeHierarchy"": [""team:alpha"", ""global""], ""minScore"": 0 });
var beta = memory.query(""preference"", 10, { ""scope"": ""team:beta"", ""scopeHierarchy"": [""team:beta"", ""global""], ""minScore"": 0 });
var alphaHasBeta = false;
for (var i = 0; i < alpha.length; i++) {
    if (indexOf(alpha[i].fact, ""Beta"") >= 0) { alphaHasBeta = true; }
}
var betaHasAlpha = false;
for (var j = 0; j < beta.length; j++) {
    if (indexOf(beta[j].fact, ""Alpha"") >= 0) { betaHasAlpha = true; }
}
print(!alphaHasBeta);
print(!betaHasAlpha);
print(alpha.length >= 1);
print(beta.length >= 1);
";
        var output = RunProgram(source);
        var lines = output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        Assert.Equal(4, lines.Length);
        Assert.Equal("true", lines[0]);
        Assert.Equal("true", lines[1]);
        Assert.Equal("true", lines[2]);
        Assert.Equal("true", lines[3]);
    }

    [Fact]
    public void SeedRalphMemoryFromInterview_WritesSemanticFacts()
    {
        var tempDir = CreateTempDirectory("interview_seed_");
        try
        {
            var draft = new
            {
                profile = "new",
                mode = "new",
                brief = new
                {
                    title = "DemoApp",
                    vision = "A demo application",
                    constraints = "Must run offline",
                    deliverables = "CLI tool"
                }
            };
            File.WriteAllText(
                Path.Combine(tempDir, ".ralph-interview-brief.json"),
                JsonSerializer.Serialize(draft));

            var source = $@"
function envBool(name, defaultValue) {{
    var v = getEnv(name);
    if (v == null || v == """") {{ return defaultValue; }}
    return v == ""1"" || v == ""true"";
}}
function truncateText(text, maxLen) {{
    if (text == null) {{ return """"; }}
    if (length(text) <= maxLen) {{ return text; }}
    return substring(text, 0, maxLen);
}}
function loadRalphInterviewBrief(workDir) {{
    var draftPath = pathJoin(workDir, "".ralph-interview-brief.json"");
    if (!hasFile(draftPath)) {{ return null; }}
    return parseJSON(readFile(draftPath));
}}
function seedRalphMemoryFromInterview(memory, workDir, memoryScope) {{
    if (!envBool(""MALDA_RALPH_MEMORY_SEED_INTERVIEW"", true)) {{ return; }}
    var payload = loadRalphInterviewBrief(workDir);
    if (payload == null || payload.brief == null) {{ return; }}
    var brief = payload.brief;
    if (brief.title != null && brief.title != """") {{
        memory.remember(""Project: "" + string(brief.title), string(brief.vision), {{
            ""type"": ""semantic"",
            ""source"": ""interview"",
            ""scope"": memoryScope,
            ""category"": ""project""
        }});
    }}
}}
var memory = new GraphMemory();
memory.initialize();
seedRalphMemoryFromInterview(memory, ""{tempDir.Replace("\\", "\\\\")}"", ""ralph:DemoApp"");
var recent = memory.getRecent(5, """", """", ""ralph:DemoApp"");
print(recent.length >= 1);
";
            var output = RunProgram(source);
            Assert.Contains("true", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
