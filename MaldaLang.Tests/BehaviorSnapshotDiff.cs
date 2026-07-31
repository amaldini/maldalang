using System.Text;

namespace MaldaLang.Tests;

public sealed class BehaviorSnapshotDiff
{
    public bool AreEqual => Differences.Count == 0;
    public IReadOnlyList<string> Differences { get; }

    private BehaviorSnapshotDiff(IReadOnlyList<string> differences)
    {
        Differences = differences;
    }

    public static BehaviorSnapshotDiff Compare(BehaviorSnapshot expected, BehaviorSnapshot actual)
    {
        var differences = new List<string>();

        if (!string.Equals(expected.PropertyName, actual.PropertyName, StringComparison.Ordinal))
            differences.Add($"PropertyName differs: expected '{expected.PropertyName}', actual '{actual.PropertyName}'.");
        if (expected.Status != actual.Status)
            differences.Add($"Status differs: expected '{expected.Status}', actual '{actual.Status}'.");
        if (expected.Seed != actual.Seed)
            differences.Add($"Seed differs: expected '{expected.Seed}', actual '{actual.Seed}'.");
        if (expected.Iterations != actual.Iterations)
            differences.Add($"Iterations differs: expected '{expected.Iterations}', actual '{actual.Iterations}'.");
        if (expected.FailedTrial != actual.FailedTrial)
            differences.Add($"FailedTrial differs: expected '{expected.FailedTrial}', actual '{actual.FailedTrial}'.");
        if (!string.Equals(expected.ErrorMessage ?? "", actual.ErrorMessage ?? "", StringComparison.Ordinal))
            differences.Add($"ErrorMessage differs: expected '{expected.ErrorMessage}', actual '{actual.ErrorMessage}'.");
        if (!string.Equals(expected.Counterexample ?? "", actual.Counterexample ?? "", StringComparison.Ordinal))
            differences.Add($"Counterexample differs: expected '{expected.Counterexample}', actual '{actual.Counterexample}'.");
        if (!string.Equals(expected.ShrunkCounterexample ?? "", actual.ShrunkCounterexample ?? "", StringComparison.Ordinal))
            differences.Add($"ShrunkCounterexample differs: expected '{expected.ShrunkCounterexample}', actual '{actual.ShrunkCounterexample}'.");
        if (!string.Equals(expected.SkipReason ?? "", actual.SkipReason ?? "", StringComparison.Ordinal))
            differences.Add($"SkipReason differs: expected '{expected.SkipReason}', actual '{actual.SkipReason}'.");

        var expectedStdOut = NormalizeText(expected.StdOut);
        var actualStdOut = NormalizeText(actual.StdOut);
        if (!string.Equals(expectedStdOut, actualStdOut, StringComparison.Ordinal))
            differences.Add($"StdOut differs: expected '{expectedStdOut}', actual '{actualStdOut}'.");

        var expectedStdErr = NormalizeText(expected.StdErr);
        var actualStdErr = NormalizeText(actual.StdErr);
        if (!string.Equals(expectedStdErr, actualStdErr, StringComparison.Ordinal))
            differences.Add($"StdErr differs: expected '{expectedStdErr}', actual '{actualStdErr}'.");

        if (expected.ExitCode != actual.ExitCode)
            differences.Add($"ExitCode differs: expected '{expected.ExitCode}', actual '{actual.ExitCode}'.");

        return new BehaviorSnapshotDiff(differences);
    }

    public static string NormalizeText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }

        return string.Join("\n", lines).Trim();
    }

    public string ToDiagnosticReport(string propertyName, int seed, int iterations, string interpreterMode, string transpiledMode)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Behavior divergence for property '{propertyName}'.");
        builder.AppendLine($"Seed: {seed}");
        builder.AppendLine($"Iterations: {iterations}");
        builder.AppendLine($"Modes: {interpreterMode} vs {transpiledMode}");
        builder.AppendLine("To reproduce: rerun the same test with identical seed/iterations.");

        foreach (var difference in Differences)
        {
            builder.AppendLine("- " + difference);
        }

        return builder.ToString();
    }
}
