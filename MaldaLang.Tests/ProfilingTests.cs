namespace MaldaLang.Tests;

using System.IO;
using System.Linq;
using System.Text.Json;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Runtime.Profiling;

public class ProfilingTests : TestBase
{
    [Fact]
    public async Task InterpreterProfiling_WritesBuiltInsFunctionsAndStatements()
    {
        var tempDir = CreateTempDirectory("malda_profile_interp_");
        try
        {
            var profilePath = Path.Combine(tempDir, "interpreter-profile.json");
            var source = """
function hot(value) {
    print(string(value));
    return value;
}

hot(1);
hot(2);
""";

            var lexer = new Lexer(source, "profile_test.malda");
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, "profile_test.malda");
            var statements = parser.Parse();
            var interpreter = new MaldaLang.Interpreter.Interpreter(currentFile: "profile_test.malda");
            interpreter.SetSourceCode(source);

            MaldaProfiler.StartSession(new ProfilingOptions
            {
                Enabled = true,
                OutputPath = profilePath,
                Format = ProfilingFormat.Json,
                WriteToConsole = false,
                MaxEntriesPerSection = 50
            }, "profile_test.malda");

            try
            {
                await interpreter.InterpretAsync(statements);
            }
            finally
            {
                MaldaProfiler.CompleteSession();
            }

            Assert.True(File.Exists(profilePath));
            using var doc = JsonDocument.Parse(File.ReadAllText(profilePath));
            AssertProfileContains(doc, "Functions", "hot");
            AssertProfileContains(doc, "BuiltIns", "string");
            AssertProfileContains(doc, "Statements", "Print");
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TranspiledProfiling_WritesBuiltInsFunctionsAndStatements()
    {
        var tempDir = CreateTempDirectory("malda_profile_transpiled_");
        try
        {
            var profilePath = Path.Combine(tempDir, "transpiled-profile.json");
            var source = """
function hot(value) {
    print(string(value));
    return value;
}

hot(1);
hot(2);
""";

            var result = TranspiledTestRunner.CompileAndRunFromSource(
                source,
                includeUiHost: false,
                environmentVariables: null,
                commandLineArgs: null,
                profilingOptions: new ProfilingOptions
                {
                    Enabled = true,
                    OutputPath = profilePath,
                    Format = ProfilingFormat.Json,
                    WriteToConsole = false,
                    MaxEntriesPerSection = 50
                });

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(profilePath));

            using var doc = JsonDocument.Parse(File.ReadAllText(profilePath));
            AssertProfileContains(doc, "Functions", "hot");
            AssertProfileContains(doc, "BuiltIns", "string");
            AssertProfileContains(doc, "Statements", "Print");
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TranspiledProfiling_CoercionBuiltIns_UseSyncCodegenPath_WhenArgumentsAreSynchronous()
    {
        var tempDir = CreateTempDirectory("malda_profile_transpiled_coercion_sync_");
        try
        {
            var profilePath = Path.Combine(tempDir, "transpiled-coercion-sync-profile.json");
            var source = """
print(string(1));
print(int("2"));
print(float("3.5"));
""";

            var result = TranspiledTestRunner.CompileAndRunFromSource(
                source,
                includeUiHost: false,
                environmentVariables: null,
                commandLineArgs: null,
                profilingOptions: new ProfilingOptions
                {
                    Enabled = true,
                    OutputPath = profilePath,
                    Format = ProfilingFormat.Json,
                    WriteToConsole = false,
                    MaxEntriesPerSection = 50
                });

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("1\n2\n3.5", result.StdOut.Trim());
            Assert.True(File.Exists(profilePath));

            using var doc = JsonDocument.Parse(File.ReadAllText(profilePath));
            AssertProfileContains(doc, "BuiltIns", "string");
            AssertProfileContains(doc, "BuiltIns", "int");
            AssertProfileContains(doc, "BuiltIns", "float");
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TranspiledProfiling_CoercionBuiltIns_FallBackToAsyncCodegenPath_WhenArgumentsContainAwait()
    {
        var tempDir = CreateTempDirectory("malda_profile_transpiled_coercion_async_");
        try
        {
            var profilePath = Path.Combine(tempDir, "transpiled-coercion-async-profile.json");
            var source = """
var t1 = async 1;
var t2 = async 2;
var t3 = async "3.5";
var baseValue = async "hello";
var suffix = async "lo";

print(string(await t1));
print(int(string(await t2)));
print(float(await t3));
print(string((await baseValue).endsWith(await suffix)));
""";

            var result = TranspiledTestRunner.CompileAndRunFromSource(
                source,
                includeUiHost: false,
                environmentVariables: null,
                commandLineArgs: null,
                profilingOptions: new ProfilingOptions
                {
                    Enabled = true,
                    OutputPath = profilePath,
                    Format = ProfilingFormat.Json,
                    WriteToConsole = false,
                    MaxEntriesPerSection = 50
                });

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("1\n2\n3.5\ntrue", result.StdOut.Trim());
            Assert.True(File.Exists(profilePath));

            using var doc = JsonDocument.Parse(File.ReadAllText(profilePath));
            AssertProfileContains(doc, "BuiltIns", "string");
            AssertProfileContains(doc, "BuiltIns", "int");
            AssertProfileContains(doc, "BuiltIns", "float");
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task InterpreterProfiling_UsesIncludedFileForFunctionAndStatementLocations()
    {
        var tempDir = CreateTempDirectory("malda_profile_interp_include_");
        try
        {
            var mainPath = Path.Combine(tempDir, "main.malda");
            var libPath = Path.Combine(tempDir, "lib.malda");
            var profilePath = Path.Combine(tempDir, "interpreter-include-profile.json");

            File.WriteAllText(libPath, """
function hot(value) {
    print(string(value));
    return value;
}
""");

            File.WriteAllText(mainPath, """
include "lib.malda";

hot(1);
""");

            var source = File.ReadAllText(mainPath);
            var lexer = new Lexer(source, mainPath);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, mainPath);
            var statements = parser.Parse();
            var interpreter = new MaldaLang.Interpreter.Interpreter(currentFile: mainPath);
            interpreter.SetSourceCode(source);

            MaldaProfiler.StartSession(new ProfilingOptions
            {
                Enabled = true,
                OutputPath = profilePath,
                Format = ProfilingFormat.Json,
                WriteToConsole = false,
                MaxEntriesPerSection = 50
            }, mainPath);

            try
            {
                await interpreter.InterpretAsync(statements);
            }
            finally
            {
                MaldaProfiler.CompleteSession();
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(profilePath));
            AssertProfileEntryFile(doc, "Functions", "hot", libPath);
            AssertProfileEntryFile(doc, "Statements", "Print", libPath);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TranspiledProfiling_UsesIncludedFileForFunctionAndStatementLocations()
    {
        var tempDir = CreateTempDirectory("malda_profile_transpiled_include_");
        try
        {
            var mainPath = Path.Combine(tempDir, "main.malda");
            var libPath = Path.Combine(tempDir, "lib.malda");
            var profilePath = Path.Combine(tempDir, "transpiled-include-profile.json");

            File.WriteAllText(libPath, """
function hot(value) {
    print(string(value));
    return value;
}
""");

            File.WriteAllText(mainPath, """
include "lib.malda";

hot(1);
""");

            var result = TranspiledTestRunner.CompileAndRunFromFile(
                mainPath,
                includeUiHost: false,
                environmentVariables: null,
                commandLineArgs: null,
                profilingOptions: new ProfilingOptions
                {
                    Enabled = true,
                    OutputPath = profilePath,
                    Format = ProfilingFormat.Json,
                    WriteToConsole = false,
                    MaxEntriesPerSection = 50
                });

            Assert.Equal(0, result.ExitCode);

            using var doc = JsonDocument.Parse(File.ReadAllText(profilePath));
            AssertProfileEntryFile(doc, "Functions", "hot", libPath);
            AssertProfileEntryFile(doc, "Statements", "Print", libPath);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task InterpreterProfiling_OmitsSyntheticStatementsWithoutSourceLine()
    {
        var tempDir = CreateTempDirectory("malda_profile_interp_synthetic_");
        try
        {
            var profilePath = Path.Combine(tempDir, "interpreter-synthetic-profile.json");
            var source = """
var sum = 0;
for (var i = 0; i < 3; i = i + 1) {
    sum = sum + i;
}
print(sum);
""";

            var lexer = new Lexer(source, "profile_synthetic_test.malda");
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, "profile_synthetic_test.malda");
            var statements = parser.Parse();
            var interpreter = new MaldaLang.Interpreter.Interpreter(currentFile: "profile_synthetic_test.malda");
            interpreter.SetSourceCode(source);

            MaldaProfiler.StartSession(new ProfilingOptions
            {
                Enabled = true,
                OutputPath = profilePath,
                Format = ProfilingFormat.Json,
                WriteToConsole = false,
                MaxEntriesPerSection = 50
            }, "profile_synthetic_test.malda");

            try
            {
                await interpreter.InterpretAsync(statements);
            }
            finally
            {
                MaldaProfiler.CompleteSession();
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(profilePath));
            AssertNoStatementWithLine(doc, 0);
            AssertProfileContains(doc, "Statements", "Print");
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TranspiledProfiling_OmitsSyntheticStatementsWithoutSourceLine()
    {
        var tempDir = CreateTempDirectory("malda_profile_transpiled_synthetic_");
        try
        {
            var profilePath = Path.Combine(tempDir, "transpiled-synthetic-profile.json");
            var source = """
var sum = 0;
for (var i = 0; i < 3; i = i + 1) {
    sum = sum + i;
}
print(sum);
""";

            var result = TranspiledTestRunner.CompileAndRunFromSource(
                source,
                includeUiHost: false,
                environmentVariables: null,
                commandLineArgs: null,
                profilingOptions: new ProfilingOptions
                {
                    Enabled = true,
                    OutputPath = profilePath,
                    Format = ProfilingFormat.Json,
                    WriteToConsole = false,
                    MaxEntriesPerSection = 50
                });

            Assert.Equal(0, result.ExitCode);

            using var doc = JsonDocument.Parse(File.ReadAllText(profilePath));
            AssertNoStatementWithLine(doc, 0);
            AssertProfileContains(doc, "Statements", "Print");
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task Profiling_PeriodicSnapshot_WritesWhileProfiledWorkIsStillRunning()
    {
        var tempDir = CreateTempDirectory("malda_profile_periodic_timer_");
        try
        {
            var profilePath = Path.Combine(tempDir, "periodic-profile.json");

            MaldaProfiler.StartSession(new ProfilingOptions
            {
                Enabled = true,
                OutputPath = profilePath,
                Format = ProfilingFormat.Json,
                WriteToConsole = false,
                MaxEntriesPerSection = 50,
                PeriodicSnapshotSeconds = 0.05
            }, "periodic_timer_test");

            var token = MaldaProfiler.EnterFunction("slowWork", "periodic_timer_test.malda", 12);
            try
            {
                var snapshotJson = await WaitForJsonConditionAsync(
                    profilePath,
                    doc => doc.RootElement.GetProperty("Partial").GetBoolean() &&
                           doc.RootElement.GetProperty("SessionName").GetString() == "periodic_timer_test" &&
                           SectionContainsEntryWithCalls(doc, "Functions", "slowWork", minimumCalls: 1),
                    TimeSpan.FromSeconds(2));

                using var doc = JsonDocument.Parse(snapshotJson);
                Assert.True(doc.RootElement.GetProperty("Partial").GetBoolean());
                Assert.Equal("periodic_timer_test", doc.RootElement.GetProperty("SessionName").GetString());
                Assert.True(SectionContainsEntryWithCalls(doc, "Functions", "slowWork", minimumCalls: 1));
            }
            finally
            {
                MaldaProfiler.Exit(token);
                MaldaProfiler.CompleteSession();
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Profiler_RecordsBuiltInWhenExitRunsOnThreadPool_AsyncLocalNotPropagated()
    {
        var tempDir = CreateTempDirectory("malda_profile_threadpool_");
        try
        {
            var profilePath = Path.Combine(tempDir, "threadpool-profile.json");
            MaldaProfiler.StartSession(new ProfilingOptions
            {
                Enabled = true,
                OutputPath = profilePath,
                Format = ProfilingFormat.Json,
                WriteToConsole = false,
                MaxEntriesPerSection = 50
            }, "threadpool_test");

            var token = MaldaProfiler.EnterBuiltIn("poolBuiltIn");
            var worker = new System.Threading.Tasks.Task(() => { MaldaProfiler.Exit(token); });
            worker.Start();
            worker.Wait();

            MaldaProfiler.CompleteSession();

            Assert.True(File.Exists(profilePath));
            using var doc = JsonDocument.Parse(File.ReadAllText(profilePath));
            AssertProfileContains(doc, "BuiltIns", "poolBuiltIn");
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private static void AssertProfileContains(JsonDocument document, string sectionName, string expectedName)
    {
        var section = document.RootElement.GetProperty(sectionName);
        Assert.True(
            section.EnumerateArray().Any(entry => entry.GetProperty("Name").GetString() == expectedName),
            $"Expected profile section '{sectionName}' to contain entry '{expectedName}'.");
    }

    private static void AssertProfileEntryFile(JsonDocument document, string sectionName, string expectedName, string expectedFile)
    {
        var section = document.RootElement.GetProperty(sectionName);
        var entry = section.EnumerateArray().FirstOrDefault(item => item.GetProperty("Name").GetString() == expectedName);
        Assert.True(entry.ValueKind != JsonValueKind.Undefined, $"Expected profile section '{sectionName}' to contain entry '{expectedName}'.");

        var actualFile = entry.GetProperty("File").GetString();
        Assert.Equal(Path.GetFullPath(expectedFile), Path.GetFullPath(actualFile ?? string.Empty));
    }

    private static void AssertNoStatementWithLine(JsonDocument document, int forbiddenLine)
    {
        var statements = document.RootElement.GetProperty("Statements");
        Assert.DoesNotContain(
            statements.EnumerateArray(),
            entry => entry.TryGetProperty("Line", out var lineElement) && lineElement.ValueKind == JsonValueKind.Number && lineElement.GetInt32() == forbiddenLine);
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadlineUtc = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadlineUtc)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(predicate(), "Timed out waiting for condition.");
    }

    private static bool SectionContainsEntryWithCalls(JsonDocument document, string sectionName, string expectedName, long minimumCalls)
    {
        var section = document.RootElement.GetProperty(sectionName);
        return section.EnumerateArray().Any(entry =>
            entry.GetProperty("Name").GetString() == expectedName &&
            entry.GetProperty("Calls").GetInt64() >= minimumCalls);
    }

    private static async Task<string> WaitForJsonConditionAsync(string path, Func<JsonDocument, bool> predicate, TimeSpan timeout)
    {
        var deadlineUtc = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadlineUtc)
        {
            if (TryReadJson(path, predicate, out var json))
            {
                return json;
            }

            await Task.Delay(25);
        }

        Assert.True(TryReadJson(path, predicate, out var finalJson), $"Timed out waiting for JSON condition on '{path}'.");
        return finalJson;
    }

    private static bool TryReadJson(string path, Func<JsonDocument, bool> predicate, out string json)
    {
        json = string.Empty;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            return predicate(doc);
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
