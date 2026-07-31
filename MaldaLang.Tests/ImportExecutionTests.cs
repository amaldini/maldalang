using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using MaldaLang.PackageManager;
using MaldaLang.PackageManager.Models;
using MaldaLang.Parser;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class ImportExecutionTests : TestBase
{
    private sealed record PackageFile(string RelativePath, string Contents);

    private async Task<string> RunProgramWithModuleLoaderAsync(string source, ModuleLoader moduleLoader)
    {
        RedirectConsole();
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            var statements = parser.Parse();

            Assert.Empty(parser.Errors);

            var interpreter = new Interpreter.Interpreter();
            var moduleLoaderField = typeof(Interpreter.Interpreter).GetField("_moduleLoader", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(moduleLoaderField);
            moduleLoaderField!.SetValue(interpreter, moduleLoader);

            await interpreter.InterpretAsync(statements);
            return GetOutput();
        }
        finally
        {
            RestoreConsole();
        }
    }

    private static ModuleLoader CreateTestModuleLoader(PackageStorage storage)
    {
        var originalRegistryUrl = System.Environment.GetEnvironmentVariable("MALDA_REGISTRY_URL");
        System.Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", "https://test-registry.maldalang.com");
        try
        {
            var registry = new PackageRegistry(storage);
            var resolver = new ModuleResolver(storage, registry);
            return new ModuleLoader(resolver);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", originalRegistryUrl);
        }
    }

    private static void InstallPackage(
        PackageStorage storage,
        string packageName,
        string version,
        string mainContents,
        List<PackageFile>? additionalFiles = null,
        Dictionary<string, string>? exports = null)
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), "malda_pkg_src_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);

        try
        {
            var libDir = Path.Combine(sourceDir, "lib");
            Directory.CreateDirectory(libDir);
            File.WriteAllText(Path.Combine(libDir, "index.malda"), mainContents);

            if (additionalFiles != null)
            {
                foreach (var file in additionalFiles)
                {
                    var fullPath = Path.Combine(sourceDir, file.RelativePath);
                    var parentDir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    File.WriteAllText(fullPath, file.Contents);
                }
            }

            storage.InstallPackage(packageName, version, sourceDir);
            storage.SavePackageMetadata(packageName, version, new PackageMetadata
            {
                Name = packageName,
                Version = version,
                Main = "lib/index.malda",
                Exports = exports
            });
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, true);
            }
        }
    }

    [Fact]
    public async Task UsingAlias_ImportsNamespaceObject()
    {
        var packagesDir = CreateTempDirectory("import_alias_pkgs_");
        try
        {
            var storage = new PackageStorage(packagesDir);
            InstallPackage(
                storage,
                "greetings",
                "1.0.0",
                """
                var answer = 42;
                function describe() {
                    return "loaded";
                }
                """);

            var output = await RunProgramWithModuleLoaderAsync(
                """
                using lib = greetings;
                print(lib.answer);
                print(lib.describe());
                """,
                CreateTestModuleLoader(storage));

            var lines = output.Split('\n');
            Assert.Equal("42", lines[0]);
            Assert.Equal("loaded", lines[1]);
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }

    [Fact]
    public async Task UsingDirectImport_DoesNotOverwriteExistingSymbols()
    {
        var packagesDir = CreateTempDirectory("import_collision_pkgs_");
        try
        {
            var storage = new PackageStorage(packagesDir);
            InstallPackage(
                storage,
                "helpers",
                "1.0.0",
                """
                var answer = 42;
                function describe() {
                    return "imported";
                }
                """);

            var output = await RunProgramWithModuleLoaderAsync(
                """
                var answer = 5;
                using helpers;
                print(answer);
                print(describe());
                """,
                CreateTestModuleLoader(storage));

            var lines = output.Split('\n');
            Assert.Equal("5", lines[0]);
            Assert.Equal("imported", lines[1]);
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }

    [Fact]
    public async Task RepeatedUsing_ReusesCachedModuleExecution()
    {
        var packagesDir = CreateTempDirectory("import_cache_pkgs_");
        try
        {
            var storage = new PackageStorage(packagesDir);
            InstallPackage(
                storage,
                "once",
                "1.0.0",
                """
                print("module init");
                var answer = 7;
                """);

            var output = await RunProgramWithModuleLoaderAsync(
                """
                using once;
                using once;
                print(answer);
                """,
                CreateTestModuleLoader(storage));

            var lines = output.Split('\n');
            Assert.Equal(2, lines.Length);
            Assert.Equal("module init", lines[0]);
            Assert.Equal("7", lines[1]);
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }

    [Fact]
    public async Task UsingMissingPackage_ThrowsRuntimeError()
    {
        var packagesDir = CreateTempDirectory("import_missing_pkgs_");
        try
        {
            var storage = new PackageStorage(packagesDir);
            var moduleLoader = CreateTestModuleLoader(storage);

            var ex = await Assert.ThrowsAsync<RuntimeException>(async () =>
                await RunProgramWithModuleLoaderAsync(
                    """
                    using doesNotExist;
                    """,
                    moduleLoader));

            Assert.Equal("Package or module not found: doesNotExist", ex.Message);
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }

    [Fact]
    public async Task UsingSubModule_ResolvesDefaultLibPath()
    {
        var packagesDir = CreateTempDirectory("import_submodule_pkgs_");
        try
        {
            var storage = new PackageStorage(packagesDir);
            InstallPackage(
                storage,
                "toolkit",
                "1.0.0",
                """
                var rootValue = "root";
                """,
                additionalFiles: new List<PackageFile>
                {
                    new("lib/math.malda",
                    """
                    var answer = 99;
                    """)
                });

            var output = await RunProgramWithModuleLoaderAsync(
                """
                using toolkit.math;
                print(answer);
                """,
                CreateTestModuleLoader(storage));

            Assert.Equal("99", output);
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }

    [Fact]
    public async Task UsingSubModule_ResolvesExportsMap()
    {
        var packagesDir = CreateTempDirectory("import_exports_pkgs_");
        try
        {
            var storage = new PackageStorage(packagesDir);
            InstallPackage(
                storage,
                "network",
                "1.0.0",
                """
                var defaultProtocol = "tcp";
                """,
                additionalFiles: new List<PackageFile>
                {
                    new("src/http.malda",
                    """
                    var protocol = "http";
                    """)
                },
                exports: new Dictionary<string, string>
                {
                    ["./http"] = "src/http.malda"
                });

            var output = await RunProgramWithModuleLoaderAsync(
                """
                using network.http;
                print(protocol);
                """,
                CreateTestModuleLoader(storage));

            Assert.Equal("http", output);
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }

    [Fact]
    public async Task ReimportedMutableExport_SharesCachedModuleStateWithinLoader()
    {
        var packagesDir = CreateTempDirectory("import_mutable_state_pkgs_");
        try
        {
            var storage = new PackageStorage(packagesDir);
            InstallPackage(
                storage,
                "sharedstate",
                "1.0.0",
                """
                var state = { count: 1 };
                """);

            var output = await RunProgramWithModuleLoaderAsync(
                """
                using sharedstate;
                state.count = 2;
                using alias = sharedstate;
                print(alias.state.count);
                """,
                CreateTestModuleLoader(storage));

            Assert.Equal("2", output);
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }

    [Fact]
    public async Task ModuleLoader_LoadedModuleHelpersReflectCacheLifecycle()
    {
        var packagesDir = CreateTempDirectory("module_loader_helpers_pkgs_");
        try
        {
            var storage = new PackageStorage(packagesDir);
            InstallPackage(
                storage,
                "helpers",
                "1.0.0",
                """
                var value = 7;
                """);

            var moduleLoader = CreateTestModuleLoader(storage);

            Assert.False(moduleLoader.IsModuleLoaded("helpers"));
            Assert.Null(moduleLoader.GetLoadedModule("helpers"));

            var loadResult = await moduleLoader.LoadModuleAsync("helpers");

            Assert.True(moduleLoader.IsModuleLoaded("helpers"));
            Assert.Same(loadResult.Environment, moduleLoader.GetLoadedModule("helpers"));

            moduleLoader.ClearCache();

            Assert.False(moduleLoader.IsModuleLoaded("helpers"));
            Assert.Null(moduleLoader.GetLoadedModule("helpers"));
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }

    [Fact]
    public async Task ModuleLoader_LoadModuleAsync_ThrowsWhenModuleAlreadyMarkedLoading()
    {
        var packagesDir = CreateTempDirectory("module_loader_cycle_pkgs_");
        try
        {
            var storage = new PackageStorage(packagesDir);
            InstallPackage(
                storage,
                "cycle",
                "1.0.0",
                """
                var value = 1;
                """);

            var moduleLoader = CreateTestModuleLoader(storage);
            var loadingModulesField = typeof(ModuleLoader).GetField("_loadingModules", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(loadingModulesField);

            var loadingModules = (HashSet<string>)loadingModulesField!.GetValue(moduleLoader)!;
            loadingModules.Add("cycle");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => moduleLoader.LoadModuleAsync("cycle"));
            Assert.Equal("Circular dependency detected: cycle", ex.Message);
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }

    [Fact]
    public async Task ImportPackage_WorkspaceSdk_ExposesTimeseriesHelpers()
    {
        var repoPackages = PlanningPaths.ResolveRepoPath("packages");
        var previous = System.Environment.GetEnvironmentVariable("MALDA_PACKAGES_DIR");
        try
        {
            System.Environment.SetEnvironmentVariable("MALDA_PACKAGES_DIR", repoPackages);
            var storage = new PackageStorage(Path.Combine(Path.GetTempPath(), "import_ws_empty_" + Guid.NewGuid().ToString("N")));
            var output = await RunProgramWithModuleLoaderAsync(
                """
                import malda-timeseries;
                print(typeOf(taSma));
                """,
                new ModuleLoader(new ModuleResolver(storage)));

            Assert.Contains("function", output.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MALDA_PACKAGES_DIR", previous);
        }
    }

    [Fact]
    public async Task ImportKeyword_PackageAlias_MatchesUsingAlias()
    {
        var packagesDir = CreateTempDirectory("import_keyword_alias_pkgs_");
        try
        {
            var storage = new PackageStorage(packagesDir);
            InstallPackage(
                storage,
                "greetings",
                "1.0.0",
                """
                var answer = 99;
                function describe() {
                    return "imported";
                }
                """);

            var output = await RunProgramWithModuleLoaderAsync(
                """
                import lib = greetings;
                print(lib.answer);
                print(lib.describe());
                """,
                CreateTestModuleLoader(storage));

            var lines = output.Split('\n');
            Assert.Equal("99", lines[0]);
            Assert.Equal("imported", lines[1]);
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }

    [Fact]
    public async Task ImportFileModule_ExportsOnlyMarkedBindings()
    {
        var tempDir = CreateTempDirectory("import_file_export_");
        try
        {
            var modulePath = Path.Combine(tempDir, "lib.malda");
            File.WriteAllText(
                modulePath,
                """
                var secret = 1;
                export var visible = 2;
                export function getVisible() {
                    return visible;
                }
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            File.WriteAllText(
                mainPath,
                $$"""
                import "lib.malda";
                print(visible);
                print(getVisible());
                """);

            var source = File.ReadAllText(mainPath);
            var lexer = new Lexer(source, mainPath);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens, mainPath);
            var statements = parser.Parse();
            Assert.Empty(parser.Errors);

            var interpreter = new Interpreter.Interpreter();
            var moduleLoader = new ModuleLoader();
            var moduleLoaderField = typeof(Interpreter.Interpreter).GetField("_moduleLoader", BindingFlags.Instance | BindingFlags.NonPublic);
            moduleLoaderField!.SetValue(interpreter, moduleLoader);

            RedirectConsole();
            try
            {
                await interpreter.InterpretAsync(statements);
                var lines = GetOutput().Split('\n');
                Assert.Equal("2", lines[0]);
                Assert.Equal("2", lines[1]);
            }
            finally
            {
                RestoreConsole();
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task ImportFileModule_UnexportedSymbolThrowsAtRuntime()
    {
        var tempDir = CreateTempDirectory("import_file_secret_");
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "lib.malda"),
                """
                export var visible = 1;
                var secret = 9;
                """);

            var mainPath = Path.Combine(tempDir, "main.malda");
            File.WriteAllText(
                mainPath,
                """
                import "lib.malda";
                print(secret);
                """);

            var lexer = new Lexer(File.ReadAllText(mainPath), mainPath);
            var parser = new Parser.Parser(lexer.Tokenize(), mainPath);
            var statements = parser.Parse();
            Assert.Empty(parser.Errors);

            var interpreter = new Interpreter.Interpreter();
            typeof(Interpreter.Interpreter)
                .GetField("_moduleLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(interpreter, new ModuleLoader());

            await Assert.ThrowsAsync<RuntimeException>(() => interpreter.InterpretAsync(statements));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
