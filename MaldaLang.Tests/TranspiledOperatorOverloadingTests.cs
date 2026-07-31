using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspiledOperatorOverloadingTests
{
    [Fact]
    public void Transpiled_OperatorOverloads_AreInvoked_AndBuiltInsStillWork()
    {
        var source = @"
            class Value {
                public var data;

                function Value(data) {
                    this.data = data;
                }

                public function __add__(other) {
                    return data + other.data;
                }

                public function __mul__(other) {
                    return data * other.data;
                }

                public function __neg__() {
                    return 0 - data;
                }

                public function __lt__(other) {
                    return data < other.data;
                }
            }

            var a = new Value(2);
            var b = new Value(3);
            print(a + b);
            print(a * b);
            print(-a);
            print(a < b);
            print(1 + 2);
            print(""a"" + ""b"");
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        var lines = result.StdOut.Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);

        Assert.True(result.ExitCode == 0, $"ExitCode={result.ExitCode}\nStdErr={result.StdErr}\nStdOut={result.StdOut}");
        Assert.Equal(new[] { "5", "6", "-2", "true", "3", "ab" }, lines);
    }

    [Fact]
    public void Transpiled_MissingOperatorMethod_FallsBackToBuiltInAndErrors()
    {
        var source = @"
            class Box {
                public var value;
                function Box(value) {
                    this.value = value;
                }
            }

            var b = new Box(10);
            print(b + 1);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Operands must be", result.StdErr);
    }

    [Fact]
    public void Transpiled_OperatorOverloadException_Propagates()
    {
        var source = @"
            class Explosive {
                public function __add__(other) {
                    throw ""boom"";
                }
            }

            var a = new Explosive();
            var b = new Explosive();
            print(a + b);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.StdErr.Contains("boom") || result.StdErr.Contains("target invocation"),
            $"Expected overload exception in stderr. Actual: {result.StdErr}");
    }

    [Fact]
    public void Transpiled_RightHandOperatorOverloads_AreInvoked_WhenLeftDoesNotDefineOverload()
    {
        var source = @"
            class RightValue {
                public var value;

                function RightValue(value) {
                    this.value = value;
                }

                public function __radd__(other) {
                    return other + value;
                }

                public function __rsub__(other) {
                    return other - value;
                }

                public function __req__(other) {
                    return other == value;
                }
            }

            var rhs = new RightValue(7);
            print(5 + rhs);
            print(10 - rhs);
            print(7 == rhs);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        var lines = result.StdOut.Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);

        Assert.True(result.ExitCode == 0, $"ExitCode={result.ExitCode}\nStdErr={result.StdErr}\nStdOut={result.StdOut}");
        Assert.Equal(new[] { "12", "3", "true" }, lines);
    }
}
