namespace MaldaLang.Tests;

using System.IO;
using System.Linq;
using System.Text.Json;
using MaldaLang.Testing;

public class TestCommandTests : TestBase
{
    [Fact]
    public void Run_ListMode_PrintsDiscoveredFiles()
    {
        var root = CreateTempDirectory("malda_test_cmd_list_");
        try
        {
            File.WriteAllText(Path.Combine(root, "alpha.test.malda"), "print(\"ok\");");
            File.WriteAllText(Path.Combine(root, "beta.spec.malda"), "print(\"ok\");");

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new TestCommandRunner();
            var code = runner.Run(new[] { root, "--list" }, output, error);

            var text = output.ToString();
            Assert.Equal(0, code);
            Assert.Contains("Discovered 2 test file(s):", text);
            Assert.Contains("alpha.test.malda", text);
            Assert.Contains("beta.spec.malda", text);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_ExecutesTestsAndReturnsFailureWhenAnyTestFails()
    {
        var root = CreateTempDirectory("malda_test_cmd_run_");
        try
        {
            File.WriteAllText(Path.Combine(root, "pass.test.malda"), "var x = 1; print(x);");
            File.WriteAllText(Path.Combine(root, "fail.test.malda"), "error(\"boom\");");

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new TestCommandRunner();
            var code = runner.Run(new[] { root }, output, error);

            Assert.Equal(1, code);
            Assert.Contains("PASS", output.ToString());
            Assert.Contains("FAIL", error.ToString());
            Assert.Contains("failed=1", output.ToString());
            Assert.Contains("Failures:", error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_CiFormat_PrintsStructuredJsonSummary()
    {
        var root = CreateTempDirectory("malda_test_cmd_ci_");
        try
        {
            File.WriteAllText(Path.Combine(root, "pass.test.malda"), "var x = 1;");
            File.WriteAllText(Path.Combine(root, "fail.test.malda"), "error(\"boom\");");

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new TestCommandRunner();
            var code = runner.Run(new[] { root, "--format", "ci" }, output, error);

            Assert.Equal(1, code);
            Assert.Equal(string.Empty, error.ToString());

            using var doc = JsonDocument.Parse(output.ToString());
            var rootJson = doc.RootElement;
            Assert.Equal("ci", rootJson.GetProperty("mode").GetString());
            Assert.Equal(2, rootJson.GetProperty("summary").GetProperty("total").GetInt32());
            Assert.Equal(1, rootJson.GetProperty("summary").GetProperty("passed").GetInt32());
            Assert.Equal(1, rootJson.GetProperty("summary").GetProperty("failed").GetInt32());
            Assert.Equal(1, rootJson.GetProperty("failures").GetArrayLength());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_InvalidFormat_ReturnsError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new TestCommandRunner();

        var code = runner.Run(new[] { "--format", "yaml" }, output, error);

        Assert.Equal(1, code);
        Assert.Contains("unsupported --format", error.ToString());
    }

    [Fact]
    public void Run_InvalidIterations_ReturnsError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new TestCommandRunner();

        var code = runner.Run(new[] { "--iterations", "0" }, output, error);

        Assert.Equal(1, code);
        Assert.Contains("invalid --iterations", error.ToString());
    }

    [Fact]
    public void Run_InvalidSeed_ReturnsError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new TestCommandRunner();

        var code = runner.Run(new[] { "--seed", "not-a-number" }, output, error);

        Assert.Equal(1, code);
        Assert.Contains("invalid --seed", error.ToString());
    }

    [Fact]
    public void Run_MissingRegressionDirectoryValue_ReturnsError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new TestCommandRunner();

        var code = runner.Run(new[] { "--regression-dir" }, output, error);

        Assert.Equal(1, code);
        Assert.Contains("--regression-dir requires a value", error.ToString());
    }

    [Fact]
    public void Run_PropertyFailure_HumanOutputIncludesSeedTrialAndCounterexamples()
    {
        var root = CreateTempDirectory("malda_test_cmd_property_human_");
        try
        {
            File.WriteAllText(Path.Combine(root, "prop.test.malda"), @"
property alwaysFails {
    assert(false, ""always fails"");
}
");

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new TestCommandRunner();
            var code = runner.Run(new[] { root, "--iterations", "20", "--seed", "123" }, output, error);

            Assert.Equal(1, code);
            var err = error.ToString();
            Assert.Contains("prop.test.malda::alwaysFails", err);
            Assert.Contains("Seed:", err);
            Assert.Contains("Failed trial:", err);
            Assert.Contains("Counterexample:", err);
            Assert.Contains("Shrunk counterexample:", err);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_PropertyFailure_CiOutputIncludesAdditivePropertyMetadata()
    {
        var root = CreateTempDirectory("malda_test_cmd_property_ci_");
        try
        {
            File.WriteAllText(Path.Combine(root, "prop.test.malda"), @"
property alwaysFails {
    assert(false, ""always fails"");
}
");

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new TestCommandRunner();
            var code = runner.Run(new[] { root, "--format", "ci", "--iterations", "25", "--seed", "44" }, output, error);

            Assert.Equal(1, code);
            Assert.Equal(string.Empty, error.ToString());

            using var doc = JsonDocument.Parse(output.ToString());
            var results = doc.RootElement.GetProperty("results");
            Assert.Equal(1, results.GetArrayLength());

            var result = results[0];
            Assert.True(result.GetProperty("isProperty").GetBoolean());
            Assert.Equal("alwaysFails", result.GetProperty("propertyName").GetString());
            Assert.True(result.TryGetProperty("property", out var propertyMetadata));
            Assert.Equal(44, propertyMetadata.GetProperty("seed").GetInt32());
            Assert.Equal(25, propertyMetadata.GetProperty("iterations").GetInt32());
            Assert.True(propertyMetadata.TryGetProperty("failedTrial", out _));
            Assert.True(propertyMetadata.TryGetProperty("counterexample", out _));
            Assert.True(propertyMetadata.TryGetProperty("shrunkCounterexample", out _));
            Assert.True(propertyMetadata.TryGetProperty("canGenerateRegression", out var canGenerateRegression));
            Assert.True(canGenerateRegression.GetBoolean());
            Assert.True(propertyMetadata.TryGetProperty("recommendedRegressionPath", out var recommendedRegressionPath));
            Assert.False(string.IsNullOrWhiteSpace(recommendedRegressionPath.GetString()));
            Assert.True(propertyMetadata.TryGetProperty("recommendedRegressionFileName", out var recommendedRegressionFileName));
            Assert.EndsWith(".spec.malda", recommendedRegressionFileName.GetString());
            Assert.True(propertyMetadata.TryGetProperty("canonicalCounterexamplePayload", out var canonicalPayload));
            Assert.False(string.IsNullOrWhiteSpace(canonicalPayload.GetString()));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_NonPropertyTest_CiOutputRetainsBackwardCompatibleKeys()
    {
        var root = CreateTempDirectory("malda_test_cmd_ci_backcompat_");
        try
        {
            File.WriteAllText(Path.Combine(root, "plain.test.malda"), "var x = 1; print(x);");

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new TestCommandRunner();
            var code = runner.Run(new[] { root, "--format", "ci" }, output, error);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());

            using var doc = JsonDocument.Parse(output.ToString());
            var rootJson = doc.RootElement;
            Assert.Equal("ci", rootJson.GetProperty("mode").GetString());
            Assert.Equal("run", rootJson.GetProperty("action").GetString());
            Assert.Equal(1, rootJson.GetProperty("summary").GetProperty("total").GetInt32());
            Assert.Equal(1, rootJson.GetProperty("results").GetArrayLength());

            var first = rootJson.GetProperty("results").EnumerateArray().First();
            Assert.True(first.TryGetProperty("path", out _));
            Assert.True(first.TryGetProperty("status", out _));
            Assert.True(first.TryGetProperty("error", out _));
            Assert.True(first.TryGetProperty("isProperty", out _));
            Assert.True(first.TryGetProperty("property", out var propertyMetadata));
            Assert.Equal(JsonValueKind.Null, propertyMetadata.ValueKind);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_WriteRegression_DefaultDirectory_GeneratesDeterministicArtifact()
    {
        var root = CreateTempDirectory("malda_test_cmd_regression_default_");
        try
        {
            File.WriteAllText(Path.Combine(root, "prop.test.malda"), @"
property belowTwo(x) {
    assert(x < 2, ""x must be below two"");
}
");

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new TestCommandRunner();

            var code = runner.Run(
                new[] { root, "--write-regression", "--iterations", "30", "--seed", "123" },
                output,
                error);

            Assert.Equal(1, code);
            var regressionDirectory = Path.Combine(root, "tests", "regressions");
            Assert.True(Directory.Exists(regressionDirectory));

            var firstRunFiles = Directory.GetFiles(regressionDirectory, "*.spec.malda");
            Assert.Single(firstRunFiles);
            var firstFile = firstRunFiles[0];
            var firstContent = File.ReadAllText(firstFile);

            Assert.Contains("// Property: belowTwo", firstContent);
            Assert.Contains("// Seed: 123", firstContent);
            Assert.Contains("// ShrunkCounterexample:", firstContent);
            Assert.Contains("var regressionArgs =", firstContent);
            Assert.Contains("Generated regression:", output.ToString());

            var secondOutput = new StringWriter();
            var secondError = new StringWriter();
            var secondCode = runner.Run(
                new[] { root, "--write-regression", "--iterations", "30", "--seed", "123" },
                secondOutput,
                secondError);

            Assert.Equal(1, secondCode);
            var secondRunFiles = Directory.GetFiles(regressionDirectory, "*.spec.malda");
            Assert.Single(secondRunFiles);
            Assert.Equal(firstFile, secondRunFiles[0]);
            Assert.Equal(firstContent, File.ReadAllText(secondRunFiles[0]));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_WriteRegression_CustomDirectory_WritesArtifactThere()
    {
        var root = CreateTempDirectory("malda_test_cmd_regression_custom_");
        try
        {
            File.WriteAllText(Path.Combine(root, "prop.test.malda"), @"
property alwaysFails {
    assert(false, ""always fails"");
}
");

            var customRegressionDirectory = Path.Combine(root, "custom-regressions");
            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new TestCommandRunner();

            var code = runner.Run(
                new[] { root, "--write-regression", "--regression-dir", customRegressionDirectory, "--seed", "44" },
                output,
                error);

            Assert.Equal(1, code);
            Assert.True(Directory.Exists(customRegressionDirectory));
            Assert.Single(Directory.GetFiles(customRegressionDirectory, "*.spec.malda"));

            var defaultRegressionDirectory = Path.Combine(root, "tests", "regressions");
            Assert.False(Directory.Exists(defaultRegressionDirectory));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }
}
