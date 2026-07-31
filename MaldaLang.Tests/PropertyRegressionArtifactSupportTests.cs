namespace MaldaLang.Tests;

using System.IO;
using MaldaLang.Testing;

public class PropertyRegressionArtifactSupportTests : TestBase
{
    [Fact]
    public void TryExtractFromOutput_ParsesCiFailurePayload()
    {
        var output = """
            {"mode":"ci","action":"run","summary":{"total":1,"passed":0,"failed":1},"results":[{"path":"tests/sample.test.malda::property catchesIssue","status":"failed","error":"fail","isProperty":true,"propertyName":"catchesIssue","property":{"seed":11,"iterations":200,"failedTrial":4,"counterexample":"[9]","shrunkCounterexample":"[1]","canGenerateRegression":true,"recommendedRegressionPath":"tests/regressions/sample-catchesIssue.spec.malda","recommendedRegressionFileName":"sample-catchesIssue.spec.malda","canonicalCounterexamplePayload":"[1]"}}],"failures":[{"path":"tests/sample.test.malda::property catchesIssue","status":"failed","error":"fail","isProperty":true,"propertyName":"catchesIssue","property":{"seed":11,"iterations":200,"failedTrial":4,"counterexample":"[9]","shrunkCounterexample":"[1]","canGenerateRegression":true,"recommendedRegressionPath":"tests/regressions/sample-catchesIssue.spec.malda","recommendedRegressionFileName":"sample-catchesIssue.spec.malda","canonicalCounterexamplePayload":"[1]"}}]}
            """;

        var ok = PropertyRegressionArtifactSupport.TryExtractFromOutput(output, out var request);

        Assert.True(ok);
        Assert.NotNull(request);
        Assert.Equal("tests/sample.test.malda", request!.SourcePath);
        Assert.Equal("catchesIssue", request.PropertyName);
        Assert.Equal(11, request.Seed);
        Assert.Equal(200, request.Iterations);
        Assert.Equal(4, request.FailedTrial);
        Assert.Equal("[1]", request.CanonicalCounterexamplePayload);
        Assert.Equal("sample-catchesIssue.spec.malda", request.RecommendedRegressionFileName);
    }

    [Fact]
    public void ResolveCollisionSafePath_ReusesIdenticalFileAndAddsSuffixForDifferentContent()
    {
        var root = CreateTempDirectory("malda_regression_support_");
        try
        {
            var preferred = Path.Combine(root, "tests", "regressions", "sample.spec.malda");
            var firstContent = "var regressionArgs = [1];";
            var samePath = PropertyRegressionArtifactSupport.ResolveCollisionSafePath(preferred, firstContent);
            Directory.CreateDirectory(Path.GetDirectoryName(samePath)!);
            File.WriteAllText(samePath, firstContent);

            var reusedPath = PropertyRegressionArtifactSupport.ResolveCollisionSafePath(preferred, firstContent);
            Assert.Equal(samePath, reusedPath);

            var differentPath = PropertyRegressionArtifactSupport.ResolveCollisionSafePath(preferred, "var regressionArgs = [2];");
            Assert.NotEqual(samePath, differentPath);
            Assert.EndsWith("-1.spec.malda", differentPath);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void TryExtractFromOutput_RejectsPayloadWithoutPropertyMarkerOrRegressionHints()
    {
        var outputMissingHints = """
            {"mode":"ci","action":"run","summary":{"total":1,"passed":0,"failed":1},"results":[{"path":"tests/sample.test.malda::property catchesIssue","status":"failed","isProperty":true,"propertyName":"catchesIssue","property":{"seed":11,"iterations":200,"failedTrial":4,"counterexample":"[9]","canGenerateRegression":true}}]}
            """;
        var outputMissingIsProperty = """
            {"mode":"ci","action":"run","summary":{"total":1,"passed":0,"failed":1},"results":[{"path":"tests/sample.test.malda::property catchesIssue","status":"failed","propertyName":"catchesIssue","property":{"seed":11,"iterations":200,"failedTrial":4,"counterexample":"[9]","canGenerateRegression":true,"recommendedRegressionFileName":"sample-catchesIssue.spec.malda"}}]}
            """;

        Assert.False(PropertyRegressionArtifactSupport.TryExtractFromOutput(outputMissingHints, out _));
        Assert.False(PropertyRegressionArtifactSupport.TryExtractFromOutput(outputMissingIsProperty, out _));
    }

    [Fact]
    public void ResolveWorkspaceSafePreferredPath_ClampsToWorkspaceForUnsafeSuggestions()
    {
        var root = CreateTempDirectory("malda_regression_safe_path_");
        try
        {
            var request = new PropertyRegressionArtifactRequest
            {
                SourcePath = "tests/sample.test.malda",
                PropertyName = "catchesIssue",
                Seed = 11,
                Iterations = 200,
                FailedTrial = 4,
                CanonicalCounterexamplePayload = "[1]"
            };

            var absoluteUnsafe = new PropertyRegressionArtifactRequest
            {
                SourcePath = request.SourcePath,
                PropertyName = request.PropertyName,
                Seed = request.Seed,
                Iterations = request.Iterations,
                FailedTrial = request.FailedTrial,
                CanonicalCounterexamplePayload = request.CanonicalCounterexamplePayload,
                RecommendedRegressionPath = "C:/Windows/System32/evil.spec.malda"
            };
            var traversalUnsafe = new PropertyRegressionArtifactRequest
            {
                SourcePath = request.SourcePath,
                PropertyName = request.PropertyName,
                Seed = request.Seed,
                Iterations = request.Iterations,
                FailedTrial = request.FailedTrial,
                CanonicalCounterexamplePayload = request.CanonicalCounterexamplePayload,
                RecommendedRegressionPath = "../outside/evil.spec.malda"
            };

            var absoluteResolved = PropertyRegressionArtifactSupport.ResolveWorkspaceSafePreferredPath(absoluteUnsafe, root);
            var traversalResolved = PropertyRegressionArtifactSupport.ResolveWorkspaceSafePreferredPath(traversalUnsafe, root);

            Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(absoluteResolved), System.StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(traversalResolved), System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine("tests", "regressions"), absoluteResolved);
            Assert.Contains(Path.Combine("tests", "regressions"), traversalResolved);
            Assert.EndsWith(".spec.malda", absoluteResolved, System.StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".spec.malda", traversalResolved, System.StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }
}
