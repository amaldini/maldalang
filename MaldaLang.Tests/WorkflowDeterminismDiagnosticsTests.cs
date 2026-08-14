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

    [Fact]
    public void InFileHelperNow_ReportsWF1001()
    {
        var source = """
            function stamp() {
                return now();
            }
            workflow Bad(input) {
                stamp();
            }
            """;
        var diagnostics = Collect(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "WF1001" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("now", StringComparison.Ordinal) &&
            d.Message.Contains("stamp", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedHelpersNow_ReportsWF1001()
    {
        var source = """
            function inner() {
                return now();
            }
            function stamp() {
                return inner();
            }
            workflow Bad(input) {
                stamp();
            }
            """;
        var diagnostics = Collect(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "WF1001" &&
            d.Message.Contains("now", StringComparison.Ordinal));
    }

    [Fact]
    public void HelperWriteFile_ReportsWF1002()
    {
        var source = """
            function persist(path) {
                writeFile(path, "x");
            }
            workflow Bad(input) {
                persist("out.txt");
            }
            """;
        var diagnostics = Collect(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "WF1002" &&
            d.Message.Contains("writeFile", StringComparison.Ordinal) &&
            d.Message.Contains("persist", StringComparison.Ordinal));
    }

    [Fact]
    public void NamespacedRandomInHelper_ReportsWF1001()
    {
        var source = """
            function roll() {
                return math.random();
            }
            workflow Bad(input) {
                roll();
            }
            """;
        var diagnostics = Collect(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "WF1001" &&
            d.Message.Contains("random", StringComparison.Ordinal));
    }

    [Fact]
    public void HelperOnlyCalledFromStep_NoDiagnostic()
    {
        var source = """
            function stamp() {
                return now();
            }
            workflow Ok(input) {
                step t = stamp();
            }
            """;
        var diagnostics = Collect(source);
        Assert.DoesNotContain(diagnostics, d => d.Source is "WF1001" or "WF1002");
    }

    [Fact]
    public void RecursiveHelpers_DoNotHang()
    {
        var source = """
            function a() { b(); }
            function b() { a(); }
            workflow Ok(input) {
                a();
            }
            """;
        var diagnostics = Collect(source);
        Assert.DoesNotContain(diagnostics, d => d.Source is "WF1001" or "WF1002");
    }

    [Fact]
    public void UnknownCallee_ReportsWF1005InfoOnce()
    {
        var source = """
            workflow Bad(input) {
                mystery();
                mystery();
            }
            """;
        var diagnostics = Collect(source);
        var infos = diagnostics.Where(d => d.Source == "WF1005").ToList();
        Assert.Single(infos);
        Assert.Equal(DiagnosticSeverity.Info, infos[0].Severity);
        Assert.Contains("mystery", infos[0].Message, StringComparison.Ordinal);
        Assert.Contains("unknown", infos[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Source is "WF1001" or "WF1002");
    }

    [Fact]
    public void ImportedCallee_ReportsWF1005Info()
    {
        var source = """
            import { stamp } from "clock.malda";
            workflow Bad(input) {
                stamp();
            }
            """;
        var diagnostics = Collect(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "WF1005" &&
            d.Severity == DiagnosticSeverity.Info &&
            d.Message.Contains("stamp", StringComparison.Ordinal) &&
            d.Message.Contains("imported", StringComparison.Ordinal));
    }

    [Fact]
    public void PipeNow_ReportsWF1001()
    {
        var source = """
            workflow Bad(input) {
                var t = 1 |> now;
            }
            """;
        var diagnostics = Collect(source);
        Assert.Contains(diagnostics, d => d.Source == "WF1001" && d.Message.Contains("now", StringComparison.Ordinal));
    }

    [Fact]
    public void LanguageService_SurfacesHelperWF1001()
    {
        var ls = new LanguageService();
        var diagnostics = ls.GetDiagnostics("""
            function stamp() { return now(); }
            workflow Bad(input) {
                stamp();
            }
            """, "wf_helper.malda");
        Assert.Contains(diagnostics, d => d.Source == "WF1001");
    }

    [Fact]
    public void VariantConstructor_NotWF1005()
    {
        var source = """
            type Intent = Search(q) | Help();
            workflow Ok(input) {
                var x = Search("hi");
            }
            """;
        var diagnostics = Collect(source);
        Assert.DoesNotContain(diagnostics, d => d.Source == "WF1005");
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
