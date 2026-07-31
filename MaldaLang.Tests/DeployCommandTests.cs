namespace MaldaLang.Tests;

using System.IO;
using MaldaLang.Deployment;
using MaldaLang.Scaffolding;

public class DeployCommandTests : TestBase
{
    [Fact]
    public void Run_WithScaffoldContracts_ValidatesSuccessfully()
    {
        var root = CreateTempDirectory("malda_deploy_cmd_ok_");
        var destination = Path.Combine(root, "sample-api");
        try
        {
            var scaffolder = new TemplateScaffolder();
            var scaffoldCode = scaffolder.Scaffold("webapi", destination, new StringWriter(), new StringWriter());
            Assert.Equal(0, scaffoldCode);

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new DeployCommandRunner();

            var code = runner.Run(System.Array.Empty<string>(), output, error, destination);

            Assert.Equal(0, code);
            Assert.Contains("Deploy skeleton mode", output.ToString());
            Assert.Contains("deploy-contract-validation", output.ToString());
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_MissingDeployContract_ReturnsValidationError()
    {
        var root = CreateTempDirectory("malda_deploy_cmd_missing_");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new DeployCommandRunner();

            var code = runner.Run(
                new[] { "--config", "config/missing.deploy.json" },
                output,
                error,
                root);

            Assert.Equal(1, code);
            Assert.Contains("contract validation failed", error.ToString());
            Assert.Contains("Missing contract file", error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_MalformedDeployJson_ReturnsValidationError()
    {
        var root = CreateTempDirectory("malda_deploy_cmd_badjson_");
        var destination = Path.Combine(root, "sample-api");
        try
        {
            var scaffolder = new TemplateScaffolder();
            Assert.Equal(0, scaffolder.Scaffold("webapi", destination, new StringWriter(), new StringWriter()));
            File.WriteAllText(Path.Combine(destination, "config", "deploy.example.json"), "{ invalid json");

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new DeployCommandRunner();
            var code = runner.Run(System.Array.Empty<string>(), output, error, destination);

            Assert.Equal(1, code);
            Assert.Contains("Invalid JSON", error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_ProfileNameResolution_UsesEnvironmentProfileName()
    {
        var root = CreateTempDirectory("malda_deploy_cmd_profile_");
        var destination = Path.Combine(root, "sample-api");
        try
        {
            var scaffolder = new TemplateScaffolder();
            Assert.Equal(0, scaffolder.Scaffold("webapi", destination, new StringWriter(), new StringWriter()));

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new DeployCommandRunner();
            var code = runner.Run(new[] { "--profile", "dev" }, output, error, destination);

            Assert.Equal(0, code);
            Assert.Contains("config\\environments\\dev.json", output.ToString().Replace("/", "\\"));
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_HealthReadinessMismatch_ReturnsValidationError()
    {
        var root = CreateTempDirectory("malda_deploy_cmd_health_");
        var destination = Path.Combine(root, "sample-api");
        try
        {
            var scaffolder = new TemplateScaffolder();
            Assert.Equal(0, scaffolder.Scaffold("webapi", destination, new StringWriter(), new StringWriter()));

            var profilePath = Path.Combine(destination, "config", "environments", "prod.json");
            var profileText = File.ReadAllText(profilePath).Replace("\"readinessPath\": \"/api/readiness\"", "\"readinessPath\": \"/api/ready\"");
            File.WriteAllText(profilePath, profileText);

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new DeployCommandRunner();
            var code = runner.Run(System.Array.Empty<string>(), output, error, destination);

            Assert.Equal(1, code);
            Assert.Contains("[readiness]", error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }
}
