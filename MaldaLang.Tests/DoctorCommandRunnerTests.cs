namespace MaldaLang.Tests;

using System.IO;
using System.Linq;
using MaldaLang.Cli;
using MaldaLang.Scaffolding;

public class DoctorCommandRunnerTests : TestBase
{
    [Fact]
    public void Run_MissingHomeAndConfig_PrintsActionableWarnings()
    {
        var root = CreateTempDirectory("malda_doctor_root_");
        var home = Path.Combine(root, ".malda-home");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new DoctorCommandRunner(home);

            var code = runner.Run(System.Array.Empty<string>(), output, error, root);

            var text = output.ToString();
            Assert.Equal(0, code);
            Assert.Contains("[warn] MALDA home", text);
            Assert.Contains("[warn] CLI config", text);
            Assert.Contains("malda onboard", text);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_InvalidConfig_ReturnsError()
    {
        var root = CreateTempDirectory("malda_doctor_badcfg_");
        var home = Path.Combine(root, ".malda-home");
        try
        {
            Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "config.json"), "{ invalid json");

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new DoctorCommandRunner(home);

            var code = runner.Run(System.Array.Empty<string>(), output, error, root);

            Assert.Equal(1, code);
            Assert.Contains("[error] CLI config", output.ToString());
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void CollectChecks_WithSkillsAndOnnxConfig_ReportsAssistantChecks()
    {
        var root = CreateTempDirectory("malda_doctor_assistant_");
        var home = Path.Combine(root, ".malda-home");
        try
        {
            Directory.CreateDirectory(home);
            var skillsDir = Path.Combine(home, "skills");
            Directory.CreateDirectory(skillsDir);
            File.WriteAllText(Path.Combine(skillsDir, "greeting.malda"), "var tools = [];\nvar agent = null;");

            File.WriteAllText(
                Path.Combine(home, "config.json"),
                """
                {
                  "channels": { "telegram": { "botToken": "123:abc", "notifyChatId": "999" } },
                  "agents": {
                    "memory": {
                      "rerankMode": "onnx",
                      "rerankModelPath": "~/.malda/models/cross-encoder"
                    }
                  }
                }
                """);

            var runner = new DoctorCommandRunner(home);
            var checks = runner.CollectChecks(root);
            var text = string.Join('\n', checks.Select(c => $"{c.Title}: {c.Message}"));

            Assert.Contains("Telegram channel", text);
            Assert.Contains("Skills", text);
            Assert.Contains("ONNX rerank", text);
            Assert.Contains("Gateway", text);
            Assert.Contains("GraphMemory", text);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void CollectChecks_WithCrashMarker_ReportsGatewayWarning()
    {
        var root = CreateTempDirectory("malda_doctor_crash_");
        var home = Path.Combine(root, ".malda-home");
        try
        {
            Directory.CreateDirectory(home);
            GatewayNotifier.RecordCrash(home, "unhandled exception");

            var runner = new DoctorCommandRunner(home);
            var checks = runner.CollectChecks(root);
            var gateway = checks.First(c => c.Title == "Gateway");

            Assert.Equal(DoctorCommandRunner.DoctorStatus.Warning, gateway.Status);
            Assert.Contains("previous crash", gateway.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_ScaffoldedProjectWithLocalProvider_ReportsHealthyProjectChecks()
    {
        var root = CreateTempDirectory("malda_doctor_scaffold_");
        var project = Path.Combine(root, "sample-api");
        var home = Path.Combine(root, ".malda-home");
        try
        {
            var scaffolder = new TemplateScaffolder();
            Assert.Equal(0, scaffolder.Scaffold("webapi", project, new StringWriter(), new StringWriter()));

            Directory.CreateDirectory(home);
            var modelPath = Path.Combine(root, "tiny.gguf");
            File.WriteAllText(modelPath, "stub");
            File.WriteAllText(
                Path.Combine(home, "config.json"),
                $$"""
                {
                  "providers": {
                    "local_llama": {
                      "modelPath": "{{modelPath.Replace("\\", "\\\\")}}"
                    }
                  },
                  "agents": {
                    "defaults": {
                      "backend": "local_llama",
                      "model": "test-local"
                    }
                  }
                }
                """);

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new DoctorCommandRunner(home);

            var code = runner.Run(System.Array.Empty<string>(), output, error, project);

            var text = output.ToString();
            Assert.Equal(0, code);
            Assert.Contains("[ok] CLI config", text);
            Assert.Contains("[ok] Assistant provider", text);
            Assert.Contains("[ok] Project scaffold", text);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_GameScaffold_DoesNotWarnAboutMissingDeployConfig()
    {
        var root = CreateTempDirectory("malda_doctor_game_");
        var project = Path.Combine(root, "sample-game");
        var home = Path.Combine(root, ".malda-home");
        try
        {
            var scaffolder = new TemplateScaffolder();
            Assert.Equal(0, scaffolder.Scaffold("game", project, new StringWriter(), new StringWriter()));
            Directory.CreateDirectory(home);

            var output = new StringWriter();
            var runner = new DoctorCommandRunner(home);
            var code = runner.Run(System.Array.Empty<string>(), output, new StringWriter(), project);

            Assert.Equal(0, code);
            Assert.Contains("[ok] Project scaffold", output.ToString());
            Assert.Contains("malda play app.malda", output.ToString());
            Assert.DoesNotContain("config/deploy.example.json", output.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_AgentScaffold_DoesNotWarnAboutMissingDeployConfig()
    {
        var root = CreateTempDirectory("malda_doctor_agent_");
        var project = Path.Combine(root, "sample-agent");
        var home = Path.Combine(root, ".malda-home");
        try
        {
            var scaffolder = new TemplateScaffolder();
            Assert.Equal(0, scaffolder.Scaffold("agent", project, new StringWriter(), new StringWriter()));
            Directory.CreateDirectory(home);

            var output = new StringWriter();
            var runner = new DoctorCommandRunner(home);
            var code = runner.Run(System.Array.Empty<string>(), output, new StringWriter(), project);

            Assert.Equal(0, code);
            Assert.Contains("[ok] Project scaffold", output.ToString());
            Assert.Contains("malda app.malda", output.ToString());
            Assert.DoesNotContain("config/deploy.example.json", output.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }
}
