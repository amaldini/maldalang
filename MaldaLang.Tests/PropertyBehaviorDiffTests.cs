namespace MaldaLang.Tests;

public class PropertyBehaviorDiffTests
{
    [Fact]
    public void InterpreterAndTranspiledCSharp_AreEquivalent_ForCorePassingProperties()
    {
        var source = """
            property intIdentity(x) {
                return (x + 0) == x;
            }

            property boolInvolution(isFlag) {
                return (!(!isFlag)) == isFlag;
            }

            property stringConcatIdentity(nameText) {
                return (nameText + "") == nameText;
            }
            """;

        var results = BehaviorDiffRunner.RunInterpreterVsCSharpFromSource(
            source,
            new BehaviorDiffOptions { Seed = 424242, Iterations = 50, TrialTimeoutMs = 2000 });

        Assert.NotEmpty(results);
        foreach (var result in results)
        {
            Assert.True(result.AreEquivalent, result.ToDiagnosticReport(seed: 424242, iterations: 50));
            Assert.True(result.InterpreterSnapshot.Passed, "Expected interpreter property to pass for core scenario.");
            Assert.True(result.CSharpSnapshot.Passed, "Expected transpiled C# property to pass for core scenario.");
            Assert.True(result.JsSnapshot.Skipped);
            Assert.Contains("target modes exclude backend 'js'", result.JsSnapshot.SkipReason ?? string.Empty);
        }
    }

    [Fact]
    public void InterpreterAndTranspiledCSharp_AreEquivalent_ForDeterministicFailureCase()
    {
        var source = """
            property boundedPositive(x) {
                return x > 10 && x < 12;
            }
            """;

        var seed = 777;
        var iterations = 30;
        var results = BehaviorDiffRunner.RunInterpreterVsCSharpFromSource(
            source,
            new BehaviorDiffOptions { Seed = seed, Iterations = iterations, TrialTimeoutMs = 2000 });

        var result = Assert.Single(results);
        Assert.True(result.AreEquivalent, result.ToDiagnosticReport(seed, iterations));
        Assert.False(result.InterpreterSnapshot.Passed);
        Assert.False(result.CSharpSnapshot.Passed);
        Assert.Equal(seed, result.InterpreterSnapshot.Seed);
        Assert.Equal(seed, result.CSharpSnapshot.Seed);
        Assert.NotNull(result.InterpreterSnapshot.Counterexample);
        Assert.NotNull(result.CSharpSnapshot.Counterexample);
    }

    [Fact]
    public void JsBackend_IsMarkedNotApplicable_WhenPropertyRequiresUnsupportedCapability()
    {
        var source = """
            @requires("actors")
            @targets("interpreter", "csharp", "js")
            property actorScopedIdentity(x) {
                return (x + 0) == x;
            }
            """;

        var result = Assert.Single(BehaviorDiffRunner.RunInterpreterVsCSharpFromSource(source));

        Assert.True(result.AreEquivalent, result.ToDiagnosticReport(seed: 1337, iterations: 40));
        Assert.True(result.InterpreterSnapshot.Passed);
        Assert.True(result.CSharpSnapshot.Passed);
        Assert.True(result.JsSnapshot.Skipped);
        Assert.Contains("not-applicable", result.JsSnapshot.SkipReason ?? string.Empty);
        Assert.Contains("Missing capabilities on 'js': actors.", result.JsSnapshot.SkipReason ?? string.Empty);
    }

    [Fact]
    public void JsEligibleProperty_IsReportedAsSkippedUntilPilotHarnessExists()
    {
        var source = """
            @requires("core")
            @targets("interpreter", "csharp", "js")
            property jsEligibleCoreProperty(x) {
                return (x * 1) == x;
            }
            """;

        var result = Assert.Single(BehaviorDiffRunner.RunInterpreterVsCSharpFromSource(source));

        Assert.True(result.InterpreterSnapshot.Passed);
        Assert.True(result.CSharpSnapshot.Passed);
        Assert.True(result.JsSnapshot.Skipped);
        Assert.Contains("JS pilot harness is disabled", result.JsSnapshot.SkipReason ?? string.Empty);
    }

    [Fact]
    public void JsEligibleProperty_RunsWithNodeHarness_WhenPilotIsEnabled()
    {
        var source = """
            @requires("core")
            @targets("interpreter", "csharp", "js")
            property jsPilotIdentity(x) {
                return (x + 0) == x;
            }
            """;

        var result = Assert.Single(BehaviorDiffRunner.RunInterpreterVsCSharpFromSource(
            source,
            new BehaviorDiffOptions
            {
                Seed = 20260306,
                Iterations = 35,
                TrialTimeoutMs = 3000,
                EnableJsPilotHarness = true
            }));

        Assert.True(result.InterpreterSnapshot.Passed, result.ToDiagnosticReport(seed: 20260306, iterations: 35));
        Assert.True(result.CSharpSnapshot.Passed, result.ToDiagnosticReport(seed: 20260306, iterations: 35));

        if (result.JsSnapshot.Skipped)
        {
            Assert.Contains("runtime is unavailable", result.JsSnapshot.SkipReason ?? string.Empty);
            return;
        }

        Assert.True(result.JsSnapshot.Passed, result.ToDiagnosticReport(seed: 20260306, iterations: 35));
        Assert.True(result.AreEquivalent, result.ToDiagnosticReport(seed: 20260306, iterations: 35));
    }
}
