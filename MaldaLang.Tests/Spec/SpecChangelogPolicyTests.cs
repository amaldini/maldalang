using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests.Spec;

/// <summary>
/// Phase 2.3: docs/spec/CHANGELOG.md semver and deprecation policy anchors.
/// </summary>
public class SpecChangelogPolicyTests
{
    private static string ChangelogText =>
        File.ReadAllText(PlanningPaths.ResolveRepoPath("docs", "spec", "CHANGELOG.md"));

    public static IEnumerable<object[]> RequiredPolicySections =>
        new[]
        {
            "semantic versioning",
            "MAJOR",
            "MINOR",
            "PATCH",
            "One-release deprecation",
            "Release N",
            "Release N+1",
            "[Unreleased]",
            "1.0.0-draft",
        }.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(RequiredPolicySections))]
    public void Changelog_DocumentsSemverPolicy(string anchor)
    {
        Assert.Contains(anchor, ChangelogText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Changelog_LinksLanguageSpec()
    {
        Assert.Contains("malda-language-1.0.md", ChangelogText, StringComparison.Ordinal);
    }
}
