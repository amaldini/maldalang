namespace MaldaLang.Tests;

using System.IO;
using MaldaLang.Scaffolding;

public class ScaffoldingTests : TestBase
{
    [Fact]
    public void Scaffold_WebApiTemplate_CreatesTestAndSecurityDefaults()
    {
        var root = CreateTempDirectory("malda_scaffold_webapi_");
        var destination = Path.Combine(root, "sample-api");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();

            var code = scaffolder.Scaffold("webapi", destination, output, error);

            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(destination, "app.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "tests", "auth_context.test.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "tests", "security_helpers.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "config", "security.example.json")));
            Assert.True(File.Exists(Path.Combine(destination, "config", "observability.example.json")));
            Assert.True(File.Exists(Path.Combine(destination, "config", "deploy.example.json")));
            Assert.True(File.Exists(Path.Combine(destination, "config", "environments", "dev.json")));
            Assert.True(File.Exists(Path.Combine(destination, "config", "environments", "test.json")));
            Assert.True(File.Exists(Path.Combine(destination, "config", "environments", "prod.json")));
            Assert.Contains("Created webapi project", output.ToString());
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_FullstackTemplate_CreatesExpectedLayout()
    {
        var root = CreateTempDirectory("malda_scaffold_fullstack_");
        var destination = Path.Combine(root, "sample-fullstack");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();

            var code = scaffolder.Scaffold("fullstack", destination, output, error);

            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(destination, "backend", "app.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "frontend", "index.html")));
            Assert.True(File.Exists(Path.Combine(destination, "tests", "auth.test.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "config", "security.example.json")));
            Assert.True(File.Exists(Path.Combine(destination, "config", "observability.example.json")));
            Assert.True(File.Exists(Path.Combine(destination, "config", "deploy.example.json")));

            var app = File.ReadAllText(Path.Combine(destination, "backend", "app.malda"));
            Assert.Contains("web.mount(api)", app);
            Assert.Contains("new HttpServer(8080)", app);
            Assert.Contains("new RestServer()", app);
            Assert.Contains("enableSession", app);
            Assert.DoesNotContain("HttpServer(8081", app);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_WebApiTemplate_LocalFirst_AddsDataBootstrap()
    {
        var root = CreateTempDirectory("malda_scaffold_webapi_localfirst_");
        var destination = Path.Combine(root, "sample-api");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "webapi",
                DestinationPath = destination,
                LocalFirst = true
            };

            var code = scaffolder.Scaffold("webapi", destination, output, error, options);

            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(destination, "data", "local_first.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "config", "data.example.json")));

            var app = File.ReadAllText(Path.Combine(destination, "app.malda"));
            Assert.Contains("include \"data/local_first.malda\";", app);
            Assert.Contains("initLocalDataPlatform();", app);
            Assert.Contains("\"storage\": \"sqlite-local\"", app);
            Assert.Contains("/api/data/status", app);
            Assert.Contains("generated local-first migration module", output.ToString());
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_FullstackTemplate_LocalFirst_RendersSQLiteTicketBoard()
    {
        var root = CreateTempDirectory("malda_scaffold_fullstack_localfirst_");
        var destination = Path.Combine(root, "sample-fullstack");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "fullstack",
                DestinationPath = destination,
                LocalFirst = true
            };

            var code = scaffolder.Scaffold("fullstack", destination, output, error, options);

            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(destination, "backend", "data", "local_first.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "config", "data.example.json")));

            var app = File.ReadAllText(Path.Combine(destination, "backend", "app.malda"));
            Assert.Contains("include \"data/local_first.malda\";", app);
            Assert.Contains("listLocalTickets()", app);
            Assert.Contains("insertLocalTicket(title);", app);
            Assert.Contains("\"storage\": \"sqlite-local\"", app);
            Assert.DoesNotContain("{{#LOCAL_FIRST}}", app);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_NoTestsOption_SkipsTestsDirectory()
    {
        var root = CreateTempDirectory("malda_scaffold_notests_");
        var destination = Path.Combine(root, "sample-api");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "webapi",
                DestinationPath = destination,
                IncludeTests = false
            };

            var code = scaffolder.Scaffold("webapi", destination, output, error, options);

            Assert.Equal(0, code);
            Assert.False(Directory.Exists(Path.Combine(destination, "tests")));
            Assert.DoesNotContain("malda test --format human", output.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_ExistingDirectory_WithoutForce_ReturnsError()
    {
        var root = CreateTempDirectory("malda_scaffold_existing_");
        var destination = Path.Combine(root, "existing");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "keep.txt"), "existing");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();

            var code = scaffolder.Scaffold("webapi", destination, output, error);

            Assert.Equal(1, code);
            Assert.Contains("already exists and is not empty", error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_ExistingDirectory_WithForce_OverwritesTemplateFiles()
    {
        var root = CreateTempDirectory("malda_scaffold_force_");
        var destination = Path.Combine(root, "force-api");
        try
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "app.malda"), "old");

            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "webapi",
                DestinationPath = destination,
                Force = true
            };

            var code = scaffolder.Scaffold("webapi", destination, output, error, options);

            Assert.Equal(0, code);
            var app = File.ReadAllText(Path.Combine(destination, "app.malda"));
            Assert.Contains("RestServer", app);
            Assert.Contains("overwritten", output.ToString());
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_NameOverride_AppliesTemplateVariables()
    {
        var root = CreateTempDirectory("malda_scaffold_name_");
        var destination = Path.Combine(root, "dir-name");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "webapi",
                DestinationPath = destination,
                ProjectName = "SalesPortal"
            };

            var code = scaffolder.Scaffold("webapi", destination, output, error, options);

            Assert.Equal(0, code);
            var app = File.ReadAllText(Path.Combine(destination, "app.malda"));
            Assert.Contains("\"service\": \"SalesPortal\"", app);
            Assert.Contains("\"serviceSlug\": \"salesportal\"", app);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_InvalidName_ReturnsValidationError()
    {
        var root = CreateTempDirectory("malda_scaffold_invalid_name_");
        var destination = Path.Combine(root, "dir-name");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "webapi",
                DestinationPath = destination,
                ProjectName = "1invalid"
            };

            var code = scaffolder.Scaffold("webapi", destination, output, error, options);

            Assert.Equal(1, code);
            Assert.Contains("Invalid project name", error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_GameTemplate_CreatesAppHtmlAndReadme()
    {
        var root = CreateTempDirectory("malda_scaffold_game_");
        var destination = Path.Combine(root, "sample-game");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();

            var code = scaffolder.Scaffold("game", destination, output, error);

            var text = output.ToString();
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(destination, "app.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "index.html")));
            Assert.True(File.Exists(Path.Combine(destination, "README.md")));
            Assert.True(File.Exists(Path.Combine(destination, "assets", ".gitkeep")));
            Assert.False(Directory.Exists(Path.Combine(destination, "config", "environments")));
            Assert.DoesNotContain("malda test --format human", text);
            Assert.Contains("malda play app.malda", text);
            Assert.DoesNotContain("malda db", text);
            Assert.DoesNotContain("Environment profiles generated", text);

            var app = File.ReadAllText(Path.Combine(destination, "app.malda"));
            Assert.Contains("game.createCanvas", app);
            Assert.Contains("game.startFixed", app);
            Assert.DoesNotContain("game.start(", app);
            Assert.Contains("function updateGame", app);

            var html = File.ReadAllText(Path.Combine(destination, "index.html"));
            var runtimeIdx = html.IndexOf("malda-js-runtime.js", StringComparison.Ordinal);
            var appIdx = html.IndexOf("./app.js", StringComparison.Ordinal);
            Assert.True(runtimeIdx >= 0 && appIdx > runtimeIdx);
            Assert.Contains("sample-game", html);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_AgentTemplate_CreatesCapToolsAndNoEnvProfiles()
    {
        var root = CreateTempDirectory("malda_scaffold_agent_");
        var destination = Path.Combine(root, "sample-agent");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();

            var code = scaffolder.Scaffold("agent", destination, output, error);

            var text = output.ToString();
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(destination, "app.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "tools.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "notes", "welcome.txt")));
            Assert.True(File.Exists(Path.Combine(destination, "README.md")));
            Assert.True(File.Exists(Path.Combine(destination, "tests", "cap_tools.test.malda")));
            Assert.False(Directory.Exists(Path.Combine(destination, "config", "environments")));
            Assert.Contains("Created agent project", text);
            Assert.Contains("malda test --format human", text);
            Assert.Contains("malda app.malda", text);
            Assert.DoesNotContain("malda play app.malda", text);
            Assert.DoesNotContain("malda db", text);
            Assert.DoesNotContain("Environment profiles generated", text);

            var tools = File.ReadAllText(Path.Combine(destination, "tools.malda"));
            Assert.Contains("schema NoteArgs", tools);
            Assert.Contains("validate(\"NoteArgs\"", tools);
            Assert.Contains("cap.confine", tools);
            Assert.Contains("cap.read", tools);
            Assert.DoesNotContain("io.readFile(args.path)", tools);
            Assert.DoesNotContain("io.readFile(relativePath)", tools);

            var app = File.ReadAllText(Path.Combine(destination, "app.malda"));
            Assert.Contains("include \"tools.malda\"", app);
            Assert.Contains("cap.fileRead", app);
            Assert.Contains("@Tool(\"read_note\"", app);
            Assert.Contains("\"NoteArgs\"", app);
            Assert.Contains("getProgramDirectory()", app);
            Assert.DoesNotContain("io.readFile(args.path)", app);

            var welcome = File.ReadAllText(Path.Combine(destination, "notes", "welcome.txt"));
            Assert.Contains("Welcome to your MALDA agent workspace.", welcome);
            Assert.Contains("sample-agent", File.ReadAllText(Path.Combine(destination, "README.md")));
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_UnsupportedTemplate_ListsGame()
    {
        var root = CreateTempDirectory("malda_scaffold_unsupported_");
        var destination = Path.Combine(root, "nope");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();

            var code = scaffolder.Scaffold("desktop", destination, output, error);

            Assert.Equal(1, code);
            Assert.Contains("webapi, fullstack, game, agent", error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scaffold_GameFullstackTemplate_CreatesScoresAppAndTests()
    {
        var root = CreateTempDirectory("malda_scaffold_game_fullstack_");
        var destination = Path.Combine(root, "sample-scores");
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "game",
                DestinationPath = destination,
                Fullstack = true
            };

            var code = scaffolder.Scaffold("game", destination, output, error, options);

            var text = output.ToString();
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(destination, "app.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "README.md")));
            Assert.True(File.Exists(Path.Combine(destination, "tests", "score.test.malda")));
            Assert.True(File.Exists(Path.Combine(destination, "assets", ".gitkeep")));
            Assert.False(File.Exists(Path.Combine(destination, "index.html")));
            Assert.False(Directory.Exists(Path.Combine(destination, "config", "environments")));
            Assert.Contains("Created game --fullstack project", text);
            Assert.Contains("malda test --format human", text);
            Assert.Contains("malda compile app.malda --mode fullstack -o dist", text);
            Assert.DoesNotContain("malda play app.malda", text);
            Assert.DoesNotContain("malda db", text);
            Assert.DoesNotContain("Environment profiles generated", text);

            var app = File.ReadAllText(Path.Combine(destination, "app.malda"));
            Assert.Contains("schema Score", app);
            Assert.Contains("validate(\"Score\"", app);
            Assert.Contains("@GET(\"/api/scores\")", app);
            Assert.Contains("@POST(\"/api/scores\")", app);
            Assert.Contains("@client()", app);
            Assert.Contains("@server()", app);
            Assert.Contains("game.startFixed", app);
            Assert.Contains("game.wasKeyPressed", app);
            Assert.Contains("game.save", app);
            Assert.Contains("httpPost", app);
            Assert.Contains("httpGet", app);
            Assert.Contains("sample-scores", File.ReadAllText(Path.Combine(destination, "README.md")));
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }
}
