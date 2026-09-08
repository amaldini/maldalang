// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Interpreter;
using MaldaLang.Runtime.Journal;
using Xunit;

namespace MaldaLang.Tests;

public class PromptUsageTests
{
    [Fact]
    public void UsageMetadata_IsReadableOnPrimitive()
    {
        var value = RuntimeValue.String("hi").WithUsage(new RunUsage
        {
            PromptTokens = 3,
            CompletionTokens = 2,
            Cost = 0.01,
            Ms = 12,
            Repairs = 1,
            Model = "t"
        });
        Assert.NotNull(value.Usage);
        Assert.Equal(5, value.Usage!.TotalTokens);
        var obj = value.Usage.ToRuntimeValue().AsObject();
        Assert.Equal(5, obj.Get("tokens").AsInteger());
    }
}
