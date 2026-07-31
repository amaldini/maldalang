// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Runtime;
using Xunit;

namespace MaldaLang.Tests;

public class CommandApprovalTests
{
    [Theory]
    [InlineData("dotnet", CommandRisk.Safe)]
    [InlineData("powershell", CommandRisk.NeedsApproval)]
    [InlineData("cmd", CommandRisk.NeedsApproval)]
    [InlineData("rm", CommandRisk.DeniedAlways)]
    [InlineData("format", CommandRisk.DeniedAlways)]
    public void Classify_ReturnsExpectedRisk(string command, CommandRisk expected)
    {
        Assert.Equal(expected, CommandApprovalService.Classify(command));
    }

    [Fact]
    public async Task WhitelistPolicy_AutoApprovesDotnet()
    {
        var policy = new CommandApprovalPolicy
        {
            Mode = CommandApprovalMode.Whitelist,
            Whitelist = new HashSet<string>(CommandApprovalPolicy.DefaultWhitelist, StringComparer.OrdinalIgnoreCase)
        };

        var (approved, error) = await CommandApprovalService.EnsureApprovedAsync(
            null, "dotnet", new[] { "build" }, ".", policy);

        Assert.True(approved);
        Assert.Null(error);
    }

    [Fact]
    public async Task WhitelistPolicy_DeniesShellWithoutUi()
    {
        var policy = new CommandApprovalPolicy
        {
            Mode = CommandApprovalMode.Whitelist,
            Whitelist = new HashSet<string>(CommandApprovalPolicy.DefaultWhitelist, StringComparer.OrdinalIgnoreCase)
        };

        var (approved, error) = await CommandApprovalService.EnsureApprovedAsync(
            null, "powershell", new[] { "-Command", "Get-ChildItem" }, ".", policy);

        Assert.False(approved);
        Assert.Contains("list_directory", error ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("powershell", error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DenyPolicy_BlocksUnknownCommand()
    {
        var policy = new CommandApprovalPolicy
        {
            Mode = CommandApprovalMode.Deny,
            Whitelist = new HashSet<string>(CommandApprovalPolicy.DefaultWhitelist, StringComparer.OrdinalIgnoreCase)
        };

        var (approved, error) = await CommandApprovalService.EnsureApprovedAsync(
            null, "customtool", Array.Empty<string>(), ".", policy);

        Assert.False(approved);
        Assert.Contains("whitelist", error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AllowPolicy_ApprovesShell()
    {
        var policy = new CommandApprovalPolicy { Mode = CommandApprovalMode.Allow };

        var (approved, error) = await CommandApprovalService.EnsureApprovedAsync(
            null, "powershell", new[] { "-Command", "echo hi" }, ".", policy);

        Assert.True(approved);
        Assert.Null(error);
    }

    [Fact]
    public void DeniedAlways_BlockedEvenWithUserApprovedScope()
    {
        var result = MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(
            "runCommand",
            new List<MaldaLang.Interpreter.RuntimeValue>
            {
                MaldaLang.Interpreter.RuntimeValue.String("rm"),
                MaldaLang.Interpreter.RuntimeValue.Null(),
                MaldaLang.Interpreter.RuntimeValue.Null(),
                MaldaLang.Interpreter.RuntimeValue.Null()
            },
            null);

        var obj = result.AsObject();
        var stderr = obj.Get("stderr").AsString();
        Assert.Contains("not allowed", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunCommand_Pwd_ReturnsWorkingDirectory()
    {
        var result = MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(
            "runCommand",
            new List<MaldaLang.Interpreter.RuntimeValue>
            {
                MaldaLang.Interpreter.RuntimeValue.String("pwd"),
                MaldaLang.Interpreter.RuntimeValue.Null(),
                MaldaLang.Interpreter.RuntimeValue.String(System.Environment.CurrentDirectory)
            },
            null);

        var obj = result.AsObject();
        Assert.Equal(0, (int)obj.Get("exitCode").AsInteger());
        var stdout = obj.Get("stdout").AsString().Trim();
        Assert.Equal(Path.GetFullPath(System.Environment.CurrentDirectory).TrimEnd('\\'), stdout.TrimEnd('\\'), ignoreCase: true);
    }

    [Fact]
    public void RunCommand_CmdDir_ListsWorkingDirectory()
    {
        using (CommandExecutionContext.EnterUserApprovedScope())
        {
            var result = MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(
                "runCommand",
                new List<MaldaLang.Interpreter.RuntimeValue>
                {
                    MaldaLang.Interpreter.RuntimeValue.String("cmd.exe"),
                    MaldaLang.Interpreter.RuntimeValue.Array(new List<MaldaLang.Interpreter.RuntimeValue>
                    {
                        MaldaLang.Interpreter.RuntimeValue.String("/c"),
                        MaldaLang.Interpreter.RuntimeValue.String("dir")
                    })
                },
                null);

            var obj = result.AsObject();
            Assert.Equal(0, (int)obj.Get("exitCode").AsInteger());
            var stdout = obj.Get("stdout").AsString();
            Assert.False(string.IsNullOrWhiteSpace(stdout));
        }
    }
}
