// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Reflection;
using MaldaLang;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Coverage for the interactive REPL dispatch: the <c>help</c> command must not be
/// dropped by the empty-code guard, entries share one interpreter so variables and
/// functions survive across them, and a bare expression echoes its value.
/// </summary>
public class ReplPromptTests : TestBase
{
    private static readonly MethodInfo ExecutePromptInput = typeof(Program)
        .GetMethod("ExecutePromptInput", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ExecutePromptInput not found on MaldaLang.Program");

    private static (string StdOut, string StdErr) InvokePromptInput(
        InputResult result,
        Interpreter.Interpreter? interpreter = null)
    {
        interpreter ??= new Interpreter.Interpreter();
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
                ExecutePromptInput.Invoke(null, new object[] { result, interpreter });
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

    [Fact]
    public void RunEntries_ShareOneInterpreter_SoLaterEntriesSeeEarlierVariables()
    {
        var interpreter = new Interpreter.Interpreter();

        InvokePromptInput(new InputResult { Code = "var x = 10;", Action = "run" }, interpreter);
        var (stdOut, stdErr) = InvokePromptInput(
            new InputResult { Code = "print(x * 2);", Action = "run" }, interpreter);

        Assert.Contains("20", stdOut);
        Assert.DoesNotContain("Undefined variable", stdOut);
        Assert.Equal(string.Empty, stdErr.Trim());
    }

    [Fact]
    public void RunEntries_ShareOneInterpreter_SoLaterEntriesSeeEarlierFunctions()
    {
        var interpreter = new Interpreter.Interpreter();

        InvokePromptInput(new InputResult { Code = "function double(n) { return n * 2; }", Action = "run" }, interpreter);
        var (stdOut, _) = InvokePromptInput(
            new InputResult { Code = "print(double(21));", Action = "run" }, interpreter);

        Assert.Contains("42", stdOut);
    }

    [Fact]
    public void BareExpression_EchoesItsValue()
    {
        var (stdOut, _) = InvokePromptInput(new InputResult { Code = "1 + 2", Action = "run" });

        Assert.Equal("3", stdOut.Trim());
    }

    [Fact]
    public void BareExpression_SeesSessionVariables()
    {
        var interpreter = new Interpreter.Interpreter();

        InvokePromptInput(new InputResult { Code = "var x = 10;", Action = "run" }, interpreter);
        var (stdOut, _) = InvokePromptInput(new InputResult { Code = "x * 2", Action = "run" }, interpreter);

        Assert.Equal("20", stdOut.Trim());
    }

    [Fact]
    public void PrintCall_DoesNotEchoTwice()
    {
        var (stdOut, _) = InvokePromptInput(new InputResult { Code = "print(7);", Action = "run" });

        Assert.Equal("7", stdOut.Trim());
    }

    [Fact]
    public void AssignmentEntry_AssignsInsteadOfEchoing()
    {
        var interpreter = new Interpreter.Interpreter();

        InvokePromptInput(new InputResult { Code = "var x = 1;", Action = "run" }, interpreter);
        var (assignOut, _) = InvokePromptInput(new InputResult { Code = "x = 5;", Action = "run" }, interpreter);
        var (echoOut, _) = InvokePromptInput(new InputResult { Code = "x", Action = "run" }, interpreter);

        Assert.Equal(string.Empty, assignOut.Trim());
        Assert.Equal("5", echoOut.Trim());
    }
}
