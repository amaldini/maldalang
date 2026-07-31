using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class OperatorOverloadingTests : TestBase
{
    [Fact]
    public void Interpreter_OperatorOverloads_AreInvoked_AndBuiltInsStillWork()
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

        var output = RunProgram(source);
        var lines = output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);

        Assert.Equal(new[] { "5", "6", "-2", "true", "3", "ab" }, lines);
    }

    [Fact]
    public void Interpreter_MissingOperatorMethod_FallsBackToBuiltInAndErrors()
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

        var ex = Assert.ThrowsAny<System.Exception>(() => RunProgram(source));
        Assert.Contains("Unable to cast object of type", ex.Message);
    }

    [Fact]
    public void Interpreter_OperatorOverloadException_Propagates()
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

        var ex = Assert.ThrowsAny<System.Exception>(() => RunProgram(source));
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public void Interpreter_RightHandOperatorOverloads_AreInvoked_WhenLeftDoesNotDefineOverload()
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

        var output = RunProgram(source);
        var lines = output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);

        Assert.Equal(new[] { "12", "3", "true" }, lines);
    }
}
