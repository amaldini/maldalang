// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInRunProperty(List<RuntimeValue> args, Interpreter? interpreter)
    {
            BuiltInArity.Require("runProperty", args, 1, 3, "name, iterations?, seed?");
        if (args[0].Type != ValueType.String)
            throw new Exception("runProperty name must be a string");

        var propertyName = args[0].AsString();
        var iterations = PropertyRunOptions.DefaultIterations;
        var seed = PropertyRunOptions.DefaultSeed;

        if (args.Count > 1)
        {
            if (!NumericCoercion.TryAsInteger(args[1], out iterations))
                throw new Exception("runProperty iterations must be an integer");
            if (iterations <= 0)
                throw new Exception("runProperty iterations must be > 0");
        }

        if (args.Count > 2)
        {
            if (!NumericCoercion.TryAsInteger(args[2], out seed))
                throw new Exception("runProperty seed must be an integer");
        }

        return interpreter != null
            ? RunPropertyInInterpreterContext(interpreter, propertyName, iterations, seed)
            : RunPropertyInTranspiledContext(propertyName, iterations, seed);
    }

    private static RuntimeValue RunPropertyInInterpreterContext(Interpreter interpreter, string propertyName, int iterations, int seed)
    {
        var declaration = interpreter.GetProperty(propertyName);
        if (declaration == null)
            throw new Exception($"Property '{propertyName}' was not found.");

        var random = new Random(seed);
        var generators = declaration.Parameters.Select(CreatePropertyGeneratorForParameter).ToList();
        var timeoutMs = PropertyRunOptions.DefaultTrialTimeoutMs;

        for (var trial = 1; trial <= iterations; trial++)
        {
            var trialArgs = generators.Select(g => g.Next(random)).ToList();
            var counterexample = FormatPropertyArguments(trialArgs);
            var outcome = ExecutePropertyTrialInInterpreterWithTimeout(interpreter, declaration, trialArgs, timeoutMs);
            if (outcome.Passed)
                continue;

            return BuildRunPropertyResult(
                propertyName,
                passed: false,
                iterations: iterations,
                seed: seed,
                failedTrial: trial,
                error: outcome.ErrorMessage,
                counterexample: counterexample,
                shrunkCounterexample: null);
        }

        return BuildRunPropertyResult(
            propertyName,
            passed: true,
            iterations: iterations,
            seed: seed,
            failedTrial: null,
            error: null,
            counterexample: null,
            shrunkCounterexample: null);
    }

    private static RuntimeValue RunPropertyInTranspiledContext(string propertyName, int iterations, int seed)
    {
        var programType = ResolveTranspiledProgramType()
            ?? throw new Exception("runProperty() is unavailable: transpiled Program type was not found.");

        var metadataMethod = programType.GetMethod("GetTranspiledProperties", BindingFlags.Public | BindingFlags.Static)
            ?? throw new Exception("runProperty() is unavailable: GetTranspiledProperties() was not found.");
        var invokeMethod = programType.GetMethod("InvokeTranspiledProperty", BindingFlags.Public | BindingFlags.Static)
            ?? throw new Exception("runProperty() is unavailable: InvokeTranspiledProperty() was not found.");

        var metadataEnumerable = metadataMethod.Invoke(null, null) as System.Collections.IEnumerable
            ?? throw new Exception("runProperty() is unavailable: property metadata payload is invalid.");

        object? propertyMetadata = null;
        foreach (var item in metadataEnumerable)
        {
            if (item == null) continue;
            var itemName = item.GetType().GetProperty("Name")?.GetValue(item) as string;
            if (string.Equals(itemName, propertyName, StringComparison.Ordinal))
            {
                propertyMetadata = item;
                break;
            }
        }

        if (propertyMetadata == null)
            throw new Exception($"Property '{propertyName}' was not found in transpiled metadata.");

        var parameterNames = new List<string>();
        var parametersRaw = propertyMetadata.GetType().GetProperty("Parameters")?.GetValue(propertyMetadata) as System.Collections.IEnumerable;
        if (parametersRaw != null)
        {
            foreach (var parameter in parametersRaw)
            {
                if (parameter is string p)
                    parameterNames.Add(p);
            }
        }

        var random = new Random(seed);
        var generators = parameterNames.Select(CreatePropertyGeneratorForParameter).ToList();
        var timeoutMs = PropertyRunOptions.DefaultTrialTimeoutMs;

        for (var trial = 1; trial <= iterations; trial++)
        {
            var trialArgs = generators.Select(g => g.Next(random)).ToList();
            var counterexample = FormatPropertyArguments(trialArgs);
            var outcome = ExecutePropertyTrialInTranspiledWithTimeout(invokeMethod, propertyName, trialArgs, timeoutMs);
            if (outcome.Passed)
                continue;

            return BuildRunPropertyResult(
                propertyName,
                passed: false,
                iterations: iterations,
                seed: seed,
                failedTrial: trial,
                error: outcome.ErrorMessage,
                counterexample: counterexample,
                shrunkCounterexample: null);
        }

        return BuildRunPropertyResult(
            propertyName,
            passed: true,
            iterations: iterations,
            seed: seed,
            failedTrial: null,
            error: null,
            counterexample: null,
            shrunkCounterexample: null);
    }

    private static System.Type? ResolveTranspiledProgramType()
    {
        var entry = Assembly.GetEntryAssembly();
        var entryProgramType = entry?.GetType("GeneratedCode.Program") ?? entry?.GetType("Program");
        if (entryProgramType != null)
            return entryProgramType;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var programType = assembly.GetType("GeneratedCode.Program") ?? assembly.GetType("Program");
            if (programType != null)
                return programType;
        }

        return null;
    }

    private static PropertyTrialResult ExecutePropertyTrialInInterpreterWithTimeout(
        Interpreter interpreter,
        PropertyDeclaration declaration,
        List<RuntimeValue> trialArgs,
        int timeoutMs)
    {
        try
        {
            return ExecutePropertyTrialInInterpreterAsync(interpreter, declaration, trialArgs)
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs))
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            return new PropertyTrialResult(false, $"Property trial exceeded timeout ({timeoutMs}ms).");
        }
    }

    private static async Task<PropertyTrialResult> ExecutePropertyTrialInInterpreterAsync(
        Interpreter interpreter,
        PropertyDeclaration declaration,
        List<RuntimeValue> trialArgs)
    {
        try
        {
            var functionDeclaration = new FunctionDeclaration(
                declaration.Name,
                declaration.Parameters,
                declaration.Body,
                line: declaration.Line,
                column: declaration.Column);
            var function = new FunctionValue(functionDeclaration, interpreter._globals);
            var returnValue = await interpreter.CallFunctionAsync(function, trialArgs);

            if (returnValue.Type == ValueType.Boolean && !returnValue.AsBoolean())
                return new PropertyTrialResult(false, "Property returned false.");

            return new PropertyTrialResult(true, null);
        }
        catch (RuntimeException ex)
        {
            return new PropertyTrialResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            return new PropertyTrialResult(false, ex.Message);
        }
    }

    private static PropertyTrialResult ExecutePropertyTrialInTranspiledWithTimeout(
        MethodInfo invokeMethod,
        string propertyName,
        List<RuntimeValue> trialArgs,
        int timeoutMs)
    {
        try
        {
            return ExecutePropertyTrialInTranspiledAsync(invokeMethod, propertyName, trialArgs)
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs))
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            return new PropertyTrialResult(false, $"Property trial exceeded timeout ({timeoutMs}ms).");
        }
    }

    private static async Task<PropertyTrialResult> ExecutePropertyTrialInTranspiledAsync(
        MethodInfo invokeMethod,
        string propertyName,
        List<RuntimeValue> trialArgs)
    {
        try
        {
            var plainArgs = trialArgs.Select(ToPlainObject).ToArray();
            var taskObj = invokeMethod.Invoke(null, new object[] { propertyName, plainArgs });
            if (taskObj is not Task<object> task)
                return new PropertyTrialResult(false, "Transpiled property invocation did not return Task<object>.");

            var result = await task;
            if (result is bool b && !b)
                return new PropertyTrialResult(false, "Property returned false.");
            if (result is RuntimeValue rv && rv.Type == ValueType.Boolean && !rv.AsBoolean())
                return new PropertyTrialResult(false, "Property returned false.");
            return new PropertyTrialResult(true, null);
        }
        catch (TargetInvocationException ex)
        {
            return new PropertyTrialResult(false, ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            return new PropertyTrialResult(false, ex.Message);
        }
    }

    private static PropertyGenerator CreatePropertyGeneratorForParameter(string parameterName)
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

    private static string FormatPropertyArguments(IReadOnlyList<RuntimeValue> args)
    {
        var formatted = args.Select(arg =>
        {
            if (arg.Type == ValueType.String)
                return "\"" + arg.AsString().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
            return arg.ToString();
        });
        return "[" + string.Join(", ", formatted) + "]";
    }

    private static object? ToPlainObject(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Integer => value.AsInteger(),
            ValueType.Float => value.AsFloat(),
            ValueType.Boolean => value.AsBoolean(),
            ValueType.String => value.AsString(),
            ValueType.Null => null,
            ValueType.Array => value.AsArray().Select(ToPlainObject).ToList(),
            _ => value.ToString()
        };
    }

    private static RuntimeValue BuildRunPropertyResult(
        string propertyName,
        bool passed,
        int iterations,
        int seed,
        int? failedTrial,
        string? error,
        string? counterexample,
        string? shrunkCounterexample)
    {
        var result = new DictionaryInstance();
        result.SetEntry("propertyName", RuntimeValue.String(propertyName));
        result.SetEntry("passed", RuntimeValue.Boolean(passed));
        result.SetEntry("iterations", RuntimeValue.Integer(iterations));
        result.SetEntry("seed", RuntimeValue.Integer(seed));
        result.SetEntry("failedTrial", failedTrial.HasValue ? RuntimeValue.Integer(failedTrial.Value) : RuntimeValue.Null());
        result.SetEntry("error", error != null ? RuntimeValue.String(error) : RuntimeValue.Null());
        result.SetEntry("counterexample", counterexample != null ? RuntimeValue.String(counterexample) : RuntimeValue.Null());
        result.SetEntry("shrunkCounterexample", shrunkCounterexample != null ? RuntimeValue.String(shrunkCounterexample) : RuntimeValue.Null());
        return RuntimeValue.Object(result);
    }

    private sealed class PropertyTrialResult
    {
        public bool Passed { get; }
        public string? ErrorMessage { get; }

        public PropertyTrialResult(bool passed, string? errorMessage)
        {
            Passed = passed;
            ErrorMessage = errorMessage;
        }
    }
}
