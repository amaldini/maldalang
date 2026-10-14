// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using MaldaLang.Tests.Conformance.Tier0;
using Xunit;

namespace MaldaLang.Tests.Fuzz;

/// <summary>
/// Differential fuzzing: one generated program, run on the interpreter (the reference), C#
/// transpile and JavaScript transpile, must print the same lines. The fixed seeds below are
/// a cheap regression net; set <c>MALDA_FUZZ_SEEDS</c> (e.g. <c>100-140</c> or <c>7,9</c>) to
/// explore more locally. A failure writes the program and all three outputs to a folder and
/// names the first differing case.
/// </summary>
public class BackendDifferentialFuzzTests
{
    private const int CasesPerProgram = 12;

    public static IEnumerable<object[]> Seeds()
    {
        var configured = System.Environment.GetEnvironmentVariable("MALDA_FUZZ_SEEDS");
        var seeds = string.IsNullOrWhiteSpace(configured) ? new[] { 1, 2, 3 } : ParseSeeds(configured);
        return seeds.Select(seed => new object[] { seed });
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task GeneratedProgram_PrintsTheSameOnEveryBackend(int seed)
    {
        var source = BackendFuzzProgramGenerator.Generate(seed, CasesPerProgram);

        var interpreted = await TestBase.CaptureInterpretOutcomeAsync(source);
        var reference = interpreted.ExitCode == 0
            ? Normalize(interpreted.StdOut)
            : $"<exit {interpreted.ExitCode}: {interpreted.Exception?.Message}>";

        string csharp;
        try
        {
            var compiled = TranspiledTestRunner.CompileAndRunFromSource(source);
            csharp = compiled.ExitCode == 0
                ? Normalize(compiled.StdOut)
                : $"<exit {compiled.ExitCode}: {compiled.StdErr.Trim()}>";
        }
        catch (Exception ex)
        {
            csharp = $"<transpile failed: {ex.Message}>";
        }

        string? javaScript = null;
        if (Tier0JavaScriptRunner.IsAvailable(out _))
        {
            try
            {
                var js = new Compiler.Compiler().TranspileToJavaScriptFromSource(source);
                javaScript = Normalize(await Tier0JavaScriptRunner.RunCompiledJavaScriptAsync(js));
            }
            catch (Exception ex)
            {
                javaScript = $"<js failed: {ex.Message}>";
            }
        }

        var failures = new List<string>();
        if (interpreted.ExitCode != 0)
            failures.Add("interpreter: " + reference);
        if (csharp != reference)
            failures.Add("C#: " + FirstDifference(reference, csharp));
        // JavaScript has one number type, so it cannot print a float -0 that an int 0 must not
        // print (formatNumber in malda-js-runtime.js); compare it with -0 read as 0.
        if (javaScript != null && WithoutNegativeZero(javaScript) != WithoutNegativeZero(reference))
            failures.Add("JavaScript: " + FirstDifference(WithoutNegativeZero(reference), WithoutNegativeZero(javaScript)));

        if (failures.Count == 0)
            return;

        var folder = WriteRepro(seed, source, reference, csharp, javaScript);
        Assert.Fail(
            $"Seed {seed}: backends disagree. Program and outputs: {folder}{System.Environment.NewLine}"
            + string.Join(System.Environment.NewLine, failures));
    }

    private static string Normalize(string text) => text.Replace("\r", "").Trim();

    /// <summary>
    /// A -0 not inside a word or a decimal; no number prints as -0 followed by digits, so
    /// <c>-0240</c> is -0 concatenated with 240.
    /// </summary>
    private static string WithoutNegativeZero(string text) =>
        Regex.Replace(text, @"(?<![\w.])-0(?!\.)", "0");

    /// <summary>The first differing line, with the <c>#n</c> case it belongs to.</summary>
    private static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var currentCase = "(before #0)";
        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var e = i < expectedLines.Length ? expectedLines[i] : "<missing>";
            var a = i < actualLines.Length ? actualLines[i] : "<missing>";
            if (e.StartsWith('#'))
                currentCase = "case" + e[1..];
            if (e != a)
                return $"line {i + 1} in {currentCase}: interpreter '{e}' vs '{a}'";
        }

        return "outputs differ";
    }

    private static string WriteRepro(int seed, string source, string reference, string csharp, string? javaScript)
    {
        var folder = Path.Combine(Path.GetTempPath(), "malda_fuzz", $"seed{seed}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "program.malda"), source, Encoding.UTF8);
        File.WriteAllText(Path.Combine(folder, "interpreter.txt"), reference, Encoding.UTF8);
        File.WriteAllText(Path.Combine(folder, "csharp.txt"), csharp, Encoding.UTF8);
        if (javaScript != null)
            File.WriteAllText(Path.Combine(folder, "javascript.txt"), javaScript, Encoding.UTF8);
        return folder;
    }

    private static IEnumerable<int> ParseSeeds(string text)
    {
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-', 1);
            if (dash > 0)
            {
                var from = int.Parse(part[..dash]);
                var to = int.Parse(part[(dash + 1)..]);
                for (var seed = from; seed <= to; seed++)
                    yield return seed;
            }
            else
            {
                yield return int.Parse(part);
            }
        }
    }
}
