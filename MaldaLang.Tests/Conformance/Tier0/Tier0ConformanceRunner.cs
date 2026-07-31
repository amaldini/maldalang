using System.Text.Json;
using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Runtime.Actors;

namespace MaldaLang.Tests.Conformance.Tier0;

public enum Tier0BackendKind
{
    Interpreter,
    CSharp,
    JavaScript
}

public sealed class Tier0CaseRunResult
{
    public required Tier0ConformanceCase Case { get; init; }
    public required Tier0BackendKind Backend { get; init; }
    public bool Passed { get; init; }
    public string? Expected { get; init; }
    public string? Actual { get; init; }
    public string? Error { get; init; }
}

public sealed class Tier0BackendMatrixReport
{
    public int TotalCases { get; init; }
    public int InterpreterEnabled { get; init; }
    public int InterpreterPassed { get; init; }
    public int CSharpEnabled { get; init; }
    public int CSharpPassed { get; init; }
    public int JavaScriptEnabled { get; init; }
    public int JavaScriptPassed { get; init; }
    public int JavaScriptSkipped { get; init; }
    public int CSharpSkipped { get; init; }

    public double JavaScriptPassRate =>
        JavaScriptEnabled == 0 ? 1.0 : (double)JavaScriptPassed / JavaScriptEnabled;
    public IReadOnlyList<Tier0CaseRunResult> Failures { get; init; } = [];

    public double InterpreterPassRate =>
        InterpreterEnabled == 0 ? 1.0 : (double)InterpreterPassed / InterpreterEnabled;

    public double CSharpPassRate =>
        CSharpEnabled == 0 ? 1.0 : (double)CSharpPassed / CSharpEnabled;

    public string ToSummaryString()
    {
        return string.Join(System.Environment.NewLine,
            $"Tier 0 backend matrix ({TotalCases} cases)",
            $"  interpreter: {InterpreterPassed}/{InterpreterEnabled} ({InterpreterPassRate:P1})",
            $"  csharp:      {CSharpPassed}/{CSharpEnabled} ({CSharpPassRate:P1})",
            JavaScriptEnabled > 0
                ? $"  javascript:  {JavaScriptPassed}/{JavaScriptEnabled} ({JavaScriptPassRate:P1})"
                : $"  javascript:  {JavaScriptSkipped} skipped (documented)");
    }

    public string ToMarkdown()
    {
        var lines = new List<string>
        {
            "# Tier 0 backend parity report",
            "",
            $"Generated: {DateTimeOffset.UtcNow:u}",
            "",
            "| Backend | Passed | Enabled | Pass rate |",
            "|---------|--------|---------|-----------|",
            $"| Interpreter | {InterpreterPassed} | {InterpreterEnabled} | {InterpreterPassRate:P1} |",
            $"| C# transpile | {CSharpPassed} | {CSharpEnabled} | {CSharpPassRate:P1} |",
            JavaScriptEnabled > 0
                ? $"| JavaScript | {JavaScriptPassed} | {JavaScriptEnabled} | {JavaScriptPassRate:P1} |"
                : $"| JavaScript | — | — | {JavaScriptSkipped} skipped (documented) |",
            ""
        };

        if (Failures.Count > 0)
        {
            lines.Add("## Failures");
            lines.Add("");
            foreach (var failure in Failures)
            {
                lines.Add($"- **{failure.Case.Id}** `{failure.Case.File}` [{failure.Backend}]: " +
                          $"{failure.Error ?? "output mismatch"}");
            }
        }
        else
        {
            lines.Add("All enabled backends met parity thresholds.");
        }

        return string.Join(System.Environment.NewLine, lines);
    }

    public string ToJson()
    {
        var payload = new
        {
            generatedAt = DateTimeOffset.UtcNow,
            totalCases = TotalCases,
            interpreter = new
            {
                enabled = InterpreterEnabled,
                passed = InterpreterPassed,
                passRate = InterpreterPassRate
            },
            csharp = new
            {
                enabled = CSharpEnabled,
                passed = CSharpPassed,
                passRate = CSharpPassRate,
                skipped = CSharpSkipped
            },
            javascript = new
            {
                enabled = JavaScriptEnabled,
                passed = JavaScriptPassed,
                passRate = JavaScriptPassRate,
                skipped = JavaScriptSkipped
            },
            failures = Failures.Select(f => new
            {
                id = f.Case.Id,
                file = f.Case.File,
                backend = f.Backend.ToString(),
                error = f.Error,
                expected = f.Expected,
                actual = f.Actual
            })
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void WriteReportArtifacts(Tier0BackendMatrixReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "parity-report.json"), report.ToJson());
        File.WriteAllText(Path.Combine(outputDirectory, "parity-report.md"), report.ToMarkdown());
    }
}

public static class Tier0ConformanceRunner
{
    public const double CSharpParityMinimum = 0.95;
    public const double JavaScriptParityMinimum = 1.0;

    public static string NormalizeOutput(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
    }

    public static void ResetRuntimeState()
    {
        ActorRuntime.ClearInstanceForTesting();
        ActorsRuntime.ResetForTesting();
        ToolRegistry.Instance.ClearUserDefinedTools();
        BuiltInFunctions.ClearGetEnvCacheForTesting();
    }

    public static async Task<string> RunInterpreterAsync(string source)
    {
        ResetRuntimeState();
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        var interpreter = new Interpreter.Interpreter();
        await using var capture = new ConsoleCapture();
        await interpreter.InterpretAsync(statements);
        await Task.Delay(100);
        return NormalizeOutput(capture.Output);
    }

    public static async Task<string> RunJavaScriptAsync(string source, string? sourceFilePath = null) =>
        await Tier0JavaScriptRunner.RunAsync(source, sourceFilePath);

    public static string RunCSharp(string maldaPath)
    {
        // Compile from a temp copy so publish output never lands in conformance/tier0/cases/.
        var source = File.ReadAllText(maldaPath);
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Transpiled exit code {result.ExitCode}. stderr: {result.StdErr}");
        return NormalizeOutput(result.StdOut);
    }

    public static async Task<Tier0CaseRunResult> RunCaseAsync(
        Tier0ConformanceCase testCase,
        Tier0BackendKind backend)
    {
        if (!File.Exists(testCase.MaldaPath))
            return Fail(testCase, backend, error: $"Missing source: {testCase.MaldaPath}");

        var expected = NormalizeOutput(await File.ReadAllTextAsync(testCase.ExpectPath));
        try
        {
            var source = await File.ReadAllTextAsync(testCase.MaldaPath);
            var actual = backend switch
            {
                Tier0BackendKind.Interpreter => await RunInterpreterAsync(source),
                Tier0BackendKind.CSharp => RunCSharp(testCase.MaldaPath),
                Tier0BackendKind.JavaScript => await RunJavaScriptAsync(source),
                _ => throw new ArgumentOutOfRangeException(nameof(backend))
            };

            if (actual == expected)
            {
                return new Tier0CaseRunResult
                {
                    Case = testCase,
                    Backend = backend,
                    Passed = true,
                    Expected = expected,
                    Actual = actual
                };
            }

            return Fail(testCase, backend, expected, actual);
        }
        catch (Exception ex)
        {
            return Fail(testCase, backend, expected, error: ex.Message);
        }
        finally
        {
            ResetRuntimeState();
        }
    }

    public static async Task<Tier0BackendMatrixReport> BuildMatrixReportAsync()
    {
        var cases = Tier0ConformanceManifest.LoadCases();
        var results = new List<Tier0CaseRunResult>();
        var interpreterPassed = 0;
        var interpreterEnabled = 0;
        var csharpPassed = 0;
        var csharpEnabled = 0;
        var jsEnabled = 0;
        var jsPassed = 0;
        var jsSkipped = 0;
        var csharpSkipped = 0;
        var jsAvailable = Tier0JavaScriptRunner.IsAvailable(out _);

        foreach (var testCase in cases)
        {
            if (!testCase.Backends.JavaScript)
                jsSkipped++;
            if (!testCase.Backends.CSharp)
                csharpSkipped++;

            if (testCase.Backends.Interpreter)
            {
                interpreterEnabled++;
                var r = await RunCaseAsync(testCase, Tier0BackendKind.Interpreter);
                if (r.Passed) interpreterPassed++;
                else results.Add(r);
            }

            if (testCase.Backends.CSharp)
            {
                csharpEnabled++;
                var r = await RunCaseAsync(testCase, Tier0BackendKind.CSharp);
                if (r.Passed) csharpPassed++;
                else results.Add(r);
            }

            if (testCase.Backends.JavaScript && jsAvailable)
            {
                jsEnabled++;
                var r = await RunCaseAsync(testCase, Tier0BackendKind.JavaScript);
                if (r.Passed) jsPassed++;
                else results.Add(r);
            }
        }

        return new Tier0BackendMatrixReport
        {
            TotalCases = cases.Count,
            InterpreterEnabled = interpreterEnabled,
            InterpreterPassed = interpreterPassed,
            CSharpEnabled = csharpEnabled,
            CSharpPassed = csharpPassed,
            JavaScriptEnabled = jsEnabled,
            JavaScriptPassed = jsPassed,
            JavaScriptSkipped = jsSkipped,
            CSharpSkipped = csharpSkipped,
            Failures = results
        };
    }

    private static Tier0CaseRunResult Fail(
        Tier0ConformanceCase testCase,
        Tier0BackendKind backend,
        string? expected = null,
        string? actual = null,
        string? error = null) =>
        new()
        {
            Case = testCase,
            Backend = backend,
            Passed = false,
            Expected = expected,
            Actual = actual,
            Error = error
        };

    private sealed class ConsoleCapture : IAsyncDisposable
    {
        private readonly StringWriter _writer = new();
        private readonly TextWriter _previousOut;

        public ConsoleCapture()
        {
            _previousOut = Console.Out;
            Console.SetOut(_writer);
        }

        public string Output => _writer.ToString();

        public ValueTask DisposeAsync()
        {
            Console.SetOut(_previousOut);
            return ValueTask.CompletedTask;
        }
    }
}
