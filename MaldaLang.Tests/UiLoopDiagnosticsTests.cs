// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class UiLoopDiagnosticsTests : TestBase
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static List<Diagnostic> Analyze(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        var diagnostics = new List<Diagnostic>();
        UiLoopDiagnostics.Validate(statements, diagnostics);
        return diagnostics;
    }

    [Fact]
    public void DispatchThenRenderWithoutPull_IsUI1001()
    {
        var diagnostics = Analyze("""
            var sid = "s";
            var tree = ui.column({}, []);
            ui.mount(tree, sid);
            ui.dispatchEvent({"type": "click", "targetPath": "/", "payload": {}}, sid, 1);
            print(ui.render(tree, sid));
            """);
        Assert.Contains(diagnostics, d =>
            d.Source == "UI1001" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void DispatchPullThenRender_NoUI1001()
    {
        var diagnostics = Analyze("""
            var sid = "s";
            var tree = ui.column({}, []);
            ui.mount(tree, sid);
            ui.dispatchEvent({"type": "click", "targetPath": "/", "payload": {}}, sid, 1);
            var evt = ui.pullEvent(sid);
            print(ui.render(tree, sid));
            """);
        Assert.DoesNotContain(diagnostics, d => d.Source == "UI1001");
    }

    [Fact]
    public void MixedPageAndUiRender_IsUI1002Info()
    {
        var diagnostics = Analyze("""
            var sid = "s";
            print(ui.render(ui.text({"value": "x"}), sid));

            @PAGE("/")
            function home() {
                return "<html></html>";
            }
            """);
        Assert.Contains(diagnostics, d =>
            d.Source == "UI1002" && d.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public void EventLoopExample_NoUI1001()
    {
        var path = Path.Combine(RepoRoot, "Examples", "Web", "ui_event_loop.malda");
        Assert.True(File.Exists(path), "ui_event_loop.malda should exist");
        var diagnostics = Analyze(File.ReadAllText(path));
        Assert.DoesNotContain(diagnostics, d => d.Source == "UI1001");
    }
}
