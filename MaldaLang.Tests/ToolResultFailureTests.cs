// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

public class ToolResultFailureTests
{
    [Fact]
    public void IsToolResultFailure_DetectsErrorString()
    {
        var result = RuntimeValue.String("Error: oldText not found");

        Assert.True(ConversationInstance.IsToolResultFailure(result, out var summary));
        Assert.Equal("Error: oldText not found", summary);
    }

    [Fact]
    public void IsToolResultFailure_DetectsStructuredEditFailure()
    {
        var obj = new JsonObject();
        obj.Set("success", RuntimeValue.Boolean(false));
        obj.Set("applied", RuntimeValue.Integer(0));
        obj.Set("totalEdits", RuntimeValue.Integer(3));
        obj.Set("failedEdit", RuntimeValue.Integer(2));
        obj.Set("error", RuntimeValue.String("Edit 2/3 failed: oldText not found"));

        Assert.True(ConversationInstance.IsToolResultFailure(RuntimeValue.Object(obj), out var summary));
        Assert.Equal("Edit 2/3 failed: oldText not found", summary);
    }

    [Fact]
    public void IsToolResultFailure_IgnoresSuccessfulStructuredResult()
    {
        var obj = new JsonObject();
        obj.Set("success", RuntimeValue.Boolean(true));
        obj.Set("applied", RuntimeValue.Integer(3));

        Assert.False(ConversationInstance.IsToolResultFailure(RuntimeValue.Object(obj), out _));
    }

    [Fact]
    public void IsWriteToolName_RecognizesEditFile()
    {
        Assert.True(ConversationInstance.IsWriteToolName("edit_file"));
        Assert.True(ConversationInstance.IsWriteToolName("replace_in_file"));
        Assert.False(ConversationInstance.IsWriteToolName("read_file"));
    }
}
