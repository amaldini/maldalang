// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

public class ParseJsonConfusionTests
{
    [Fact]
    public void ParseJson_OneArg_NamesParseJSON()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            BuiltInFunctions.CallBuiltIn(
                "parseJson",
                new List<RuntimeValue> { RuntimeValue.String("{\"a\":1}") },
                null));
        Assert.Contains("parseJSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJSON_TwoArgs_NamesParseJson()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            BuiltInFunctions.CallBuiltIn(
                "parseJSON",
                new List<RuntimeValue>
                {
                    RuntimeValue.String("{\"a\":1}"),
                    RuntimeValue.String("Name")
                },
                null));
        Assert.Contains("parseJson", ex.Message, StringComparison.Ordinal);
    }
}
