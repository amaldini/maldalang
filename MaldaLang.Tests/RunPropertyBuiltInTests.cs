namespace MaldaLang.Tests;

using Xunit;

[Collection("Sequential")]
public class RunPropertyBuiltInTests : TestBase
{
    [Fact]
    public void RunProperty_WorksInInterpreterMode()
    {
        var source = """
property stableIdentity(x) {
    return (x + 0) == x;
}

var result = runProperty("stableIdentity", 15, 123);
print(string(result.seed));
print(string(result.iterations));
print(string(result.passed));
""";

        var output = RunProgram(source);
        Assert.Contains("123", output);
        Assert.Contains("15", output);
        Assert.Contains("true", output, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunProperty_WorksInTranspiledMode()
    {
        var source = """
property stableIdentity(x) {
    return (x + 0) == x;
}

var result = runProperty("stableIdentity", 12, 99);
print(string(result.seed));
print(string(result.iterations));
print(string(result.passed));
""";

        var run = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("99", run.StdOut);
        Assert.Contains("12", run.StdOut);
        Assert.Contains("true", run.StdOut, System.StringComparison.OrdinalIgnoreCase);
    }
}
