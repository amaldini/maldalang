namespace MaldaLang.Interpreter;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

public sealed class PropertyRunOptions
{
    public const int DefaultIterations = 100;
    public const int DefaultSeed = 1337;
    public const int DefaultTrialTimeoutMs = 2000;
    public const int DefaultMaxShrinkAttempts = 256;
    public const int DefaultMaxShrinkPassesPerArgument = 64;

    public int Iterations { get; set; } = DefaultIterations;
    public int Seed { get; set; } = DefaultSeed;
    public int TrialTimeoutMs { get; set; } = DefaultTrialTimeoutMs;
    public int MaxShrinkAttempts { get; set; } = DefaultMaxShrinkAttempts;
    public int MaxShrinkPassesPerArgument { get; set; } = DefaultMaxShrinkPassesPerArgument;
}

public enum PropertyExecutionStatus
{
    Passed,
    Failed,
    Skipped
}

public sealed class PropertyRunResult
{
    public string PropertyName { get; }
    public PropertyExecutionStatus Status { get; }
    public bool Passed => Status == PropertyExecutionStatus.Passed;
    public int Seed { get; }
    public int Iterations { get; }
    public int? FailedTrial { get; }
    public string? ErrorMessage { get; }
    public string? Counterexample { get; }
    public string? ShrunkCounterexample { get; }
    public string? SkipReason { get; }

    public PropertyRunResult(
        string propertyName,
        bool passed,
        int seed,
        int iterations,
        int? failedTrial = null,
        string? errorMessage = null,
        string? counterexample = null,
        string? shrunkCounterexample = null,
        PropertyExecutionStatus? status = null,
        string? skipReason = null)
    {
        PropertyName = propertyName;
        Status = status ?? (passed ? PropertyExecutionStatus.Passed : PropertyExecutionStatus.Failed);
        Seed = seed;
        Iterations = iterations;
        FailedTrial = failedTrial;
        ErrorMessage = errorMessage;
        Counterexample = counterexample;
        ShrunkCounterexample = shrunkCounterexample;
        SkipReason = skipReason;
    }

    public static PropertyRunResult Skipped(
        string propertyName,
        int seed,
        int iterations,
        string reason)
    {
        return new PropertyRunResult(
            propertyName,
            passed: false,
            seed: seed,
            iterations: iterations,
            status: PropertyExecutionStatus.Skipped,
            skipReason: reason);
    }
}

public abstract class PropertyGenerator
{
    public abstract RuntimeValue Next(Random random);

    public virtual IEnumerable<RuntimeValue> Shrink(RuntimeValue value)
    {
        yield break;
    }
}

public static class PropertyGenerators
{
    public static PropertyGenerator Int(int min = -100, int max = 100) => new IntPropertyGenerator(min, max);
    public static PropertyGenerator Bool() => new BoolPropertyGenerator();
    public static PropertyGenerator String(int maxLength = 16) => new StringPropertyGenerator(maxLength);
    public static PropertyGenerator List(PropertyGenerator elementGenerator, int maxLength = 8) => new ListPropertyGenerator(elementGenerator, maxLength);
    public static PropertyGenerator OneOf(params PropertyGenerator[] generators) => new OneOfPropertyGenerator(generators);

    private sealed class IntPropertyGenerator : PropertyGenerator
    {
        private readonly int _min;
        private readonly int _max;

        public IntPropertyGenerator(int min, int max)
        {
            if (min > max)
            {
                throw new ArgumentException("min must be <= max.");
            }

            _min = min;
            _max = max;
        }

        public override RuntimeValue Next(Random random)
        {
            var value = random.Next(_min, _max + 1);
            return RuntimeValue.Integer(value);
        }

        public override IEnumerable<RuntimeValue> Shrink(RuntimeValue value)
        {
            if (value.Type != ValueType.Integer)
            {
                yield break;
            }

            var n = value.AsInteger();
            if (n == 0)
            {
                yield break;
            }

            yield return RuntimeValue.Integer(0);

            var half = n / 2;
            if (half != n)
            {
                yield return RuntimeValue.Integer(half);
            }

            if (n > 0)
            {
                yield return RuntimeValue.Integer(1);
            }
            else
            {
                yield return RuntimeValue.Integer(-1);
            }
        }
    }

    private sealed class BoolPropertyGenerator : PropertyGenerator
    {
        public override RuntimeValue Next(Random random)
        {
            return RuntimeValue.Boolean(random.Next(0, 2) == 1);
        }

        public override IEnumerable<RuntimeValue> Shrink(RuntimeValue value)
        {
            if (value.Type == ValueType.Boolean && value.AsBoolean())
            {
                yield return RuntimeValue.Boolean(false);
            }
        }
    }

    private sealed class StringPropertyGenerator : PropertyGenerator
    {
        private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private readonly int _maxLength;

        public StringPropertyGenerator(int maxLength)
        {
            _maxLength = Math.Max(0, maxLength);
        }

        public override RuntimeValue Next(Random random)
        {
            var length = _maxLength == 0 ? 0 : random.Next(0, _maxLength + 1);
            var builder = new StringBuilder(length);
            for (var i = 0; i < length; i++)
            {
                var idx = random.Next(0, Alphabet.Length);
                builder.Append(Alphabet[idx]);
            }

            return RuntimeValue.String(builder.ToString());
        }

        public override IEnumerable<RuntimeValue> Shrink(RuntimeValue value)
        {
            if (value.Type != ValueType.String)
            {
                yield break;
            }

            var s = value.AsString();
            if (s.Length == 0)
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (seen.Add(string.Empty))
            {
                yield return RuntimeValue.String(string.Empty);
            }

            // Minimize size first: shortest prefixes are attempted first.
            for (var targetLength = 1; targetLength < s.Length; targetLength++)
            {
                var candidate = s.Substring(0, targetLength);
                if (seen.Add(candidate))
                {
                    yield return RuntimeValue.String(candidate);
                }
            }

            // Then reduce value complexity at the current size.
            var normalized = new string('a', s.Length);
            if (!string.Equals(normalized, s, StringComparison.Ordinal) && seen.Add(normalized))
            {
                yield return RuntimeValue.String(normalized);
            }
        }
    }

    private sealed class ListPropertyGenerator : PropertyGenerator
    {
        private readonly PropertyGenerator _elementGenerator;
        private readonly int _maxLength;

        public ListPropertyGenerator(PropertyGenerator elementGenerator, int maxLength)
        {
            _elementGenerator = elementGenerator;
            _maxLength = Math.Max(0, maxLength);
        }

        public override RuntimeValue Next(Random random)
        {
            var length = _maxLength == 0 ? 0 : random.Next(0, _maxLength + 1);
            var items = new List<RuntimeValue>(length);
            for (var i = 0; i < length; i++)
            {
                items.Add(_elementGenerator.Next(random));
            }

            return RuntimeValue.Array(items);
        }

        public override IEnumerable<RuntimeValue> Shrink(RuntimeValue value)
        {
            if (value.Type != ValueType.Array)
            {
                yield break;
            }

            var items = value.AsArray();
            if (items.Count == 0)
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            // Minimize size first: shortest prefixes are attempted first.
            for (var targetLength = 0; targetLength < items.Count; targetLength++)
            {
                var prefix = items.Take(targetLength).ToList();
                var signature = BuildArraySignature(prefix);
                if (seen.Add(signature))
                {
                    yield return RuntimeValue.Array(prefix);
                }
            }

            // Then reduce element complexity while preserving list size.
            for (var i = 0; i < items.Count; i++)
            {
                foreach (var shrunk in _elementGenerator.Shrink(items[i]))
                {
                    var candidate = items.ToList();
                    candidate[i] = shrunk;
                    var signature = BuildArraySignature(candidate);
                    if (seen.Add(signature))
                    {
                        yield return RuntimeValue.Array(candidate);
                    }
                }
            }
        }

        private static string BuildArraySignature(IReadOnlyList<RuntimeValue> values)
        {
            return string.Join("\u001f", values.Select(BuildValueSignature));
        }

        private static string BuildValueSignature(RuntimeValue value)
        {
            return value.Type switch
            {
                ValueType.Integer => "i:" + value.AsInteger().ToString(CultureInfo.InvariantCulture),
                ValueType.Float => "f:" + value.AsFloat().ToString("R", CultureInfo.InvariantCulture),
                ValueType.Boolean => value.AsBoolean() ? "b:1" : "b:0",
                ValueType.String => "s:" + value.AsString(),
                ValueType.Null => "n",
                ValueType.Array => "a:[" + BuildArraySignature(value.AsArray()) + "]",
                _ => value.ToString()
            };
        }
    }

    private sealed class OneOfPropertyGenerator : PropertyGenerator
    {
        private readonly PropertyGenerator[] _generators;

        public OneOfPropertyGenerator(PropertyGenerator[] generators)
        {
            _generators = generators ?? Array.Empty<PropertyGenerator>();
        }

        public override RuntimeValue Next(Random random)
        {
            if (_generators.Length == 0)
            {
                return RuntimeValue.Null();
            }

            var idx = random.Next(0, _generators.Length);
            return _generators[idx].Next(random);
        }

        public override IEnumerable<RuntimeValue> Shrink(RuntimeValue value)
        {
            var candidates = new List<RuntimeValue>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var generator in _generators)
            {
                foreach (var shrunk in generator.Shrink(value))
                {
                    var signature = BuildValueSignature(shrunk);
                    if (seen.Add(signature))
                    {
                        candidates.Add(shrunk);
                    }
                }
            }

            foreach (var candidate in candidates
                .OrderBy(GetStructuralSize)
                .ThenBy(GetTypeRank)
                .ThenBy(BuildValueSignature, StringComparer.Ordinal))
            {
                yield return candidate;
            }
        }

        private static int GetStructuralSize(RuntimeValue value)
        {
            return value.Type switch
            {
                ValueType.Null => 0,
                ValueType.Boolean => 1,
                ValueType.Integer => 1,
                ValueType.Float => 1,
                ValueType.String => value.AsString().Length,
                ValueType.Array => value.AsArray().Count,
                _ => 2
            };
        }

        private static int GetTypeRank(RuntimeValue value)
        {
            return value.Type switch
            {
                ValueType.Null => 0,
                ValueType.Boolean => 1,
                ValueType.Integer => 2,
                ValueType.Float => 3,
                ValueType.String => 4,
                ValueType.Array => 5,
                _ => 6
            };
        }

        private static string BuildValueSignature(RuntimeValue value)
        {
            return value.Type switch
            {
                ValueType.Integer => "i:" + value.AsInteger().ToString(CultureInfo.InvariantCulture),
                ValueType.Float => "f:" + value.AsFloat().ToString("R", CultureInfo.InvariantCulture),
                ValueType.Boolean => value.AsBoolean() ? "b:1" : "b:0",
                ValueType.String => "s:" + value.AsString(),
                ValueType.Null => "n",
                ValueType.Array => "a:[" + string.Join("\u001f", value.AsArray().Select(BuildValueSignature)) + "]",
                _ => value.ToString()
            };
        }
    }
}

public sealed class PropertyRunner
{
    public PropertyRunResult RunProperty(
        IReadOnlyList<Statement> statements,
        PropertyDeclaration declaration,
        PropertyRunOptions? options = null)
    {
        var runOptions = options ?? new PropertyRunOptions();
        var propertySeed = runOptions.Seed;
        var random = new Random(propertySeed);
        var generators = declaration.Parameters.Select(CreateGeneratorForParameter).ToList();

        for (var trial = 1; trial <= runOptions.Iterations; trial++)
        {
            var args = generators.Select(g => g.Next(random)).ToList();
            var counterexample = FormatArguments(args);
            var outcome = ExecuteTrialWithTimeout(statements, declaration, args, runOptions.TrialTimeoutMs);
            if (outcome.Passed)
            {
                continue;
            }

            var shrunkArgs = ShrinkArguments(
                statements,
                declaration,
                generators,
                args,
                runOptions.TrialTimeoutMs,
                runOptions.MaxShrinkAttempts,
                runOptions.MaxShrinkPassesPerArgument);
            var shrunkCounterexample = FormatArguments(shrunkArgs);

            return new PropertyRunResult(
                declaration.Name,
                passed: false,
                seed: propertySeed,
                iterations: runOptions.Iterations,
                failedTrial: trial,
                errorMessage: outcome.ErrorMessage,
                counterexample: counterexample,
                shrunkCounterexample: shrunkCounterexample);
        }

        return new PropertyRunResult(
            declaration.Name,
            passed: true,
            seed: propertySeed,
            iterations: runOptions.Iterations);
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

    private static List<RuntimeValue> ShrinkArguments(
        IReadOnlyList<Statement> statements,
        PropertyDeclaration declaration,
        IReadOnlyList<PropertyGenerator> generators,
        List<RuntimeValue> originalArgs,
        int timeoutMs,
        int maxShrinkAttempts,
        int maxShrinkPassesPerArgument)
    {
        var current = originalArgs.ToList();
        var remainingAttempts = Math.Max(0, maxShrinkAttempts);
        var maxPasses = Math.Max(0, maxShrinkPassesPerArgument);

        if (remainingAttempts == 0 || maxPasses == 0)
        {
            return current;
        }

        for (var argIndex = 0; argIndex < current.Count; argIndex++)
        {
            var acceptedPasses = 0;
            var changed = true;
            while (changed && acceptedPasses < maxPasses && remainingAttempts > 0)
            {
                changed = false;
                foreach (var candidate in generators[argIndex].Shrink(current[argIndex]))
                {
                    if (remainingAttempts <= 0)
                    {
                        break;
                    }

                    if (RuntimeValueEquals(candidate, current[argIndex]))
                    {
                        continue;
                    }

                    var trialArgs = current.ToList();
                    trialArgs[argIndex] = candidate;
                    remainingAttempts--;
                    var outcome = ExecuteTrialWithTimeout(statements, declaration, trialArgs, timeoutMs);
                    if (!outcome.Passed)
                    {
                        current = trialArgs;
                        changed = true;
                        acceptedPasses++;
                        break;
                    }
                }
            }

            if (remainingAttempts <= 0)
            {
                break;
            }
        }

        return current;
    }

    private static bool RuntimeValueEquals(RuntimeValue left, RuntimeValue right)
    {
        if (left.Type != right.Type)
        {
            return false;
        }

        return left.Type switch
        {
            ValueType.Integer => left.AsInteger() == right.AsInteger(),
            ValueType.Float => Math.Abs(left.AsFloat() - right.AsFloat()) <= double.Epsilon,
            ValueType.Boolean => left.AsBoolean() == right.AsBoolean(),
            ValueType.String => string.Equals(left.AsString(), right.AsString(), StringComparison.Ordinal),
            ValueType.Null => true,
            ValueType.Array => ArraysEqual(left.AsArray(), right.AsArray()),
            _ => string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal)
        };
    }

    private static bool ArraysEqual(IReadOnlyList<RuntimeValue> left, IReadOnlyList<RuntimeValue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!RuntimeValueEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static PropertyTrialOutcome ExecuteTrialWithTimeout(
        IReadOnlyList<Statement> statements,
        PropertyDeclaration declaration,
        List<RuntimeValue> args,
        int timeoutMs)
    {
        try
        {
            var task = ExecuteTrialAsync(statements, declaration, args);
            return task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            return new PropertyTrialOutcome(
                passed: false,
                errorMessage: $"Property trial exceeded timeout ({timeoutMs}ms).");
        }
    }

    private static async System.Threading.Tasks.Task<PropertyTrialOutcome> ExecuteTrialAsync(
        IReadOnlyList<Statement> statements,
        PropertyDeclaration declaration,
        List<RuntimeValue> args)
    {
        try
        {
            var interpreter = new Interpreter();
            await interpreter.InterpretAsync(statements.ToList());

            var resolvedProperty = interpreter.GetProperty(declaration.Name);
            if (resolvedProperty == null)
            {
                return new PropertyTrialOutcome(
                    passed: false,
                    errorMessage: $"Property '{declaration.Name}' is not registered.");
            }

            var functionDeclaration = new FunctionDeclaration(
                resolvedProperty.Name,
                resolvedProperty.Parameters,
                resolvedProperty.Body,
                line: resolvedProperty.Line,
                column: resolvedProperty.Column);
            var function = new FunctionValue(functionDeclaration, interpreter._globals);
            var returnValue = await interpreter.CallFunctionAsync(function, args);

            if (returnValue.Type == ValueType.Boolean && !returnValue.AsBoolean())
            {
                return new PropertyTrialOutcome(
                    passed: false,
                    errorMessage: "Property returned false.");
            }

            return new PropertyTrialOutcome(passed: true);
        }
        catch (RuntimeException ex)
        {
            return new PropertyTrialOutcome(passed: false, errorMessage: ex.Message);
        }
        catch (Exception ex)
        {
            return new PropertyTrialOutcome(passed: false, errorMessage: ex.Message);
        }
    }

    private static string FormatArguments(IReadOnlyList<RuntimeValue> args)
    {
        return "[" + string.Join(", ", args.Select(FormatValue)) + "]";
    }

    private static string FormatValue(RuntimeValue value)
    {
        return value.Type switch
        {
            ValueType.Integer => value.AsInteger().ToString(CultureInfo.InvariantCulture),
            ValueType.Float => value.AsFloat().ToString("G", CultureInfo.InvariantCulture),
            ValueType.Boolean => value.AsBoolean() ? "true" : "false",
            ValueType.String => "\"" + value.AsString().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
            ValueType.Null => "null",
            ValueType.Array => "[" + string.Join(", ", value.AsArray().Select(FormatValue)) + "]",
            _ => value.ToString()
        };
    }

    private sealed class PropertyTrialOutcome
    {
        public bool Passed { get; }
        public string? ErrorMessage { get; }

        public PropertyTrialOutcome(bool passed, string? errorMessage = null)
        {
            Passed = passed;
            ErrorMessage = errorMessage;
        }
    }
}
