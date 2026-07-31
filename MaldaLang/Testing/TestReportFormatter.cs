namespace MaldaLang.Testing;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal enum TestReportFormat
{
    Human,
    Ci
}

internal sealed class TestExecutionResult
{
    public string Path { get; }
    public bool Passed { get; }
    public string? ErrorMessage { get; }
    public bool IsProperty { get; }
    public string? PropertyName { get; }
    public int? PropertySeed { get; }
    public int? PropertyIterations { get; }
    public int? PropertyFailedTrial { get; }
    public string? PropertyCounterexample { get; }
    public string? PropertyShrunkCounterexample { get; }
    public bool? CanGenerateRegression { get; }
    public string? RecommendedRegressionPath { get; }
    public string? RecommendedRegressionFileName { get; }
    public string? CanonicalCounterexamplePayload { get; }

    public TestExecutionResult(
        string path,
        bool passed,
        string? errorMessage = null,
        bool isProperty = false,
        string? propertyName = null,
        int? propertySeed = null,
        int? propertyIterations = null,
        int? propertyFailedTrial = null,
        string? propertyCounterexample = null,
        string? propertyShrunkCounterexample = null,
        bool? canGenerateRegression = null,
        string? recommendedRegressionPath = null,
        string? recommendedRegressionFileName = null,
        string? canonicalCounterexamplePayload = null)
    {
        Path = path;
        Passed = passed;
        ErrorMessage = errorMessage;
        IsProperty = isProperty;
        PropertyName = propertyName;
        PropertySeed = propertySeed;
        PropertyIterations = propertyIterations;
        PropertyFailedTrial = propertyFailedTrial;
        PropertyCounterexample = propertyCounterexample;
        PropertyShrunkCounterexample = propertyShrunkCounterexample;
        CanGenerateRegression = canGenerateRegression;
        RecommendedRegressionPath = recommendedRegressionPath;
        RecommendedRegressionFileName = recommendedRegressionFileName;
        CanonicalCounterexamplePayload = canonicalCounterexamplePayload;
    }
}

internal static class TestReportFormatter
{
    public static void WriteList(TestReportFormat format, IReadOnlyList<string> tests, TextWriter output)
    {
        if (format == TestReportFormat.Ci)
        {
            var payload = new
            {
                mode = "ci",
                action = "list",
                count = tests.Count,
                tests
            };
            output.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        output.WriteLine($"Discovered {tests.Count} test file(s):");
        foreach (var testPath in tests)
        {
            output.WriteLine($" - {testPath}");
        }
    }

    public static void WriteRunReport(
        TestReportFormat format,
        IReadOnlyList<string> tests,
        IReadOnlyList<TestExecutionResult> results,
        TextWriter output,
        TextWriter error)
    {
        var orderedResults = results
            .OrderBy(r => NormalizePath(r.Path), System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        var passed = orderedResults.Count(r => r.Passed);
        var failed = orderedResults.Count - passed;

        if (format == TestReportFormat.Ci)
        {
            var ciResults = orderedResults
                .Select(r => new
                {
                    path = r.Path,
                    status = r.Passed ? "passed" : "failed",
                    error = r.Passed ? null : r.ErrorMessage,
                    isProperty = r.IsProperty,
                    propertyName = r.PropertyName,
                    property = r.IsProperty
                        ? new
                        {
                            seed = r.PropertySeed,
                            iterations = r.PropertyIterations,
                            failedTrial = r.PropertyFailedTrial,
                            counterexample = r.PropertyCounterexample,
                            shrunkCounterexample = r.PropertyShrunkCounterexample,
                            canGenerateRegression = r.CanGenerateRegression,
                            recommendedRegressionPath = r.RecommendedRegressionPath,
                            recommendedRegressionFileName = r.RecommendedRegressionFileName,
                            canonicalCounterexamplePayload = r.CanonicalCounterexamplePayload
                        }
                        : null
                })
                .ToList();

            var payload = new
            {
                mode = "ci",
                action = "run",
                summary = new
                {
                    total = orderedResults.Count,
                    passed,
                    failed
                },
                results = ciResults,
                failures = ciResults.Where(r => r.status == "failed").ToList()
            };

            output.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        foreach (var result in orderedResults)
        {
            if (result.Passed)
            {
                output.WriteLine($"PASS {result.Path}");
            }
            else
            {
                error.WriteLine($"FAIL {result.Path}");
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    error.WriteLine($"  {result.ErrorMessage}");
                }

                if (result.IsProperty)
                {
                    if (result.PropertySeed.HasValue)
                    {
                        error.WriteLine($"  Seed: {result.PropertySeed.Value}");
                    }

                    if (result.PropertyIterations.HasValue)
                    {
                        error.WriteLine($"  Iterations: {result.PropertyIterations.Value}");
                    }

                    if (result.PropertyFailedTrial.HasValue)
                    {
                        error.WriteLine($"  Failed trial: {result.PropertyFailedTrial.Value}");
                    }

                    if (!string.IsNullOrWhiteSpace(result.PropertyCounterexample))
                    {
                        error.WriteLine($"  Counterexample: {result.PropertyCounterexample}");
                    }

                    if (!string.IsNullOrWhiteSpace(result.PropertyShrunkCounterexample))
                    {
                        error.WriteLine($"  Shrunk counterexample: {result.PropertyShrunkCounterexample}");
                    }
                }
            }
        }

        if (failed > 0)
        {
            error.WriteLine();
            error.WriteLine("Failures:");
            foreach (var failedResult in orderedResults.Where(r => !r.Passed))
            {
                error.WriteLine($" - {failedResult.Path}");
                if (!string.IsNullOrWhiteSpace(failedResult.ErrorMessage))
                {
                    error.WriteLine($"   {failedResult.ErrorMessage}");
                }
            }
        }

        output.WriteLine();
        output.WriteLine($"Test run complete. total={orderedResults.Count} passed={passed} failed={failed}");
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
