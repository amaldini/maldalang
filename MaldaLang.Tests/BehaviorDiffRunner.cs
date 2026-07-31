using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Loader;
using MaldaLang.Compiler;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Tests;

public sealed class BehaviorDiffOptions
{
    public int Seed { get; set; } = PropertyRunOptions.DefaultSeed;
    public int Iterations { get; set; } = 40;
    public int TrialTimeoutMs { get; set; } = PropertyRunOptions.DefaultTrialTimeoutMs;
    public bool EnableJsPilotHarness { get; set; }
}

public sealed class PropertyBehaviorDiffResult
{
    public string PropertyName { get; }
    public BehaviorSnapshot InterpreterSnapshot { get; }
    public BehaviorSnapshot CSharpSnapshot { get; }
    public BehaviorSnapshot JsSnapshot { get; }
    public IReadOnlyDictionary<string, BackendEligibility> BackendEligibilityByMode { get; }
    public BehaviorSnapshotDiff Diff { get; }
    public BehaviorSnapshotDiff? JsDiff { get; }

    public bool AreEquivalent => Diff.AreEqual && (JsDiff == null || JsDiff.AreEqual);

    public PropertyBehaviorDiffResult(
        string propertyName,
        BehaviorSnapshot interpreterSnapshot,
        BehaviorSnapshot csharpSnapshot,
        BehaviorSnapshot jsSnapshot,
        IReadOnlyDictionary<string, BackendEligibility> backendEligibilityByMode,
        BehaviorSnapshotDiff diff,
        BehaviorSnapshotDiff? jsDiff)
    {
        PropertyName = propertyName;
        InterpreterSnapshot = interpreterSnapshot;
        CSharpSnapshot = csharpSnapshot;
        JsSnapshot = jsSnapshot;
        BackendEligibilityByMode = backendEligibilityByMode;
        Diff = diff;
        JsDiff = jsDiff;
    }

    public string ToDiagnosticReport(int seed, int iterations)
    {
        var report = Diff.ToDiagnosticReport(PropertyName, seed, iterations, InterpreterSnapshot.Mode, CSharpSnapshot.Mode);
        if (JsSnapshot.Skipped && !string.IsNullOrWhiteSpace(JsSnapshot.SkipReason))
        {
            report += System.Environment.NewLine + $"JS status: {JsSnapshot.SkipReason}";
        }
        else if (JsDiff is { AreEqual: false })
        {
            report += System.Environment.NewLine + "JS divergence:";
            report += System.Environment.NewLine + JsDiff.ToDiagnosticReport(PropertyName, seed, iterations, InterpreterSnapshot.Mode, JsSnapshot.Mode);
        }

        return report;
    }
}

public static class BehaviorDiffRunner
{
    private const string JsPilotHarnessTodoReason =
        "skipped: JS pilot harness is disabled. Set BehaviorDiffOptions.EnableJsPilotHarness=true to execute eligible properties.";
    private const string JsResultMarker = "__MALDA_PROPERTY_RESULT__";
    private const string JsRuntimeMissingPrefix = "skipped: JS pilot harness runtime is unavailable.";

    public static IReadOnlyList<PropertyBehaviorDiffResult> RunInterpreterVsCSharpFromSource(string source, BehaviorDiffOptions? options = null)
    {
        var runOptions = options ?? new BehaviorDiffOptions();
        var statements = Parse(source);
        var properties = statements.OfType<PropertyDeclaration>().ToList();
        if (properties.Count == 0)
            throw new InvalidOperationException("No properties found in source for behavior diff.");

        TranspiledPropertyHost? transpiled = null;
        JsPropertyHost? jsHost = null;
        try
        {
            var results = new List<PropertyBehaviorDiffResult>();
            foreach (var property in properties)
            {
                var eligibility = BackendCapabilityMatrix.Evaluate(property);
                var eligibilityByMode = eligibility.ToDictionary(
                    e => BackendCapabilityMatrix.ToModeName(e.Backend),
                    e => e,
                    StringComparer.OrdinalIgnoreCase);

                var interpreterResult = RunInterpreterIfEligible(
                    statements,
                    property,
                    runOptions,
                    eligibilityByMode["interpreter"]);

                var csharpResult = RunCSharpIfEligible(
                    source,
                    property,
                    runOptions,
                    eligibilityByMode["csharp"],
                    ref transpiled);

                var jsResult = RunJsPilotIfEligible(
                    statements,
                    property,
                    runOptions,
                    eligibilityByMode["js"],
                    runOptions.EnableJsPilotHarness,
                    ref jsHost);

                var interpreterSnapshot = BehaviorSnapshot.FromPropertyResult("interpreter", interpreterResult);
                var csharpSnapshot = BehaviorSnapshot.FromPropertyResult("csharp-transpiled", csharpResult);
                var jsSnapshot = BehaviorSnapshot.FromPropertyResult("js", jsResult);
                var diff = BehaviorSnapshotDiff.Compare(interpreterSnapshot, csharpSnapshot);
                var jsDiff = jsSnapshot.Skipped ? null : BehaviorSnapshotDiff.Compare(interpreterSnapshot, jsSnapshot);
                results.Add(new PropertyBehaviorDiffResult(property.Name, interpreterSnapshot, csharpSnapshot, jsSnapshot, eligibilityByMode, diff, jsDiff));
            }

            return results;
        }
        finally
        {
            transpiled?.LoadContext.Unload();
            jsHost?.Dispose();
        }
    }

    private static PropertyRunResult RunInterpreterIfEligible(
        IReadOnlyList<Statement> statements,
        PropertyDeclaration property,
        BehaviorDiffOptions runOptions,
        BackendEligibility eligibility)
    {
        if (!eligibility.IsEligible)
        {
            return PropertyRunResult.Skipped(property.Name, runOptions.Seed, runOptions.Iterations, $"{eligibility.Status}: {eligibility.Reason}");
        }

        return new PropertyRunner().RunProperty(
            statements,
            property,
            new PropertyRunOptions
            {
                Seed = runOptions.Seed,
                Iterations = runOptions.Iterations,
                TrialTimeoutMs = runOptions.TrialTimeoutMs
            });
    }

    private static PropertyRunResult RunCSharpIfEligible(
        string source,
        PropertyDeclaration property,
        BehaviorDiffOptions runOptions,
        BackendEligibility eligibility,
        ref TranspiledPropertyHost? transpiled)
    {
        if (!eligibility.IsEligible)
        {
            return PropertyRunResult.Skipped(property.Name, runOptions.Seed, runOptions.Iterations, $"{eligibility.Status}: {eligibility.Reason}");
        }

        transpiled ??= LoadTranspiledPropertyHost(source);
        return RunTranspiledProperty(
            property,
            transpiled,
            runOptions.Seed,
            runOptions.Iterations,
            runOptions.TrialTimeoutMs);
    }

    private static PropertyRunResult RunJsPilotIfEligible(
        IReadOnlyList<Statement> statements,
        PropertyDeclaration property,
        BehaviorDiffOptions runOptions,
        BackendEligibility eligibility,
        bool enableJsPilotHarness,
        ref JsPropertyHost? host)
    {
        if (!eligibility.IsEligible)
        {
            return PropertyRunResult.Skipped(property.Name, runOptions.Seed, runOptions.Iterations, $"{eligibility.Status}: {eligibility.Reason}");
        }

        if (enableJsPilotHarness)
        {
            try
            {
                host ??= LoadJsPropertyHost(statements);
            }
            catch (Exception ex)
            {
                if (ex.Message.StartsWith(JsRuntimeMissingPrefix, StringComparison.Ordinal))
                {
                    return PropertyRunResult.Skipped(property.Name, runOptions.Seed, runOptions.Iterations, ex.Message);
                }

                return new PropertyRunResult(
                    property.Name,
                    passed: false,
                    seed: runOptions.Seed,
                    iterations: runOptions.Iterations,
                    failedTrial: 0,
                    errorMessage: "JS pilot harness setup failed: " + ex.Message);
            }

            return RunTranspiledJsProperty(
                property,
                host,
                runOptions.Seed,
                runOptions.Iterations,
                runOptions.TrialTimeoutMs);
        }

        return PropertyRunResult.Skipped(property.Name, runOptions.Seed, runOptions.Iterations, JsPilotHarnessTodoReason);
    }

    private static List<Statement> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new MaldaLang.Parser.Parser(tokens);
        var statements = parser.Parse();
        if (parser.Errors.Count > 0)
            throw new InvalidOperationException("Parse errors: " + string.Join("; ", parser.Errors.Select(e => e.Message)));
        return statements;
    }

    private static TranspiledPropertyHost LoadTranspiledPropertyHost(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_property_diff", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "property-diff.malda");
        var dllPath = Path.Combine(tempDir, "property-diff.dll");
        File.WriteAllText(sourcePath, source);

        var compiler = new Compiler.Compiler();
        var compileResult = compiler.Compile(
            sourcePath,
            dllPath,
            CompilationMode.TranspileToDll,
            includeLLamaSharp: false,
            includeUiHost: false);
        if (!compileResult.Success || string.IsNullOrWhiteSpace(compileResult.OutputPath) || !File.Exists(compileResult.OutputPath))
            throw new InvalidOperationException("Transpiled DLL compilation failed: " + (compileResult.ErrorMessage ?? "unknown error"));

        var loadContext = new AssemblyLoadContext($"malda-property-diff-{Guid.NewGuid():N}", isCollectible: true);
        var assembly = loadContext.LoadFromAssemblyPath(compileResult.OutputPath);
        var programType = assembly.GetType("GeneratedCode.Program")
            ?? throw new InvalidOperationException("Generated transpiled Program type not found.");
        var metadataMethod = programType.GetMethod("GetTranspiledProperties", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("GetTranspiledProperties method missing from transpiled output.");
        var invokeMethod = programType.GetMethod("InvokeTranspiledProperty", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("InvokeTranspiledProperty method missing from transpiled output.");

        var metadataObjects = (metadataMethod.Invoke(null, null) as System.Collections.IEnumerable)
            ?? throw new InvalidOperationException("Transpiled property metadata payload is not enumerable.");
        var metadataByName = new Dictionary<string, TranspiledPropertyMetadataRef>(StringComparer.Ordinal);
        foreach (var item in metadataObjects)
        {
            if (item == null)
                continue;

            var itemType = item.GetType();
            var name = itemType.GetProperty("Name")?.GetValue(item) as string;
            var parametersRaw = itemType.GetProperty("Parameters")?.GetValue(item) as System.Collections.IEnumerable;
            var sourceLineValue = itemType.GetProperty("SourceLine")?.GetValue(item);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var parameters = new List<string>();
            if (parametersRaw != null)
            {
                foreach (var p in parametersRaw)
                {
                    if (p is string ps)
                        parameters.Add(ps);
                }
            }

            metadataByName[name] = new TranspiledPropertyMetadataRef(name, parameters, sourceLineValue as int? ?? 0);
        }

        return new TranspiledPropertyHost(loadContext, invokeMethod, metadataByName);
    }

    private static PropertyRunResult RunTranspiledProperty(
        PropertyDeclaration declaration,
        TranspiledPropertyHost host,
        int seed,
        int iterations,
        int timeoutMs)
    {
        if (!host.MetadataByName.TryGetValue(declaration.Name, out var metadata))
            throw new InvalidOperationException($"Transpiled metadata missing property '{declaration.Name}'.");
        if (metadata.Parameters.Count != declaration.Parameters.Count)
            throw new InvalidOperationException($"Parameter count mismatch for property '{declaration.Name}'.");

        var random = new Random(seed);
        var generators = declaration.Parameters.Select(CreateGeneratorForParameter).ToList();

        for (var trial = 1; trial <= iterations; trial++)
        {
            var args = generators.Select(g => g.Next(random)).ToList();
            var counterexample = FormatArguments(args);
            var outcome = ExecuteTranspiledTrialWithTimeout(host, declaration.Name, args, timeoutMs);
            if (outcome.Passed)
                continue;

            var shrunk = ShrinkArguments(host, declaration.Name, generators, args, timeoutMs);
            return new PropertyRunResult(
                declaration.Name,
                passed: false,
                seed: seed,
                iterations: iterations,
                failedTrial: trial,
                errorMessage: outcome.ErrorMessage,
                counterexample: counterexample,
                shrunkCounterexample: FormatArguments(shrunk));
        }

        return new PropertyRunResult(
            declaration.Name,
            passed: true,
            seed: seed,
            iterations: iterations);
    }

    private static List<RuntimeValue> ShrinkArguments(
        TranspiledPropertyHost host,
        string propertyName,
        IReadOnlyList<PropertyGenerator> generators,
        IReadOnlyList<RuntimeValue> originalArgs,
        int timeoutMs)
    {
        var current = originalArgs.ToList();
        for (var i = 0; i < current.Count; i++)
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var candidate in generators[i].Shrink(current[i]))
                {
                    var trialArgs = current.ToList();
                    trialArgs[i] = candidate;
                    var outcome = ExecuteTranspiledTrialWithTimeout(host, propertyName, trialArgs, timeoutMs);
                    if (!outcome.Passed)
                    {
                        current = trialArgs;
                        changed = true;
                        break;
                    }
                }
            }
        }

        return current;
    }

    private static PropertyTrialOutcome ExecuteTranspiledTrialWithTimeout(
        TranspiledPropertyHost host,
        string propertyName,
        IReadOnlyList<RuntimeValue> args,
        int timeoutMs)
    {
        try
        {
            var task = ExecuteTranspiledTrialAsync(host, propertyName, args);
            return task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            return new PropertyTrialOutcome(false, $"Property trial exceeded timeout ({timeoutMs}ms).");
        }
    }

    private static async Task<PropertyTrialOutcome> ExecuteTranspiledTrialAsync(
        TranspiledPropertyHost host,
        string propertyName,
        IReadOnlyList<RuntimeValue> args)
    {
        try
        {
            var objectArgs = args.Select(ToPlainObject).ToArray();
            var taskObj = host.InvokeMethod.Invoke(null, new object[] { propertyName, objectArgs });
            if (taskObj is not Task<object> task)
                return new PropertyTrialOutcome(false, "Transpiled property invocation did not return Task<object>.");

            var result = await task;
            if (result is bool b && !b)
                return new PropertyTrialOutcome(false, "Property returned false.");
            if (result is RuntimeValue rv && rv.Type == MaldaLang.Interpreter.ValueType.Boolean && !rv.AsBoolean())
                return new PropertyTrialOutcome(false, "Property returned false.");
            return new PropertyTrialOutcome(true, null);
        }
        catch (TargetInvocationException ex)
        {
            return new PropertyTrialOutcome(false, ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            return new PropertyTrialOutcome(false, ex.Message);
        }
    }

    private static object? ToPlainObject(RuntimeValue value)
    {
        return value.Type switch
        {
            MaldaLang.Interpreter.ValueType.Integer => value.AsInteger(),
            MaldaLang.Interpreter.ValueType.Float => value.AsFloat(),
            MaldaLang.Interpreter.ValueType.Boolean => value.AsBoolean(),
            MaldaLang.Interpreter.ValueType.String => value.AsString(),
            MaldaLang.Interpreter.ValueType.Null => null,
            MaldaLang.Interpreter.ValueType.Array => value.AsArray().Select(ToPlainObject).ToList(),
            _ => value.ToString()
        };
    }

    private static JsPropertyHost LoadJsPropertyHost(IReadOnlyList<Statement> statements)
    {
        var runtimePath = ResolveJsRuntimePath();
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_property_diff_js", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var programPath = Path.Combine(tempDir, "property-diff.js");
        var harnessPath = Path.Combine(tempDir, "property-harness.js");

        var properties = statements.OfType<PropertyDeclaration>().ToList();
        var jsStatements = new List<Statement>(statements.Count);
        foreach (var statement in statements)
        {
            if (statement is PropertyDeclaration property)
            {
                jsStatements.Add(new FunctionDeclaration(
                    property.Name,
                    property.Parameters.ToList(),
                    property.Body,
                    decorators: property.Decorators,
                    line: property.Line,
                    column: property.Column));
            }
            else
            {
                jsStatements.Add(statement);
            }
        }

        var transpiler = new JsTranspiler();
        var jsCode = transpiler.Transpile(jsStatements, isLibrary: false, sourceFilePath: null);
        var jsWithExports = EnsurePropertyFunctionsAreExported(jsCode, properties.Select(p => p.Name).ToArray());
        File.WriteAllText(programPath, jsWithExports);
        File.WriteAllText(harnessPath, GetNodeHarnessScript());
        return new JsPropertyHost(tempDir, runtimePath, programPath, harnessPath);
    }

    private static string EnsurePropertyFunctionsAreExported(string jsCode, IReadOnlyList<string> propertyNames)
    {
        if (propertyNames.Count == 0)
        {
            return jsCode;
        }

        var matches = Regex.Matches(jsCode, @"return\s*\{\s*([^}]*)\s*\};");
        if (matches.Count == 0)
        {
            throw new InvalidOperationException("JS pilot harness could not locate module export object in transpiled JavaScript.");
        }

        var match = matches[^1];
        var existingExportsRaw = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
        foreach (var propertyName in propertyNames)
        {
            if (!existingExportsRaw.Contains(propertyName, StringComparer.Ordinal))
            {
                existingExportsRaw.Add(propertyName);
            }
        }

        var merged = "return { " + string.Join(", ", existingExportsRaw) + " };";
        return jsCode[..match.Index] + merged + jsCode[(match.Index + match.Length)..];
    }

    private static string ResolveJsRuntimePath()
    {
        var overridePath = System.Environment.GetEnvironmentVariable("MALDA_JS_RUNTIME_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var probeRoots = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var root in probeRoots)
        {
            var current = new DirectoryInfo(root);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "Examples", "Web", "wwwroot", "malda-js-runtime.js");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException(
            JsRuntimeMissingPrefix +
            " Could not locate 'Examples/Web/wwwroot/malda-js-runtime.js'. Set MALDA_JS_RUNTIME_PATH to override.");
    }

    public static bool IsJsPilotRuntimeAvailable(out string reason)
    {
        try
        {
            _ = ResolveJsRuntimePath();
            _ = ResolveNodeExecutablePath();
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static PropertyRunResult RunTranspiledJsProperty(
        PropertyDeclaration declaration,
        JsPropertyHost host,
        int seed,
        int iterations,
        int timeoutMs)
    {
        var random = new Random(seed);
        var generators = declaration.Parameters.Select(CreateGeneratorForParameter).ToList();

        for (var trial = 1; trial <= iterations; trial++)
        {
            var args = generators.Select(g => g.Next(random)).ToList();
            var counterexample = FormatArguments(args);
            var outcome = ExecuteJsTrialWithTimeout(host, declaration.Name, args, timeoutMs);
            if (outcome.Passed)
            {
                continue;
            }

            if (outcome.SkipReason != null)
            {
                return PropertyRunResult.Skipped(declaration.Name, seed, iterations, outcome.SkipReason);
            }

            var shrunk = ShrinkJsArguments(host, declaration.Name, generators, args, timeoutMs);
            return new PropertyRunResult(
                declaration.Name,
                passed: false,
                seed: seed,
                iterations: iterations,
                failedTrial: trial,
                errorMessage: outcome.ErrorMessage,
                counterexample: counterexample,
                shrunkCounterexample: FormatArguments(shrunk));
        }

        return new PropertyRunResult(
            declaration.Name,
            passed: true,
            seed: seed,
            iterations: iterations);
    }

    private static List<RuntimeValue> ShrinkJsArguments(
        JsPropertyHost host,
        string propertyName,
        IReadOnlyList<PropertyGenerator> generators,
        IReadOnlyList<RuntimeValue> originalArgs,
        int timeoutMs)
    {
        var current = originalArgs.ToList();
        for (var i = 0; i < current.Count; i++)
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var candidate in generators[i].Shrink(current[i]))
                {
                    var trialArgs = current.ToList();
                    trialArgs[i] = candidate;
                    var outcome = ExecuteJsTrialWithTimeout(host, propertyName, trialArgs, timeoutMs);
                    if (!outcome.Passed)
                    {
                        current = trialArgs;
                        changed = true;
                        break;
                    }
                }
            }
        }

        return current;
    }

    private static JsTrialOutcome ExecuteJsTrialWithTimeout(
        JsPropertyHost host,
        string propertyName,
        IReadOnlyList<RuntimeValue> args,
        int timeoutMs)
    {
        try
        {
            var nodePath = ResolveNodeExecutablePath();
            var plainArgs = args.Select(ToPlainObject).ToArray();
            var argsJson = JsonSerializer.Serialize(plainArgs);
            var startInfo = new ProcessStartInfo
            {
                FileName = nodePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = host.TempDir
            };
            startInfo.ArgumentList.Add(host.HarnessPath);
            startInfo.ArgumentList.Add(host.RuntimePath);
            startInfo.ArgumentList.Add(host.ProgramPath);
            startInfo.ArgumentList.Add(propertyName);
            startInfo.ArgumentList.Add(argsJson);

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new JsTrialOutcome(false, "JS pilot harness failed to start Node process.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExit(timeoutMs);
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new JsTrialOutcome(false, $"Property trial exceeded timeout ({timeoutMs}ms) in JS harness.");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            var markerPayload = ExtractJsResultPayload(stdout);
            if (process.ExitCode != 0)
            {
                var details = BuildJsFailureDetails(process.ExitCode, stderr, markerPayload, stdout);
                return new JsTrialOutcome(false, details);
            }

            if (markerPayload == null)
            {
                return new JsTrialOutcome(false, "JS harness did not produce a result marker. StdOut: " + BehaviorSnapshotDiff.NormalizeText(stdout));
            }

            using var doc = JsonDocument.Parse(markerPayload);
            if (!doc.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
            {
                var error = doc.RootElement.TryGetProperty("error", out var errorElement)
                    ? errorElement.GetString()
                    : "Unknown JS harness error.";
                return new JsTrialOutcome(false, "JS harness failed: " + error);
            }

            if (doc.RootElement.TryGetProperty("result", out var resultElement) &&
                resultElement.ValueKind == JsonValueKind.False)
            {
                return new JsTrialOutcome(false, "Property returned false.");
            }

            return new JsTrialOutcome(true, null);
        }
        catch (Exception ex) when (ex is Win32Exception || ex is FileNotFoundException)
        {
            return new JsTrialOutcome(
                false,
                null,
                SkipReason: JsRuntimeMissingPrefix + " Node.js executable was not found. Install Node.js or set MALDA_NODE_PATH.");
        }
        catch (Exception ex)
        {
            return new JsTrialOutcome(false, "JS harness execution failed: " + ex.Message);
        }
    }

    private static string ResolveNodeExecutablePath()
    {
        var configured = System.Environment.GetEnvironmentVariable("MALDA_NODE_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return "node";
    }

    private static string? ExtractJsResultPayload(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        var normalized = stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.StartsWith(JsResultMarker, StringComparison.Ordinal))
            {
                return line[JsResultMarker.Length..];
            }
        }

        return null;
    }

    private static string BuildJsFailureDetails(int exitCode, string stderr, string? markerPayload, string stdout)
    {
        if (!string.IsNullOrWhiteSpace(markerPayload))
        {
            try
            {
                using var markerDoc = JsonDocument.Parse(markerPayload);
                if (markerDoc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    var markerError = errorElement.GetString();
                    if (!string.IsNullOrWhiteSpace(markerError))
                    {
                        return markerError;
                    }
                }
            }
            catch
            {
                // Ignore malformed marker payload and continue with stderr.
            }
        }

        var normalizedErr = BehaviorSnapshotDiff.NormalizeText(stderr);
        if (!string.IsNullOrWhiteSpace(normalizedErr))
        {
            return $"JS process exited with code {exitCode}. {normalizedErr}";
        }

        var normalizedOut = BehaviorSnapshotDiff.NormalizeText(stdout);
        return $"JS process exited with code {exitCode}. StdOut: {normalizedOut}";
    }

    private static string GetNodeHarnessScript()
    {
        return """
const fs = require("fs");

async function run() {
  const runtimePath = process.argv[2];
  const programPath = process.argv[3];
  const propertyName = process.argv[4];
  const argsJson = process.argv[5] || "[]";

  if (!runtimePath || !programPath || !propertyName) {
    throw new Error("Usage: node property-harness.js <runtimePath> <programPath> <propertyName> <argsJson>");
  }

  if (!fs.existsSync(runtimePath)) {
    throw new Error("MALDA JS runtime not found at: " + runtimePath);
  }

  require(runtimePath);
  const app = require(programPath);
  if (!app || typeof app[propertyName] !== "function") {
    throw new Error("Transpiled JS module does not export property function '" + propertyName + "'.");
  }

  const args = JSON.parse(argsJson);
  const result = await app[propertyName](...(Array.isArray(args) ? args : []));
  process.stdout.write("__MALDA_PROPERTY_RESULT__" + JSON.stringify({ ok: true, result }) + "\n");
}

run().catch((error) => {
  const detail = error && (error.stack || error.message) ? (error.stack || error.message) : String(error);
  process.stdout.write("__MALDA_PROPERTY_RESULT__" + JSON.stringify({ ok: false, error: detail }) + "\n");
  process.exit(1);
});
""";
    }

    private static PropertyGenerator CreateGeneratorForParameter(string parameterName)
    {
        var name = parameterName.ToLowerInvariant();
        if (name.EndsWith("bool", StringComparison.Ordinal) ||
            name.StartsWith("is", StringComparison.Ordinal) ||
            name.StartsWith("has", StringComparison.Ordinal) ||
            name.Contains("flag", StringComparison.Ordinal))
        {
            return PropertyGenerators.Bool();
        }

        if (name.EndsWith("string", StringComparison.Ordinal) ||
            name.Contains("name", StringComparison.Ordinal) ||
            name.Contains("text", StringComparison.Ordinal))
        {
            return PropertyGenerators.String(16);
        }

        if (name.EndsWith("list", StringComparison.Ordinal) ||
            name.EndsWith("items", StringComparison.Ordinal) ||
            name.EndsWith("array", StringComparison.Ordinal) ||
            name == "xs")
        {
            return PropertyGenerators.List(PropertyGenerators.Int(-32, 32), 8);
        }

        if (name.Contains("any", StringComparison.Ordinal))
        {
            return PropertyGenerators.OneOf(
                PropertyGenerators.Int(-100, 100),
                PropertyGenerators.Bool(),
                PropertyGenerators.String(12),
                PropertyGenerators.List(PropertyGenerators.Int(-8, 8), 5));
        }

        return PropertyGenerators.Int(-100, 100);
    }

    private static string FormatArguments(IReadOnlyList<RuntimeValue> args)
    {
        return "[" + string.Join(", ", args.Select(FormatValue)) + "]";
    }

    private static string FormatValue(RuntimeValue value)
    {
        return value.Type switch
        {
            MaldaLang.Interpreter.ValueType.Integer => value.AsInteger().ToString(CultureInfo.InvariantCulture),
            MaldaLang.Interpreter.ValueType.Float => value.AsFloat().ToString("G", CultureInfo.InvariantCulture),
            MaldaLang.Interpreter.ValueType.Boolean => value.AsBoolean() ? "true" : "false",
            MaldaLang.Interpreter.ValueType.String => "\"" + value.AsString().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
            MaldaLang.Interpreter.ValueType.Null => "null",
            MaldaLang.Interpreter.ValueType.Array => "[" + string.Join(", ", value.AsArray().Select(FormatValue)) + "]",
            _ => value.ToString()
        };
    }

    private sealed record TranspiledPropertyMetadataRef(string Name, IReadOnlyList<string> Parameters, int SourceLine);
    private sealed record TranspiledPropertyHost(AssemblyLoadContext LoadContext, MethodInfo InvokeMethod, IReadOnlyDictionary<string, TranspiledPropertyMetadataRef> MetadataByName);
    private sealed record PropertyTrialOutcome(bool Passed, string? ErrorMessage);
    private sealed record JsTrialOutcome(bool Passed, string? ErrorMessage, string? SkipReason = null);

    private sealed class JsPropertyHost : IDisposable
    {
        public string TempDir { get; }
        public string RuntimePath { get; }
        public string ProgramPath { get; }
        public string HarnessPath { get; }

        public JsPropertyHost(string tempDir, string runtimePath, string programPath, string harnessPath)
        {
            TempDir = tempDir;
            RuntimePath = runtimePath;
            ProgramPath = programPath;
            HarnessPath = harnessPath;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(TempDir, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failures in test harness.
            }
        }
    }
}
