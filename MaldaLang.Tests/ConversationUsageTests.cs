// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using Xunit;

namespace MaldaLang.Tests;

public class ConversationUsageTests
{
    [Fact]
    public void AccumulateTurnUsage_SumsPromptCompletionAndCostAcrossRounds()
    {
        var conversation = new ConversationInstance();
        conversation.AddUserMessage("task");

        var accumulate = typeof(ConversationInstance).GetMethod(
            "AccumulateTurnUsage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var attach = typeof(ConversationInstance).GetMethod(
            "AttachAccumulatedUsage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        accumulate.Invoke(conversation, new object[] { BuildUsageResponse(100, 20, 0.0001) });
        accumulate.Invoke(conversation, new object[] { BuildUsageResponse(50, 10, 0.00005) });

        var finalResponse = new JsonObject();
        finalResponse.Set("content", RuntimeValue.String("done"));
        attach.Invoke(conversation, new object[] { RuntimeValue.Object(finalResponse) });

        var usage = finalResponse.Get("usage", null)!.AsObject();
        Assert.Equal(150, usage.Get("promptTokens", null)!.AsInteger());
        Assert.Equal(30, usage.Get("completionTokens", null)!.AsInteger());
        Assert.Equal(180, usage.Get("totalTokens", null)!.AsInteger());
        Assert.Equal(0.00015, usage.Get("cost", null)!.AsFloat(), 6);
    }

    private static JsonObject BuildUsageResponse(int promptTokens, int completionTokens, double cost)
    {
        var usage = new JsonObject();
        usage.Set("promptTokens", RuntimeValue.Integer(promptTokens));
        usage.Set("completionTokens", RuntimeValue.Integer(completionTokens));
        usage.Set("totalTokens", RuntimeValue.Integer(promptTokens + completionTokens));
        usage.Set("cost", RuntimeValue.Float(cost));

        var response = new JsonObject();
        response.Set("content", RuntimeValue.String("partial"));
        response.Set("usage", RuntimeValue.Object(usage));
        return response;
    }
}
