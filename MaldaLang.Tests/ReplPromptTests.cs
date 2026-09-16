// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MaldaLang;
using MaldaLang.Cli;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Coverage for the interactive REPL dispatch: the <c>help</c> command must not be
/// dropped by the empty-code guard, entries share one interpreter so variables and
/// functions survive across them, a bare expression echoes its value,
/// <c>vars</c> lists user definitions, and a blank continuation line leaves
/// multiline edit without ending the session.
/// </summary>
public class ReplPromptTests : TestBase
{
    private static readonly MethodInfo ExecutePromptInput = typeof(Program)
        .GetMethod("ExecutePromptInput", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ExecutePromptInput not found on MaldaLang.Program");

    private static readonly MethodInfo ReadMultilineInput = typeof(Program)
        .GetMethod("ReadMultilineInput", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ReadMultilineInput not found on MaldaLang.Program");

    private static (string StdOut, string StdErr) InvokePromptInput(
        InputResult result,
        Interpreter.Interpreter? interpreter = null,
        ISet<string>? hostNames = null,
        ReplSessionSource? sessionSource = null)
    {
        interpreter ??= new Interpreter.Interpreter();
        sessionSource ??= new ReplSessionSource();
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
                ExecutePromptInput.Invoke(null, new object[] { result, interpreter, hostNames, sessionSource });
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }

            return (output.ToString().Replace("\r", ""), error.ToString().Replace("\r", ""));
        }
    }

    private static InputResult? InvokeReadMultilineInput(string consoleInput)
    {
        lock (_consoleLock)
        {
            var originalIn = Console.In;
            var originalOut = Console.Out;
            using var input = new StringReader(consoleInput);
            using var output = new StringWriter();
            Console.SetIn(input);
            Console.SetOut(output);
            try
            {
                return (InputResult?)ReadMultilineInput.Invoke(null, null);
            }
            finally
            {
                Console.SetIn(originalIn);
                Console.SetOut(originalOut);
            }
        }
    }

    [Fact]
    public void HelpEntry_PrintsCliHelp_InsteadOfBeingSkipped()
    {
        var (stdOut, _) = InvokePromptInput(new InputResult { Code = null, Action = "help" });

        Assert.Contains("MALDA CLI", stdOut);
        Assert.Contains("REPL commands:", stdOut);
        Assert.Contains("run | compile | transpile | vars | source | drop | replace | help | exit", stdOut);
        Assert.Contains("vars [kind]", stdOut);
        Assert.Contains("source [kind|name]", stdOut);
        Assert.Contains("drop <name>", stdOut);
        Assert.Contains("replace <name>", stdOut);
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

    [Fact]
    public void VarsEntry_EmptySession_PrintsNoUserDefinitions()
    {
        var interpreter = new Interpreter.Interpreter();
        var hostNames = ReplSessionInventory.SnapshotHostNames(interpreter);

        var (stdOut, stdErr) = InvokePromptInput(
            new InputResult { Code = "", Action = "vars" }, interpreter, hostNames);

        Assert.Equal("(no user definitions)", stdOut.Trim());
        Assert.Equal(string.Empty, stdErr.Trim());
    }

    [Fact]
    public void VarsEntry_ListsUserDefinitions_AndHidesHostNames()
    {
        var interpreter = new Interpreter.Interpreter();
        var hostNames = ReplSessionInventory.SnapshotHostNames(interpreter);

        InvokePromptInput(new InputResult { Code = "var x = 10;", Action = "run" }, interpreter, hostNames);
        InvokePromptInput(new InputResult { Code = "function double(n) { return n * 2; }", Action = "run" }, interpreter, hostNames);
        var (stdOut, _) = InvokePromptInput(
            new InputResult { Code = "", Action = "vars" }, interpreter, hostNames);

        Assert.Contains("variables:", stdOut);
        Assert.Contains("x = 10", stdOut);
        Assert.Contains("functions:", stdOut);
        Assert.Contains("double(n)", stdOut);
        Assert.DoesNotContain("math", stdOut);
        Assert.DoesNotContain("AnsiConsole", stdOut);
    }

    [Fact]
    public void VarsEntry_UnknownFilter_PrintsUsageError()
    {
        var (stdOut, _) = InvokePromptInput(new InputResult { Code = "widgets", Action = "vars" });

        Assert.Contains("Unknown vars filter 'widgets'", stdOut);
        Assert.Contains("variables, functions, classes", stdOut);
    }

    [Fact]
    public void RunEntries_LaterClass_AugmentsInsteadOfRedefining()
    {
        var interpreter = new Interpreter.Interpreter();

        InvokePromptInput(new InputResult { Code = "class Point(x, y);", Action = "run" }, interpreter);
        InvokePromptInput(
            new InputResult { Code = "class Point { function total() { return this.x + this.y; } }", Action = "run" },
            interpreter);
        var (stdOut, stdErr) = InvokePromptInput(
            new InputResult { Code = "print(new Point(3, 4).total());", Action = "run" }, interpreter);

        Assert.Contains("7", stdOut);
        Assert.Equal(string.Empty, stdErr.Trim());
    }

    [Fact]
    public void RunEntries_BracelessClass_AugmentsExistingClass()
    {
        var interpreter = new Interpreter.Interpreter();

        InvokePromptInput(new InputResult { Code = "class Point(x, y);", Action = "run" }, interpreter);
        InvokePromptInput(
            new InputResult { Code = "class Point function doubled() this.x * 2;", Action = "run" },
            interpreter);
        var (stdOut, _) = InvokePromptInput(
            new InputResult { Code = "print(new Point(3, 4).doubled());", Action = "run" }, interpreter);

        Assert.Contains("6", stdOut);
    }

    [Fact]
    public void RunEntries_ExistingInstance_SeesAddedMethod()
    {
        var interpreter = new Interpreter.Interpreter();

        InvokePromptInput(new InputResult { Code = "class Point(x, y); var p = new Point(3, 4);", Action = "run" }, interpreter);
        InvokePromptInput(
            new InputResult { Code = "class Point function total() this.x + this.y;", Action = "run" },
            interpreter);
        var (stdOut, _) = InvokePromptInput(
            new InputResult { Code = "print(p.total());", Action = "run" }, interpreter);

        Assert.Contains("7", stdOut);
    }

    [Fact]
    public void RunEntries_DuplicateMember_ReportsAlreadyDefined()
    {
        var interpreter = new Interpreter.Interpreter();

        InvokePromptInput(
            new InputResult { Code = "class Point(x, y) { function total() { return this.x + this.y; } }", Action = "run" },
            interpreter);
        var (stdOut, _) = InvokePromptInput(
            new InputResult { Code = "class Point { function total() { return 0; } }", Action = "run" },
            interpreter);

        Assert.Contains("already defined on class 'Point'", stdOut);
    }

    [Fact]
    public void VarsEntry_AfterAugmentation_ListsAddedMethod()
    {
        var interpreter = new Interpreter.Interpreter();
        var hostNames = ReplSessionInventory.SnapshotHostNames(interpreter);

        InvokePromptInput(new InputResult { Code = "class Point(x, y);", Action = "run" }, interpreter, hostNames);
        InvokePromptInput(
            new InputResult { Code = "class Point function total() this.x + this.y;", Action = "run" },
            interpreter, hostNames);
        var (stdOut, _) = InvokePromptInput(
            new InputResult { Code = "classes", Action = "vars" }, interpreter, hostNames);

        Assert.Contains("classes:", stdOut);
        Assert.Contains("Point(x, y)", stdOut);
        Assert.Contains("methods: total", stdOut);
    }

    [Fact]
    public void VarsEntry_FunctionsFilter_OmitsVariables()
    {
        var interpreter = new Interpreter.Interpreter();
        var hostNames = ReplSessionInventory.SnapshotHostNames(interpreter);

        InvokePromptInput(new InputResult { Code = "var x = 1;", Action = "run" }, interpreter, hostNames);
        InvokePromptInput(new InputResult { Code = "function ping() { return 1; }", Action = "run" }, interpreter, hostNames);
        var (stdOut, _) = InvokePromptInput(
            new InputResult { Code = "functions", Action = "vars" }, interpreter, hostNames);

        Assert.Contains("functions:", stdOut);
        Assert.Contains("ping()", stdOut);
        Assert.DoesNotContain("variables:", stdOut);
        Assert.DoesNotContain("x = 1", stdOut);
    }

    [Fact]
    public void SourceEntry_EmptySession_PrintsNoUserDefinitions()
    {
        var interpreter = new Interpreter.Interpreter();
        var sessionSource = new ReplSessionSource();

        var (stdOut, stdErr) = InvokePromptInput(
            new InputResult { Code = "", Action = "source" }, interpreter, sessionSource: sessionSource);

        Assert.Equal("(no user definitions)", stdOut.Trim());
        Assert.Equal(string.Empty, stdErr.Trim());
    }

    [Fact]
    public void SourceEntry_PrintsEnteredDefinitionSource()
    {
        var interpreter = new Interpreter.Interpreter();
        var sessionSource = new ReplSessionSource();

        InvokePromptInput(new InputResult { Code = "var x = 10;", Action = "run" }, interpreter, sessionSource: sessionSource);
        InvokePromptInput(
            new InputResult { Code = "function double(n) { return n * 2; }", Action = "run" },
            interpreter,
            sessionSource: sessionSource);
        var (stdOut, _) = InvokePromptInput(
            new InputResult { Code = "", Action = "source" }, interpreter, sessionSource: sessionSource);

        Assert.Contains("var x = 10;", stdOut);
        Assert.Contains("function double(n) { return n * 2; }", stdOut);
        Assert.DoesNotContain("variables:", stdOut);
    }

    [Fact]
    public void SourceEntry_NameFilter_PrintsOneDefinition()
    {
        var interpreter = new Interpreter.Interpreter();
        var sessionSource = new ReplSessionSource();

        InvokePromptInput(new InputResult { Code = "var x = 1;", Action = "run" }, interpreter, sessionSource: sessionSource);
        InvokePromptInput(
            new InputResult { Code = "function ping() { return 1; }", Action = "run" },
            interpreter,
            sessionSource: sessionSource);
        var (stdOut, _) = InvokePromptInput(
            new InputResult { Code = "ping", Action = "source" }, interpreter, sessionSource: sessionSource);

        Assert.Contains("function ping() { return 1; }", stdOut);
        Assert.DoesNotContain("var x", stdOut);
    }

    [Fact]
    public void SourceEntry_AfterClassAugmentation_PrintsBothSnippets()
    {
        var interpreter = new Interpreter.Interpreter();
        var sessionSource = new ReplSessionSource();

        InvokePromptInput(new InputResult { Code = "class Point(x, y);", Action = "run" }, interpreter, sessionSource: sessionSource);
        InvokePromptInput(
            new InputResult { Code = "class Point function total() this.x + this.y;", Action = "run" },
            interpreter,
            sessionSource: sessionSource);
        var (stdOut, _) = InvokePromptInput(
            new InputResult { Code = "Point", Action = "source" }, interpreter, sessionSource: sessionSource);

        Assert.Contains("class Point(x, y);", stdOut);
        Assert.Contains("class Point function total() this.x + this.y;", stdOut);
    }

    [Fact]
    public void DropEntry_RemovesFunction_SoNameIsUndefined()
    {
        var interpreter = new Interpreter.Interpreter();
        var hostNames = ReplSessionInventory.SnapshotHostNames(interpreter);
        var sessionSource = new ReplSessionSource();

        InvokePromptInput(
            new InputResult { Code = "function ping() { return 1; }", Action = "run" },
            interpreter, hostNames, sessionSource);
        var (dropOut, _) = InvokePromptInput(
            new InputResult { Code = "ping", Action = "drop" }, interpreter, hostNames, sessionSource);
        var (callOut, _) = InvokePromptInput(
            new InputResult { Code = "print(ping());", Action = "run" }, interpreter, hostNames, sessionSource);
        var (sourceOut, _) = InvokePromptInput(
            new InputResult { Code = "", Action = "source" }, interpreter, hostNames, sessionSource);

        Assert.Contains("Dropped function 'ping'", dropOut);
        Assert.Contains("Undefined variable", callOut);
        Assert.Equal("(no user definitions)", sourceOut.Trim());
    }

    [Fact]
    public void ReplaceEntry_PrintsPreviousSource_ThenNewDefinitionBinds()
    {
        var interpreter = new Interpreter.Interpreter();
        var hostNames = ReplSessionInventory.SnapshotHostNames(interpreter);
        var sessionSource = new ReplSessionSource();

        InvokePromptInput(
            new InputResult { Code = "function ping() { return 1; }", Action = "run" },
            interpreter, hostNames, sessionSource);
        var (replaceOut, _) = InvokePromptInput(
            new InputResult { Code = "ping", Action = "replace" }, interpreter, hostNames, sessionSource);
        InvokePromptInput(
            new InputResult { Code = "function ping() { return 7; }", Action = "run" },
            interpreter, hostNames, sessionSource);
        var (callOut, _) = InvokePromptInput(
            new InputResult { Code = "print(ping());", Action = "run" }, interpreter, hostNames, sessionSource);

        Assert.Contains("Dropped function 'ping'", replaceOut);
        Assert.Contains("function ping() { return 1; }", replaceOut);
        Assert.Contains("Enter a new definition to replace it.", replaceOut);
        Assert.Contains("7", callOut);
    }

    [Fact]
    public void DropEntry_AllowsClassToBeRedefinedWithPrimaryConstructor()
    {
        var interpreter = new Interpreter.Interpreter();
        var hostNames = ReplSessionInventory.SnapshotHostNames(interpreter);
        var sessionSource = new ReplSessionSource();

        InvokePromptInput(new InputResult { Code = "class Point(x, y);", Action = "run" }, interpreter, hostNames, sessionSource);
        InvokePromptInput(
            new InputResult { Code = "class Point function total() this.x + this.y;", Action = "run" },
            interpreter, hostNames, sessionSource);
        var (dropOut, _) = InvokePromptInput(
            new InputResult { Code = "Point", Action = "drop" }, interpreter, hostNames, sessionSource);
        var (redefineOut, redefineErr) = InvokePromptInput(
            new InputResult { Code = "class Point(a, b);", Action = "run" }, interpreter, hostNames, sessionSource);
        var (callOut, _) = InvokePromptInput(
            new InputResult { Code = "print(new Point(3, 4).a);", Action = "run" }, interpreter, hostNames, sessionSource);

        Assert.Contains("Dropped class 'Point'", dropOut);
        Assert.Equal(string.Empty, redefineOut.Trim());
        Assert.Equal(string.Empty, redefineErr.Trim());
        Assert.Contains("3", callOut);
    }

    [Fact]
    public void DropEntry_RefusesHostName()
    {
        var interpreter = new Interpreter.Interpreter();
        var hostNames = ReplSessionInventory.SnapshotHostNames(interpreter);
        var sessionSource = new ReplSessionSource();

        var (stdOut, _) = InvokePromptInput(
            new InputResult { Code = "math", Action = "drop" }, interpreter, hostNames, sessionSource);

        Assert.Contains("Cannot drop host name 'math'", stdOut);
    }

    [Fact]
    public void DropEntry_UnknownName_PrintsError()
    {
        var (stdOut, _) = InvokePromptInput(new InputResult { Code = "missing", Action = "drop" });

        Assert.Contains("No user definition named 'missing'", stdOut);
    }

    [Fact]
    public void ContinuationBlankLine_DoesNotEndTheSession()
    {
        var result = InvokeReadMultilineInput("function ping() {\n\n");

        Assert.NotNull(result);
        Assert.Equal("run", result!.Action);
        Assert.Contains("function ping()", result.Code);
    }

    [Fact]
    public void ContinuationBlankLine_AfterClosedBlock_SubmitsTheBuffer()
    {
        var result = InvokeReadMultilineInput("function ping() {\nreturn 1;\n}\n\n");

        Assert.NotNull(result);
        Assert.Equal("run", result!.Action);
        Assert.Contains("function ping()", result.Code);
        Assert.Contains("return 1;", result.Code);
        Assert.Contains("}", result.Code);
    }

    [Fact]
    public void ContinuationBlankLine_AfterClosedBlock_DefinesTheFunction()
    {
        var result = InvokeReadMultilineInput("function ping() {\nreturn 1;\n}\n\n");
        Assert.NotNull(result);

        var interpreter = new Interpreter.Interpreter();
        InvokePromptInput(result!, interpreter);
        var (stdOut, stdErr) = InvokePromptInput(
            new InputResult { Code = "print(ping());", Action = "run" }, interpreter);

        Assert.Contains("1", stdOut);
        Assert.Equal(string.Empty, stdErr.Trim());
    }

    [Fact]
    public void ContinuationEof_EndsTheSession()
    {
        var result = InvokeReadMultilineInput("function ping() {\n");

        Assert.Null(result);
    }

    [Fact]
    public void BlankFirstLine_StaysInSessionAsNoOp()
    {
        var result = InvokeReadMultilineInput("\n");

        Assert.NotNull(result);
        Assert.Equal("run", result!.Action);
    }
}
