using Xunit;
using Xunit.Abstractions;

namespace MaldaLang.Tests.Conformance.Tier0;

/// <summary>
/// File-driven Tier 0 conformance (<c>conformance/tier0/cases/*.malda</c>).
/// </summary>
[Collection("Sequential")]
public class Tier0MaldaConformanceTests
{
    public static IEnumerable<object[]> InterpreterCaseIds() =>
        Tier0ConformanceManifest.LoadCases()
            .Where(c => c.Backends.Interpreter)
            .Select(c => new object[] { c.Id });

    public static IEnumerable<object[]> CSharpCaseIds() =>
        Tier0ConformanceManifest.LoadCases()
            .Where(c => c.Backends.CSharp)
            .Select(c => new object[] { c.Id });

    public static IEnumerable<object[]> JavaScriptCaseIds()
    {
        if (!Tier0JavaScriptRunner.IsAvailable(out _))
            yield break;

        foreach (var testCase in Tier0ConformanceManifest.LoadCases().Where(c => c.Backends.JavaScript))
            yield return new object[] { testCase.Id };
    }

    [Theory]
    [MemberData(nameof(InterpreterCaseIds))]
    public async Task Interpreter_MatchesExpected(string caseId)
    {
        var testCase = FindCase(caseId);
        var result = await Tier0ConformanceRunner.RunCaseAsync(testCase, Tier0BackendKind.Interpreter);
        Assert.True(result.Passed, FormatFailure(result));
    }

    [Theory]
    [MemberData(nameof(CSharpCaseIds))]
    public async Task CSharpTranspile_MatchesExpected(string caseId)
    {
        var testCase = FindCase(caseId);
        var result = await Tier0ConformanceRunner.RunCaseAsync(testCase, Tier0BackendKind.CSharp);
        Assert.True(result.Passed, FormatFailure(result));
    }

    [Theory]
    [MemberData(nameof(JavaScriptCaseIds))]
    public async Task JavaScript_MatchesExpected(string caseId)
    {
        var testCase = FindCase(caseId);
        var result = await Tier0ConformanceRunner.RunCaseAsync(testCase, Tier0BackendKind.JavaScript);
        Assert.True(result.Passed, FormatFailure(result));
    }

    [Fact]
    public void Manifest_AllCasesHaveSourceAndExpectFiles()
    {
        foreach (var testCase in Tier0ConformanceManifest.LoadCases())
        {
            Assert.True(File.Exists(testCase.MaldaPath), $"Missing {testCase.MaldaPath}");
            Assert.True(File.Exists(testCase.ExpectPath), $"Missing {testCase.ExpectPath}");
        }
    }

    private static Tier0ConformanceCase FindCase(string caseId) =>
        Tier0ConformanceManifest.LoadCases().First(c => c.Id == caseId);

    private static string FormatFailure(Tier0CaseRunResult result)
    {
        var lines = new List<string>
        {
            $"{result.Case.Id} ({result.Backend}) failed."
        };
        if (result.Error != null)
            lines.Add($"Error: {result.Error}");
        if (result.Expected != null)
            lines.Add($"Expected:{Environment.NewLine}{result.Expected}");
        if (result.Actual != null)
            lines.Add($"Actual:{Environment.NewLine}{result.Actual}");
        return string.Join(Environment.NewLine, lines);
    }
}

[Collection("Sequential")]
public class Tier0BackendMatrixTests
{
    private readonly ITestOutputHelper _output;

    public Tier0BackendMatrixTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task BackendMatrix_MeetsTier0ParityThresholds()
    {
        var report = await Tier0ConformanceRunner.BuildMatrixReportAsync();
        _output.WriteLine(report.ToSummaryString());

        var reportDir = Environment.GetEnvironmentVariable("TIER0_PARITY_OUT");
        if (!string.IsNullOrWhiteSpace(reportDir))
            Tier0BackendMatrixReport.WriteReportArtifacts(report, reportDir);

        foreach (var failure in report.Failures)
        {
            _output.WriteLine(
                $"FAIL {failure.Case.Id} [{failure.Backend}]: {failure.Error ?? "output mismatch"}");
        }

        Assert.Equal(report.InterpreterEnabled, report.InterpreterPassed);
        Assert.True(
            report.CSharpPassRate >= Tier0ConformanceRunner.CSharpParityMinimum,
            $"C# parity {report.CSharpPassed}/{report.CSharpEnabled} ({report.CSharpPassRate:P1}) " +
            $"is below {Tier0ConformanceRunner.CSharpParityMinimum:P0}.");

        if (report.JavaScriptEnabled > 0)
        {
            Assert.True(
                report.JavaScriptPassRate >= Tier0ConformanceRunner.JavaScriptParityMinimum,
                $"JavaScript parity {report.JavaScriptPassed}/{report.JavaScriptEnabled} ({report.JavaScriptPassRate:P1}) " +
                $"is below {Tier0ConformanceRunner.JavaScriptParityMinimum:P0}.");
        }
        else if (!Tier0JavaScriptRunner.IsAvailable(out var jsReason))
        {
            _output.WriteLine("JavaScript Tier 0 pilot not run: " + jsReason);
        }
    }
}
