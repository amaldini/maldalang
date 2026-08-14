using System.Diagnostics;
using System.Runtime.InteropServices;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests.Spec;

/// <summary>
/// Phase 2.4: Parser/Lexer edits must update spec, CHANGELOG, or grammar (scripts/verify-spec-parser-drift.ps1).
/// </summary>
public class SpecParserDriftGuardTests
{
    private static string RepoRoot => PlanningPaths.RepoRoot;

    private static string ScriptPath =>
        PlanningPaths.ResolveRepoPath("scripts", "verify-spec-parser-drift.ps1");

    [Fact]
    public void ParserDriftGuard_CompanionDocumentationFilesExist()
    {
        Assert.True(File.Exists(PlanningPaths.ResolveRepoPath("docs", "spec", "malda-language-1.0.md")));
        Assert.True(File.Exists(PlanningPaths.ResolveRepoPath("docs", "spec", "CHANGELOG.md")));
        Assert.True(File.Exists(PlanningPaths.ResolveRepoPath("ReferenceManual", "34-grammar.html")));
        Assert.True(File.Exists(ScriptPath));
    }

    [Fact]
    public void ParserDriftGuard_ScriptPassesForCurrentBranch()
    {
        var shell = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell" : "pwsh";
        var psi = new ProcessStartInfo
        {
            FileName = shell,
            ArgumentList =
            {
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", ScriptPath,
                "-RepoRoot", RepoRoot,
            },
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(TimeSpan.FromMinutes(2));

        Assert.True(process.ExitCode == 0,
            $"verify-spec-parser-drift.ps1 failed (exit {process.ExitCode}).\nstdout:\n{stdout}\nstderr:\n{stderr}");
        Assert.Contains("OK:", stdout, StringComparison.Ordinal);
    }
}
