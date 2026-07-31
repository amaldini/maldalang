using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using MaldaLang.PackageManager;
using MaldaLang.PackageManager.Models;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class ExecutionInterpreterTests : TestBase
{
    private sealed record PackageFile(string RelativePath, string Contents);

    private async Task InterpretAsync(Interpreter.Interpreter interpreter, string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();

        Assert.Empty(parser.Errors);
        await interpreter.InterpretAsync(statements);
    }

    private async Task<string> RunWithInterpreterAsync(Interpreter.Interpreter interpreter, string source)
    {
        RedirectConsole();
        try
        {
            await InterpretAsync(interpreter, source);
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
        var sourceDir = Path.Combine(Path.GetTempPath(), "execution_interp_pkg_" + Guid.NewGuid().ToString("N"));
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

    private static void SetModuleLoader(Interpreter.Interpreter interpreter, ModuleLoader moduleLoader)
    {
        var moduleLoaderField = typeof(Interpreter.Interpreter).GetField("_moduleLoader", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(moduleLoaderField);
        moduleLoaderField!.SetValue(interpreter, moduleLoader);
    }

    [Fact]
    public async Task CreateExecutionInterpreter_RebindsFunctionClosuresToCopiedGlobals()
    {
        var root = new Interpreter.Interpreter();
        await InterpretAsync(root,
            """
            var baseValue = 10;
            function addBase(x) {
                return baseValue + x;
            }
            """);

        var child = root.CreateExecutionInterpreter();

        var childOutput = await RunWithInterpreterAsync(child,
            """
            baseValue = 20;
            print(addBase(5));
            """);

        var rootOutput = await RunWithInterpreterAsync(root,
            """
            print(addBase(5));
            """);

        Assert.Equal("25", childOutput);
        Assert.Equal("15", rootOutput);
    }

    [Fact]
    public async Task CreateExecutionInterpreter_DoesNotShareMutableGlobalObjects()
    {
        var root = new Interpreter.Interpreter();
        await InterpretAsync(root,
            """
            var config = { count: 1 };
            function currentCount() {
                return config.count;
            }
            """);

        var child = root.CreateExecutionInterpreter();

        var childOutput = await RunWithInterpreterAsync(child,
            """
            config.count = 2;
            print(currentCount());
            """);

        var rootOutput = await RunWithInterpreterAsync(root,
            """
            print(currentCount());
            """);

        Assert.Equal("2", childOutput);
        Assert.Equal("1", rootOutput);
    }

    [Fact]
    public async Task CreateExecutionInterpreter_PreservesClassesAndBuiltIns()
    {
        var root = new Interpreter.Interpreter();
        await InterpretAsync(root,
            """
            class Greeter {
                function shout() {
                    return upper("hello");
                }
            }
            """);

        var child = root.CreateExecutionInterpreter();

        var output = await RunWithInterpreterAsync(child,
            """
            var greeter = new Greeter();
            print(greeter.shout());
            print(string(123));
            """);

        var lines = output.Split('\n');
        Assert.Equal("HELLO", lines[0]);
        Assert.Equal("123", lines[1]);
    }

    [Fact]
    public async Task CreateExecutionInterpreter_PreservesDirectImportsAlreadyLoadedInRootGlobals()
    {
        var packagesDir = CreateTempDirectory("execution_import_globals_pkgs_");
        try
        {
            var storage = new PackageStorage(packagesDir);
            InstallPackage(
                storage,
                "sharedlib",
                "1.0.0",
                """
                var answer = 7;
                """);

            var root = new Interpreter.Interpreter();
            SetModuleLoader(root, CreateTestModuleLoader(storage));

            await InterpretAsync(root,
                """
                using sharedlib;
                """);

            var child = root.CreateExecutionInterpreter();

            var output = await RunWithInterpreterAsync(child,
                """
                print(answer);
                """);

            Assert.Equal("7", output);
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }

    [Fact]
    public async Task CreateExecutionInterpreter_ReimportUsesFreshModuleLoaderCache()
    {
        var packagesDir = CreateTempDirectory("execution_import_cache_pkgs_");
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

            var moduleLoader = CreateTestModuleLoader(storage);
            var root = new Interpreter.Interpreter();
            SetModuleLoader(root, moduleLoader);

            var rootFirstOutput = await RunWithInterpreterAsync(root,
                """
                using once;
                print(answer);
                """);

            var child = root.CreateExecutionInterpreter();
            SetModuleLoader(child, CreateTestModuleLoader(storage));

            var childOutput = await RunWithInterpreterAsync(child,
                """
                using once;
                print(answer);
                """);

            var rootSecondOutput = await RunWithInterpreterAsync(root,
                """
                using once;
                print(answer);
                """);

            Assert.Equal("module init\n7", rootFirstOutput);
            Assert.Equal("module init\n7", childOutput);
            Assert.Equal("7", rootSecondOutput);
        }
        finally
        {
            SafeDeleteDirectory(packagesDir);
        }
    }
}
