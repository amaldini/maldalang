// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Eval;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class EvalSuiteTests : TestBase
{
    [Fact]
    public void ParsesSuiteAndExpect()
    {
        var output = RunProgram("""
            suite "t" {
                case "offline" {
                    expect(true);
                }
            }
            print("ok");
            """);
        Assert.Equal("ok", output.Trim());
    }
}

[Collection("Sequential")]
public class EvalCommandTests : TestBase
{
    [Fact]
    public void EvalCommand_OfflineSuite_Passes()
    {
        var dir = CreateTempDirectory();
        var path = Path.Combine(dir, "suite.malda");
        File.WriteAllText(path, """
            suite "t" {
                case "offline" {
                    expect(1 == 1);
                }
            }
            """);
        var runner = new EvalCommandRunner();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = runner.Run(new[] { path }, stdout, stderr);
        Assert.Equal(0, code);
        Assert.Contains("100", stdout.ToString());
    }
}
