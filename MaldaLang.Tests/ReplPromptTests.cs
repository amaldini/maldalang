// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Reflection;
using MaldaLang;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Regression coverage for the interactive REPL dispatch. The banner advertises
/// 'help', but <c>help</c> carries no code, so a naive empty-code guard drops it
/// before dispatch and the command silently does nothing.
/// </summary>
public class ReplPromptTests : TestBase
{
    private static readonly MethodInfo ExecutePromptInput = typeof(Program)
        .GetMethod("ExecutePromptInput", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ExecutePromptInput not found on MaldaLang.Program");

    private static (string StdOut, string StdErr) InvokePromptInput(InputResult result)
    {
        lock (_consoleLock)
        {
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            using var output = new StringWriter();
            using var error = new StringWriter();
            Console.SetOut(output);
            Console.SetError(error);
            try
            {
                ExecutePromptInput.Invoke(null, new object[] { result });
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }

            return (output.ToString().Replace("\r", ""), error.ToString().Replace("\r", ""));
        }
    }

    [Fact]
    public void HelpEntry_PrintsCliHelp_InsteadOfBeingSkipped()
    {
        var (stdOut, _) = InvokePromptInput(new InputResult { Code = null, Action = "help" });

        Assert.Contains("MALDA CLI", stdOut);
        Assert.Contains("REPL commands:", stdOut);
        Assert.Contains("run | compile | transpile | help | exit", stdOut);
    }

    [Fact]
    public void RunEntry_WithNoCode_IsANoOp()
    {
        var (stdOut, stdErr) = InvokePromptInput(new InputResult { Code = null, Action = "run" });

        Assert.Equal(string.Empty, stdOut.Trim());
        Assert.Equal(string.Empty, stdErr.Trim());
    }

    [Fact]
    public void RunEntry_WithWhitespaceCode_IsANoOp()
    {
        var (stdOut, stdErr) = InvokePromptInput(new InputResult { Code = "   ", Action = "run" });

        Assert.Equal(string.Empty, stdOut.Trim());
        Assert.Equal(string.Empty, stdErr.Trim());
    }
}
