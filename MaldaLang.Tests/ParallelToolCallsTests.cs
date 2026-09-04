// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using Xunit;

namespace MaldaLang.Tests;

public class ParallelToolCallsTests
{
    [Theory]
    [InlineData("read_file", true)]
    [InlineData("grep", true)]
    [InlineData("glob", true)]
    [InlineData("list_directory", true)]
    [InlineData("get_symbols", true)]
    [InlineData("get_parse_errors", true)]
    [InlineData("check_malda", true)]
    [InlineData("validate_json", true)]
    [InlineData("test_malda", false)]
    [InlineData("web_search", true)]
    [InlineData("web_fetch", true)]
    [InlineData("git_status", true)]
    [InlineData("write_file", false)]
    [InlineData("replace_in_file", false)]
    [InlineData("run_command", false)]
    [InlineData("ask_user", false)]
    public void IsParallelSafeBuiltInTool_ClassifiesKnownTools(string toolName, bool expected)
    {
        var tool = new ToolInstance();
        tool.Initialize(toolName, "test tool", RuntimeValue.Null(), null, ".");

        var actual = ConversationInstance.IsParallelSafeBuiltInTool(tool, toolName);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsParallelSafeBuiltInTool_RejectsCustomHandlers()
    {
        var tool = new ToolInstance();
        tool.Initialize("read_file", "custom read", RuntimeValue.Null(), null, ".");

        var function = new MaldaLang.Interpreter.FunctionValue(null, null, false, null);
        tool.SetFunctionHandler(function, new MaldaLang.Interpreter.Interpreter());

        Assert.False(ConversationInstance.IsParallelSafeBuiltInTool(tool, "read_file"));
    }

    [Fact]
    public void IsParallelToolCallsEnabled_DefaultsToTrueWhenUnset()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_PARALLEL_TOOL_CALLS");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_PARALLEL_TOOL_CALLS", null);
            ResetParallelToolCallsCache();

            Assert.True(ConversationInstance.IsParallelToolCallsEnabled());
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_PARALLEL_TOOL_CALLS", previous);
            ResetParallelToolCallsCache();
        }
    }

    [Fact]
    public void IsParallelToolCallsEnabled_CanBeDisabledViaEnv()
    {
        var previous = System.Environment.GetEnvironmentVariable("MALDA_PARALLEL_TOOL_CALLS");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_PARALLEL_TOOL_CALLS", "false");
            ResetParallelToolCallsCache();

            Assert.False(ConversationInstance.IsParallelToolCallsEnabled());
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_PARALLEL_TOOL_CALLS", previous);
            ResetParallelToolCallsCache();
        }
    }

    private static void ResetParallelToolCallsCache()
    {
        typeof(ConversationInstance)
            .GetField("_parallelToolCallsEnabled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);
    }
}
