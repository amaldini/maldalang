using Xunit;

namespace MaldaLang.Tests;

public class TranspileFailureDebugHintTests
{
    [Fact]
    public void TranspileFailureDebugHint_PointsAtDebuggingDocAndLineMapping()
    {
        var hint = global::MaldaLang.Compiler.Compiler.TranspileFailureDebugHint;
        Assert.Contains("docs/debugging-transpile.md", hint);
        Assert.Contains(".malda", hint);
        Assert.Contains("#line default", hint);
    }
}
