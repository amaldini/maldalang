// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class AgentGovernanceGoldenTests : TestBase
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string GoldenPath =>
        Path.Combine(RepoRoot, "Examples", "Agents", "agent_governance_golden.malda");

    [Fact]
    public void Golden_RunsOffline_ValidateAndPurePath()
    {
        var source = File.ReadAllText(GoldenPath);
        var output = RunProgram(source);
        Assert.Contains("summary=SHIP GOVERNANCE DEFAULTS", output, StringComparison.Ordinal);
        Assert.Contains("steps=validate | pure helpers | effects", output, StringComparison.Ordinal);
        Assert.Contains("invalid:", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Golden_StrictAnalyze_NoPureOrEffectsErrors()
    {
        var source = File.ReadAllText(GoldenPath);
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Enabled, diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-pure");
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-effects");
    }
}
