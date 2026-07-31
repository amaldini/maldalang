namespace MaldaLang.Testing;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal sealed class RegressionArtifactWriter
{
    internal readonly struct RegressionHint
    {
        public bool CanGenerate { get; init; }
        public string? RecommendedPath { get; init; }
        public string? RecommendedFileName { get; init; }
        public string? CanonicalCounterexamplePayload { get; init; }
    }

    public IReadOnlyList<string> WriteArtifacts(
        IReadOnlyList<TestExecutionResult> results,
        string rootPath,
        string? regressionDirectory)
    {
        var failedProperties = results
            .Where(r => r.IsProperty && !r.Passed)
            .OrderBy(r => NormalizePath(r.Path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (failedProperties.Count == 0)
        {
            return Array.Empty<string>();
        }

        var targetDirectory = ResolveTargetDirectory(rootPath, regressionDirectory);
        Directory.CreateDirectory(targetDirectory);

        var generated = new List<string>(failedProperties.Count);
        foreach (var failedProperty in failedProperties)
        {
            var path = WriteSingleArtifact(targetDirectory, failedProperty);
            generated.Add(path);
        }

        return generated;
    }

    public RegressionHint BuildRegressionHint(
        TestExecutionResult result,
        string rootPath,
        string? regressionDirectory)
    {
        if (!result.IsProperty || result.Passed)
        {
            return new RegressionHint
            {
                CanGenerate = false
            };
        }

        var targetDirectory = ResolveTargetDirectory(rootPath, regressionDirectory);
        var plan = BuildArtifactPlan(targetDirectory, result);
        return new RegressionHint
        {
            CanGenerate = true,
            RecommendedPath = plan.RecommendedPath,
            RecommendedFileName = Path.GetFileName(plan.RecommendedPath),
            CanonicalCounterexamplePayload = plan.CanonicalCounterexample
        };
    }

    private static string WriteSingleArtifact(string targetDirectory, TestExecutionResult failedProperty)
    {
        var plan = BuildArtifactPlan(targetDirectory, failedProperty);
        var content = BuildContent(
            plan.SourcePath,
            plan.PropertyName,
            plan.Seed,
            plan.Iterations,
            plan.FailedTrial,
            failedProperty.PropertyCounterexample,
            failedProperty.PropertyShrunkCounterexample,
            plan.CanonicalCounterexample);
        var filePath = ResolveCollisionSafePath(targetDirectory, plan.BaseName, content);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private static ArtifactPlan BuildArtifactPlan(string targetDirectory, TestExecutionResult failedProperty)
    {
        var sourcePath = ExtractSourcePath(failedProperty.Path);
        var propertyName = string.IsNullOrWhiteSpace(failedProperty.PropertyName)
            ? "property"
            : failedProperty.PropertyName!;
        var seed = failedProperty.PropertySeed ?? 0;
        var iterations = failedProperty.PropertyIterations ?? 0;
        var failedTrial = failedProperty.PropertyFailedTrial ?? 0;
        var canonicalCounterexample = SelectCanonicalCounterexample(failedProperty);

        var sourceSlug = Slugify(GetSourceStem(sourcePath));
        var propertySlug = Slugify(propertyName);
        var stableHash = ComputeHash($"{sourcePath}|{propertyName}|{seed}|{iterations}|{failedTrial}|{canonicalCounterexample}");
        var baseName = $"{sourceSlug}-{propertySlug}-seed{seed}-trial{failedTrial}-{stableHash}";
        var recommendedPath = Path.Combine(targetDirectory, $"{baseName}.spec.malda");

        return new ArtifactPlan
        {
            SourcePath = sourcePath,
            PropertyName = propertyName,
            Seed = seed,
            Iterations = iterations,
            FailedTrial = failedTrial,
            CanonicalCounterexample = canonicalCounterexample,
            BaseName = baseName,
            RecommendedPath = recommendedPath
        };
    }

    private static string ResolveTargetDirectory(string rootPath, string? regressionDirectory)
    {
        if (!string.IsNullOrWhiteSpace(regressionDirectory))
        {
            return Path.GetFullPath(regressionDirectory);
        }

        var rootFullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(rootPath) ? Directory.GetCurrentDirectory() : rootPath);
        var baseDirectory = File.Exists(rootFullPath)
            ? Path.GetDirectoryName(rootFullPath) ?? Directory.GetCurrentDirectory()
            : rootFullPath;
        return Path.Combine(baseDirectory, "tests", "regressions");
    }

    private static string ResolveCollisionSafePath(string targetDirectory, string baseName, string content)
    {
        var attempt = 0;
        while (true)
        {
            var suffix = attempt == 0 ? string.Empty : $"-{attempt.ToString(CultureInfo.InvariantCulture)}";
            var candidate = Path.Combine(targetDirectory, $"{baseName}{suffix}.spec.malda");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            var existing = File.ReadAllText(candidate);
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return candidate;
            }

            attempt++;
        }
    }

    private static string BuildContent(
        string sourcePath,
        string propertyName,
        int seed,
        int iterations,
        int failedTrial,
        string? originalCounterexample,
        string? shrunkCounterexample,
        string canonicalCounterexample)
    {
        var sourceNormalized = NormalizePath(sourcePath);
        var original = string.IsNullOrWhiteSpace(originalCounterexample) ? "[]" : originalCounterexample!;
        var shrunk = string.IsNullOrWhiteSpace(shrunkCounterexample) ? canonicalCounterexample : shrunkCounterexample!;

        return string.Join(
            "\n",
            [
                "// Auto-generated by `malda test --write-regression`",
                $"// Source: {sourceNormalized}",
                $"// Property: {propertyName}",
                $"// Seed: {seed}",
                $"// Iterations: {iterations}",
                $"// FailedTrial: {failedTrial}",
                $"// Counterexample: {original}",
                $"// ShrunkCounterexample: {shrunk}",
                string.Empty,
                $"var regressionSource = \"{EscapeString(sourceNormalized)}\";",
                $"var regressionProperty = \"{EscapeString(propertyName)}\";",
                $"var regressionSeed = {seed};",
                $"var regressionIterations = {iterations};",
                $"var regressionFailedTrial = {failedTrial};",
                $"var regressionArgs = {canonicalCounterexample};",
                "print(\"Regression artifact loaded:\");",
                "print(regressionSource + \"::\" + regressionProperty);",
                "print(regressionArgs);",
                string.Empty
            ]);
    }

    private static string SelectCanonicalCounterexample(TestExecutionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.PropertyShrunkCounterexample))
        {
            return result.PropertyShrunkCounterexample!;
        }

        if (!string.IsNullOrWhiteSpace(result.PropertyCounterexample))
        {
            return result.PropertyCounterexample!;
        }

        return "[]";
    }

    private static string ExtractSourcePath(string testExecutionPath)
    {
        var markerIndex = testExecutionPath.IndexOf("::", StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return testExecutionPath;
        }

        return testExecutionPath[..markerIndex];
    }

    private static string GetSourceStem(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
            stem.EndsWith(".spec", StringComparison.OrdinalIgnoreCase))
        {
            stem = Path.GetFileNameWithoutExtension(stem);
        }

        return string.IsNullOrWhiteSpace(stem) ? "test" : stem;
    }

    private static string Slugify(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "item";
        }

        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append('-');
            }
        }

        var collapsed = builder
            .ToString()
            .Trim('-');
        while (collapsed.Contains("--", StringComparison.Ordinal))
        {
            collapsed = collapsed.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(collapsed) ? "item" : collapsed;
    }

    private static string ComputeHash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var hex = Convert.ToHexString(bytes);
        return hex[..12].ToLowerInvariant();
    }

    private static string EscapeString(string raw)
    {
        return raw
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private sealed class ArtifactPlan
    {
        public string SourcePath { get; init; } = string.Empty;
        public string PropertyName { get; init; } = string.Empty;
        public int Seed { get; init; }
        public int Iterations { get; init; }
        public int FailedTrial { get; init; }
        public string CanonicalCounterexample { get; init; } = "[]";
        public string BaseName { get; init; } = string.Empty;
        public string RecommendedPath { get; init; } = string.Empty;
    }
}
