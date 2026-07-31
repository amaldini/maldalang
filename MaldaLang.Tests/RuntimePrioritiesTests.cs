using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

namespace MaldaLang.Tests;

public class RuntimePrioritiesTests : TestBase
{
    [Fact]
    public void RuntimeDiagnostics_FormatForConsole_IncludesLocationAndSource()
    {
        var interpreter = new Interpreter.Interpreter(currentFile: "sample.malda");
        interpreter.SetSourceCode("print(1);\nvar value = [1][3];\nprint(value);");

        var formatted = RuntimeDiagnostics.FormatForConsole(
            new RuntimeException("Array index out of bounds.", 2, "sample.malda"),
            interpreter);

        Assert.Contains("Error: Array index out of bounds.", formatted);
        Assert.Contains("Location: sample.malda:2", formatted);
        Assert.Contains("Source: var value = [1][3];", formatted);
    }

    [Fact]
    public void WebRuntime_CreateErrorFromException_CanIncludeDiagnostics()
    {
        var payload = WebRuntimeHelpers.CreateErrorFromException(
            new RuntimeException("Undefined variable 'pivot'.", 4, "program.malda", "print(pivot);"),
            "corr-42",
            out var statusCode,
            includeDiagnostics: true);

        var root = Assert.IsType<JsonObject>(payload.AsObject());
        Assert.Equal(500, statusCode);
        Assert.Equal("InternalServerError", root.Get("error", null).AsString());
        Assert.Equal("corr-42", root.Get("correlationId", null).AsString());

        var diagnostics = Assert.IsType<JsonObject>(root.Get("diagnostics", null).AsObject());
        Assert.Equal("program.malda", diagnostics.Get("file", null).AsString());
        Assert.Equal(4, diagnostics.Get("line", null).AsInteger());
        Assert.Equal("print(pivot);", diagnostics.Get("sourceLine", null).AsString());
    }

    [Fact]
    public void ArrayAndStringEnhancements_AreConsistentInInterpreterAndTranspiledMode()
    {
        var source = @"
            var values = [10, 2, 30];
            values.sort();
            print(values.join("",""));
            print(values.at(-1));
            print(values.get(10, ""fallback""));
            print([1, 2, 3].includes(2.0));
            print(""Hello"".replace(""He"", ""Ye""));
            print(""a,b"".split("","").length);
        ";

        var interpreted = RunProgram(source);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, transpiled.ExitCode);
        Assert.Equal(interpreted, transpiled.StdOut);
        Assert.Contains("2,10,30", interpreted);
        Assert.Contains("30", interpreted);
        Assert.Contains("fallback", interpreted);
    }

    [Fact]
    public void TextBuiltIns_WorkInInterpreterAndTranspiledMode()
    {
        var source = @"
            print(normalizeText(""  Città, 42! ""));
            print(tokenize(""Uno, due, tre!"").join(""|""));
            var overlap = tokenOverlap(""cpu memoria cache"", ""cache cpu"");
            print(overlap.sharedCount);
            print(round(similarity(""equazione lineare"", ""equazioni lineari"") * 100));
            print(round(similarity(""processore"", ""processor"", ""char-ngram"") * 100));
            print(extractNumbers(""x=12, y=3.5, z=-2"").join(""|""));
        ";

        var interpreted = RunProgram(source);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, transpiled.ExitCode);
        Assert.Equal(interpreted, transpiled.StdOut);
        Assert.Contains("citta 42", interpreted);
        Assert.Contains("uno|due|tre", interpreted);
        Assert.Contains("12|3.5|-2", interpreted);
    }
}
