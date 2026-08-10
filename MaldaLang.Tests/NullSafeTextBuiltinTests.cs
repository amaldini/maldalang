// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class NullSafeTextBuiltinTests : TestBase
{
    public NullSafeTextBuiltinTests()
    {
        BuiltInFunctions.ClearGetEnvCacheForTesting();
    }

    [Fact]
    public void StrText_MapsNullToEmpty_UnlikeString()
    {
        var output = RunProgram("""
            print("s=" + string(null));
            print("t=" + str.text(null));
            print("n=" + str.text(42));
            print("tt=" + str.trimText(null));
            print("th=" + str.trimText("  hi  "));
            """).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("s=null", output[0].Trim());
        Assert.Equal("t=", output[1].Trim());
        Assert.Equal("n=42", output[2].Trim());
        Assert.Equal("tt=", output[3].Trim());
        Assert.Equal("th=hi", output[4].Trim());
    }

    [Fact]
    public void IoGetEnvOr_NeverReturnsNull()
    {
        var missing = "MALDA_TEST_GETENVOR_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(missing, null);
        BuiltInFunctions.ClearGetEnvCacheForTesting();

        try
        {
            var output = RunProgram($"""
                print(io.getEnv("{missing}") == null);
                print("e=" + io.getEnvOr("{missing}"));
                print("f=" + io.getEnvOr("{missing}", "fallback"));
                print("tr=" + str.trim(io.getEnvOr("{missing}")));
                """).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal("true", output[0].Trim());
            Assert.Equal("e=", output[1].Trim());
            Assert.Equal("f=fallback", output[2].Trim());
            Assert.Equal("tr=", output[3].Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable(missing, null);
            BuiltInFunctions.ClearGetEnvCacheForTesting();
        }
    }

    [Fact]
    public void NullSafeHelpers_MatchInTranspiledMode()
    {
        var missing = "MALDA_TEST_GETENVOR_T_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(missing, null);
        BuiltInFunctions.ClearGetEnvCacheForTesting();

        try
        {
            var source = $"""
                print("t=" + str.text(null));
                print("x=" + str.trimText("  x  "));
                print("e=" + io.getEnvOr("{missing}", "ok"));
                """;
            var interpreted = RunProgram(source);
            var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source);
            Assert.Equal(0, transpiled.ExitCode);
            Assert.Equal(interpreted.Replace("\r", ""), transpiled.StdOut.Replace("\r", ""));
        }
        finally
        {
            Environment.SetEnvironmentVariable(missing, null);
            BuiltInFunctions.ClearGetEnvCacheForTesting();
        }
    }
}
