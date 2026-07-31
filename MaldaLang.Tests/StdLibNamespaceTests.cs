using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

public class StdLibNamespaceTests : TestBase
{
    [Fact]
    public void MathNamespace_AbsMatchesFlatBuiltin()
    {
        var flat = RunProgram("print(abs(-3));");
        var namespaced = RunProgram("print(math.abs(-3));");
        var legacyModule = RunProgram("print(Math.abs(-3));");

        Assert.Equal("3", flat.Trim());
        Assert.Equal(flat, namespaced);
        Assert.Equal(flat, legacyModule);
    }

    [Fact]
    public void StrNamespace_SplitMatchesFlatBuiltin()
    {
        var flat = RunProgram("print(join(split(\"a,b\", \",\"), \"|\"));");
        var namespaced = RunProgram("print(str.join(str.split(\"a,b\", \",\"), \"|\"));");

        Assert.Equal("a|b", flat.Trim());
        Assert.Equal(flat, namespaced);
    }

    [Fact]
    public void IoNamespace_ReadFileMatchesFlatBuiltin()
    {
        var path = Path.Combine(Path.GetTempPath(), $"malda-io-ns-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "phase-1.2");

        try
        {
            var source = $"print(readFile(\"{path.Replace("\\", "\\\\")}\"));";
            var ioSource = $"print(io.readFile(\"{path.Replace("\\", "\\\\")}\"));";

            Assert.Equal("phase-1.2", RunProgram(source).Trim());
            Assert.Equal(RunProgram(source), RunProgram(ioSource));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void StdLibNamespaces_AreDefinedInGlobals()
    {
        var source = """
            print(typeOf(math));
            print(typeOf(str));
            print(typeOf(io));
            print(typeOf(Math));
            """;

        var output = RunProgram(source).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, output.Length);
        Assert.Equal("object", output[0].Trim());
        Assert.Equal("object", output[1].Trim());
        Assert.Equal("object", output[2].Trim());
        Assert.Equal("object", output[3].Trim());
    }

    [Fact]
    public void MathNamespace_WorksInTranspiledMode()
    {
        var source = "print(math.round(2.6));";
        var interpreted = RunProgram(source);
        var transpiled = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal("3", interpreted.Trim());
        Assert.Equal(0, transpiled.ExitCode);
        Assert.Equal(interpreted, transpiled.StdOut);
    }

    [Fact]
    public void LanguageService_WarnsOnDeprecatedFlatMathCall()
    {
        var languageService = new LanguageService();
        var warnings = languageService
            .GetDiagnostics("abs(-1);", "stdlib-ns.malda")
            .Where(d => d.Source == "malda-style" && d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        Assert.Contains(warnings, d => d.Message.Contains("deprecated flat alias", StringComparison.Ordinal));
        Assert.Contains(warnings, d => d.Message.Contains("math.abs", StringComparison.Ordinal));
    }

    [Fact]
    public void LanguageService_WarnsOnDeprecatedMathModuleAlias()
    {
        var languageService = new LanguageService();
        var warnings = languageService
            .GetDiagnostics("Math.sqrt(9);", "math-alias.malda")
            .Where(d => d.Source == "malda-style" && d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        Assert.Contains(warnings, d => d.Message.Contains("deprecated module alias", StringComparison.Ordinal));
        Assert.Contains(warnings, d => d.Message.Contains("math.sqrt", StringComparison.Ordinal));
    }
}
