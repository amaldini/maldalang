// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class ToJsonClassInstanceTests
{
    [Fact]
    public async Task ToJSON_CyclicClassInstance_Throws()
    {
        var outcome = await TestBase.CaptureInterpretOutcomeAsync(
            """
            class Node(next);
            var n = new Node(null);
            n.next = n;
            print(toJSON(n));
            """);

        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("cyclic", outcome.Exception?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToJSON_UnsetPublicField_IsNull()
    {
        var stdout = await TestBase.CaptureInterpretAsync(
            """
            class Box {
                public var x;
            }
            print(toJSON(new Box()));
            """);

        Assert.Equal("{\"x\":null}", stdout);
    }
}
