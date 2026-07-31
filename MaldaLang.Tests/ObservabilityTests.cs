namespace MaldaLang.Tests;

using System.IO;
using MaldaLang.Deployment;
using MaldaLang.Observability;
using MaldaLang.Scaffolding;

public class ObservabilityTests : TestBase
{
    [Fact]
    public void Validate_WithScaffoldContracts_ReturnsNoErrors()
    {
        var root = CreateTempDirectory("malda_observability_ok_");
        var destination = Path.Combine(root, "sample-api");
        try
        {
            Assert.Equal(0, new TemplateScaffolder().Scaffold("webapi", destination, new StringWriter(), new StringWriter()));
            var loaded = TryLoadContracts(destination, out var bundle, out var loadErrors);

            Assert.True(loaded, string.Join("\n", loadErrors));
            var errors = ObservabilityContractValidator.Validate(bundle!);
            Assert.Empty(errors);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Validate_HealthPathMismatch_ReturnsActionableError()
    {
        var root = CreateTempDirectory("malda_observability_health_");
        var destination = Path.Combine(root, "sample-api");
        try
        {
            Assert.Equal(0, new TemplateScaffolder().Scaffold("webapi", destination, new StringWriter(), new StringWriter()));
            var profilePath = Path.Combine(destination, "config", "environments", "prod.json");
            var profileText = File.ReadAllText(profilePath).Replace("\"healthPath\": \"/api/health\"", "\"healthPath\": \"/api/healthz\"");
            File.WriteAllText(profilePath, profileText);

            Assert.True(TryLoadContracts(destination, out var bundle, out var loadErrors), string.Join("\n", loadErrors));
            var errors = ObservabilityContractValidator.Validate(bundle!);

            Assert.Contains(errors, e => e.Contains("[health]", System.StringComparison.Ordinal));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Validate_LoggingFormatMustBeJson()
    {
        var root = CreateTempDirectory("malda_observability_logging_");
        var destination = Path.Combine(root, "sample-api");
        try
        {
            Assert.Equal(0, new TemplateScaffolder().Scaffold("webapi", destination, new StringWriter(), new StringWriter()));
            var observabilityPath = Path.Combine(destination, "config", "observability.example.json");
            var observabilityText = File.ReadAllText(observabilityPath).Replace("\"format\": \"json\"", "\"format\": \"text\"");
            File.WriteAllText(observabilityPath, observabilityText);

            Assert.True(TryLoadContracts(destination, out var bundle, out var loadErrors), string.Join("\n", loadErrors));
            var errors = ObservabilityContractValidator.Validate(bundle!);

            Assert.Contains(errors, e => e.Contains("[logging]", System.StringComparison.Ordinal));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Validate_MetricsEndpointMismatch_ReturnsActionableError()
    {
        var root = CreateTempDirectory("malda_observability_metrics_");
        var destination = Path.Combine(root, "sample-api");
        try
        {
            Assert.Equal(0, new TemplateScaffolder().Scaffold("webapi", destination, new StringWriter(), new StringWriter()));
            var observabilityPath = Path.Combine(destination, "config", "observability.example.json");
            var observabilityText = File.ReadAllText(observabilityPath).Replace("\"endpoint\": \"/metrics\"", "\"endpoint\": \"/internal/metrics\"");
            File.WriteAllText(observabilityPath, observabilityText);

            Assert.True(TryLoadContracts(destination, out var bundle, out var loadErrors), string.Join("\n", loadErrors));
            var errors = ObservabilityContractValidator.Validate(bundle!);

            Assert.Contains(errors, e => e.Contains("[metrics]", System.StringComparison.Ordinal));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    private static bool TryLoadContracts(string destination, out DeployContractBundle? bundle, out List<string> errors)
    {
        return DeployContractLoader.TryLoad(
            Path.Combine(destination, "config", "deploy.example.json"),
            Path.Combine(destination, "config", "environments", "prod.json"),
            Path.Combine(destination, "config", "observability.example.json"),
            out bundle,
            out errors);
    }
}
