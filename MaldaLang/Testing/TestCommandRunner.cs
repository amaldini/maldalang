namespace MaldaLang.Testing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

public sealed class TestCommandRunner
{
    private sealed class TestCommandOptions
    {
        public string RootPath { get; set; } = Directory.GetCurrentDirectory();
        public string? Filter { get; set; }
        public bool ListOnly { get; set; }
        public TestReportFormat ReportFormat { get; set; } = TestReportFormat.Human;
        public int Iterations { get; set; } = PropertyRunOptions.DefaultIterations;
        public int Seed { get; set; } = PropertyRunOptions.DefaultSeed;
        public bool WriteRegression { get; set; }
        public string? RegressionDirectory { get; set; }
    }

    public int Run(string[] args, TextWriter output, TextWriter error)
    {
        var options = ParseOptions(args, error);
        if (options == null)
        {
            return 1;
        }

        var discovery = new TestDiscovery();
        var tests = discovery.Discover(options.RootPath, options.Filter);
        if (tests.Count == 0)
        {
            output.WriteLine("No test files discovered.");
            return 0;
        }

        if (options.ListOnly)
        {
            TestReportFormatter.WriteList(options.ReportFormat, tests, output);
            return 0;
        }

        var results = new List<TestExecutionResult>(tests.Count);
        var regressionWriter = new RegressionArtifactWriter();

        foreach (var testPath in tests)
        {
            try
            {
                var testResults = RunSingleTest(testPath, options, regressionWriter);
                results.AddRange(testResults);
            }
            catch (Exception ex)
            {
                results.Add(new TestExecutionResult(testPath, passed: false, SanitizeMessage(ex.Message)));
            }
        }

        if (options.WriteRegression)
        {
            var generated = regressionWriter.WriteArtifacts(results, options.RootPath, options.RegressionDirectory);
            foreach (var path in generated)
            {
                output.WriteLine($"Generated regression: {path}");
            }
        }

        TestReportFormatter.WriteRunReport(options.ReportFormat, tests, results, output, error);
        var failed = results.Count(r => !r.Passed);
        return failed == 0 ? 0 : 1;
    }

    private static IReadOnlyList<TestExecutionResult> RunSingleTest(
        string path,
        TestCommandOptions options,
        RegressionArtifactWriter regressionWriter)
    {
        var source = File.ReadAllText(path);
        var lexer = new Lexer(source, path);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, path);
        var statements = parser.Parse();

        var properties = statements.OfType<PropertyDeclaration>().ToList();
        if (properties.Count == 0)
        {
            var interpreter = new Interpreter();
            interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
            return new List<TestExecutionResult> { new(path, passed: true) };
        }

        return RunProperties(path, statements, properties, options, regressionWriter);
    }

    private static IReadOnlyList<TestExecutionResult> RunProperties(
        string path,
        IReadOnlyList<Statement> statements,
        IReadOnlyList<PropertyDeclaration> properties,
        TestCommandOptions options,
        RegressionArtifactWriter regressionWriter)
    {
        var results = new List<TestExecutionResult>(properties.Count);
        var runner = new PropertyRunner();
        var runOptions = new PropertyRunOptions
        {
            Iterations = options.Iterations,
            Seed = options.Seed
        };

        foreach (var property in properties)
        {
            var result = runner.RunProperty(statements, property, runOptions);
            var unitPath = $"{path}::{property.Name}";
            var baseResult = new TestExecutionResult(
                unitPath,
                result.Passed,
                result.Passed ? null : SanitizeMessage(result.ErrorMessage ?? "Property failed."),
                isProperty: true,
                propertyName: result.PropertyName,
                propertySeed: result.Seed,
                propertyIterations: result.Iterations,
                propertyFailedTrial: result.FailedTrial,
                propertyCounterexample: result.Counterexample,
                propertyShrunkCounterexample: result.ShrunkCounterexample);

            var regressionHint = regressionWriter.BuildRegressionHint(baseResult, options.RootPath, options.RegressionDirectory);
            results.Add(new TestExecutionResult(
                baseResult.Path,
                baseResult.Passed,
                baseResult.ErrorMessage,
                isProperty: baseResult.IsProperty,
                propertyName: baseResult.PropertyName,
                propertySeed: baseResult.PropertySeed,
                propertyIterations: baseResult.PropertyIterations,
                propertyFailedTrial: baseResult.PropertyFailedTrial,
                propertyCounterexample: baseResult.PropertyCounterexample,
                propertyShrunkCounterexample: baseResult.PropertyShrunkCounterexample,
                canGenerateRegression: regressionHint.CanGenerate,
                recommendedRegressionPath: regressionHint.RecommendedPath,
                recommendedRegressionFileName: regressionHint.RecommendedFileName,
                canonicalCounterexamplePayload: regressionHint.CanonicalCounterexamplePayload));
        }

        return results;
    }

    private static TestCommandOptions? ParseOptions(string[] args, TextWriter error)
    {
        var options = new TestCommandOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--list")
            {
                options.ListOnly = true;
                continue;
            }
            if (arg == "--filter")
            {
                if (i + 1 >= args.Length)
                {
                    error.WriteLine("malda test: --filter requires a value.");
                    return null;
                }
                options.Filter = args[++i];
                continue;
            }
            if (arg == "--format")
            {
                if (i + 1 >= args.Length)
                {
                    error.WriteLine("malda test: --format requires a value.");
                    return null;
                }

                var format = args[++i].Trim().ToLowerInvariant();
                if (!TryParseFormat(format, out var parsedFormat))
                {
                    error.WriteLine($"malda test: unsupported --format '{format}'. Use 'human' or 'ci'.");
                    return null;
                }

                options.ReportFormat = parsedFormat;
                continue;
            }
            if (arg == "--iterations")
            {
                if (i + 1 >= args.Length)
                {
                    error.WriteLine("malda test: --iterations requires a value.");
                    return null;
                }

                var rawValue = args[++i];
                if (!int.TryParse(rawValue, out var iterations) || iterations <= 0)
                {
                    error.WriteLine($"malda test: invalid --iterations value '{rawValue}'. Use a positive integer.");
                    return null;
                }

                options.Iterations = iterations;
                continue;
            }
            if (arg == "--seed")
            {
                if (i + 1 >= args.Length)
                {
                    error.WriteLine("malda test: --seed requires a value.");
                    return null;
                }

                var rawValue = args[++i];
                if (!int.TryParse(rawValue, out var seed))
                {
                    error.WriteLine($"malda test: invalid --seed value '{rawValue}'. Use a 32-bit integer.");
                    return null;
                }

                options.Seed = seed;
                continue;
            }
            if (arg == "--write-regression")
            {
                options.WriteRegression = true;
                continue;
            }
            if (arg == "--regression-dir")
            {
                if (i + 1 >= args.Length)
                {
                    error.WriteLine("malda test: --regression-dir requires a value.");
                    return null;
                }

                options.RegressionDirectory = args[++i];
                continue;
            }
            if (arg.StartsWith("-"))
            {
                error.WriteLine($"malda test: unknown option '{arg}'.");
                return null;
            }

            options.RootPath = arg;
        }

        return options;
    }

    private static bool TryParseFormat(string format, out TestReportFormat reportFormat)
    {
        switch (format)
        {
            case "human":
            case "console":
                reportFormat = TestReportFormat.Human;
                return true;
            case "ci":
            case "json":
                reportFormat = TestReportFormat.Ci;
                return true;
            default:
                reportFormat = TestReportFormat.Human;
                return false;
        }
    }

    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Test failed.";
        }

        var firstLine = message
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstLine) ? "Test failed." : firstLine.Trim();
    }
}
