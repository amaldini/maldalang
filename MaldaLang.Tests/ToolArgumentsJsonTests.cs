// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.Json;
using MaldaLang.BuiltIns;
using Xunit;

namespace MaldaLang.Tests;

public class ToolArgumentsJsonTests
{
    [Fact]
    public void IsLikelyTruncated_WriteFile_SmallTruncatedJson_Detected()
    {
        var json = """{"filePath":"snake.html","content":"<!DOCTYPE html><html>""";
        var ex = Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(json));
        Assert.True(ToolArgumentsJsonHelper.IsLikelyTruncated(ex, json, "write_file"));
    }

    [Fact]
    public void IsLikelyTruncated_WriteFile_ShortInvalidJson_NotDetected()
    {
        var json = """{"oops":true}""";
        var ex = Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse("{"));
        Assert.False(ToolArgumentsJsonHelper.IsLikelyTruncated(ex, json, "write_file"));
    }

    [Fact]
    public void LooksLikeWriteFilePayload_DetectsKeys()
    {
        Assert.True(ToolArgumentsJsonHelper.LooksLikeWriteFilePayload("""{"filePath":"a.html","content":"hi"""));
        Assert.False(ToolArgumentsJsonHelper.LooksLikeWriteFilePayload("""{"path":"x"}"""));
    }
}
