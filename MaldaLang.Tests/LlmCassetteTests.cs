// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.LlmCassettes;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class LlmCassetteTests : TestBase
{
    [Fact]
    public void RecordThenReplay_ReturnsSameResponse()
    {
        var path = Path.Combine(CreateTempDirectory(), "cassettes.jsonl");
        CassetteTransport.ResetForTesting();
        CassetteTransport.RecordPathOverride = path;

        var messages = RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.Object(MakeMsg("user", "hi"))
        });
        var live = CassetteTransport.Execute(
            CassetteTransport.RequestFromRuntime(messages, null, null, "test-model"),
            () => MakeResponse("hello"));

        Assert.Equal("hello", live.AsObject().Get("content").AsString());

        CassetteTransport.ResetForTesting();
        CassetteTransport.ReplayPathOverride = path;
        CassetteTransport.StrictOverride = true;

        var replayed = CassetteTransport.Execute(
            CassetteTransport.RequestFromRuntime(messages, null, null, "test-model"),
            () => throw new InvalidOperationException("live client must not run"));

        Assert.Equal("hello", replayed.AsObject().Get("content").AsString());
        CassetteTransport.ResetForTesting();
    }

    [Fact]
    public void StrictMiss_Throws()
    {
        CassetteTransport.ResetForTesting();
        CassetteTransport.ReplayPathOverride = Path.Combine(CreateTempDirectory(), "empty.jsonl");
        CassetteTransport.StrictOverride = true;
        File.WriteAllText(CassetteTransport.ReplayPathOverride, "");

        var messages = RuntimeValue.Array(new List<RuntimeValue>());
        Assert.Throws<RuntimeException>(() =>
            CassetteTransport.Execute(
                CassetteTransport.RequestFromRuntime(messages, null, null, "m"),
                () => MakeResponse("nope")));
        CassetteTransport.ResetForTesting();
    }

    private static JsonObject MakeMsg(string role, string content)
    {
        var obj = new JsonObject();
        obj.Set("role", RuntimeValue.String(role));
        obj.Set("content", RuntimeValue.String(content));
        return obj;
    }

    private static RuntimeValue MakeResponse(string content)
    {
        var obj = new JsonObject();
        obj.Set("content", RuntimeValue.String(content));
        return RuntimeValue.Object(obj);
    }
}
