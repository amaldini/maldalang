// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.IDE.Services;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class WorkflowDeterminismDiagnosticsTests
{
    [Fact]
    public void SleepOutsideStep_ReportsWF1001()
    {
        var source = """
            workflow Bad(input) {
                sleep(10);
            }
            """;
        var diagnostics = Collect(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "WF1001" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("sleep", StringComparison.Ordinal));
    }

    [Fact]
    public void NowOutsideStep_ReportsWF1001()
    {
        var source = """
            workflow Bad(input) {
                var t = now();
            }
            """;
        var diagnostics = Collect(source);
        Assert.Contains(diagnostics, d => d.Source == "WF1001" && d.Message.Contains("now", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteFileOutsideStep_ReportsWF1002()
    {
        var source = """
            workflow Bad(input) {
                writeFile("x.txt", "data");
            }
            """;
        var diagnostics = Collect(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "WF1002" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("writeFile", StringComparison.Ordinal));
    }

    [Fact]
    public void SleepInsideStepCallee_NoDiagnostic()
    {
        var source = """
            function pause() {
                sleep(5);
                return 1;
            }
            workflow Ok(input) {
                step x = pause();
            }
            """;
        var diagnostics = Collect(source);
        Assert.DoesNotContain(diagnostics, d => d.Source is "WF1001" or "WF1002");
    }

    [Fact]
    public void SleepDirectlyInStepCall_NoDiagnostic()
    {
        // Direct call as step expression is inside step boundary.
        var source = """
            workflow Ok(input) {
                step x = sleep(5);
            }
            """;
        var diagnostics = Collect(source);
        Assert.DoesNotContain(diagnostics, d => d.Source is "WF1001" or "WF1002");
    }

    [Fact]
    public void OnRejectSideEffect_NoDiagnostic()
    {
        var source = """
            function notify() { writeFile("n.txt", "x"); return 1; }
            workflow Ok(input) {
                approval gate = approval("mgr") onReject notify();
            }
            """;
        var diagnostics = Collect(source);
        Assert.DoesNotContain(diagnostics, d => d.Source == "WF1002");
    }

    [Fact]
    public void LanguageService_SurfacesWF1001()
    {
        var ls = new LanguageService();
        var diagnostics = ls.GetDiagnostics("""
            workflow Bad(input) {
                sleep(1);
            }
            """, "wf_sleep.malda");
        Assert.Contains(diagnostics, d => d.Source == "WF1001");
    }

    private static List<Diagnostic> Collect(string source)
    {
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var diagnostics = new List<Diagnostic>();
        WorkflowDeterminismDiagnostics.Validate(statements, diagnostics);
        return diagnostics;
    }
}
