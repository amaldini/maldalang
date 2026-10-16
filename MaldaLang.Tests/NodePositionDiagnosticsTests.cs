// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using Xunit;
using InterpreterRuntime = MaldaLang.Interpreter.Interpreter;
using ParserRuntime = MaldaLang.Parser.Parser;

namespace MaldaLang.Tests;

/// <summary>
/// Member access, indexing and interpolation holes used to be built without a source position,
/// so runtime errors and diagnostics on them reported line 0. These pin the user-visible result.
/// </summary>
[Collection("Sequential")]
public class NodePositionDiagnosticsTests
{
    [Theory]
    [InlineData("var a = 1;\nvar o = {};\nio.print(o.missing.x);\n", 3)]
    [InlineData("var a = 1;\nvar arr = [1];\nio.print(arr[5]);\n", 3)]
    // The failing access sits on a later line than the statement that contains it.
    [InlineData("var o = {};\nio.print(\n  o.missing.x);\n", 3)]
    [InlineData("var arr = [1];\nio.print(\n  arr[5]);\n", 3)]
    public async Task RuntimeError_OnMemberAccessOrIndex_ReportsItsLine(string source, int expectedLine)
    {
        const string path = "main.malda";
        var statements = new ParserRuntime(new Lexer(source, path).Tokenize(), path).Parse();
        var interpreter = new InterpreterRuntime(currentFile: path);
        interpreter.SetSourceCode(source);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => interpreter.InterpretAsync(statements));

        var info = RuntimeDiagnostics.CreateDiagnosticInfo(ex, interpreter);
        Assert.Equal(expectedLine, info.Line);
    }

    [Fact]
    public void WF1002_OnNamespacedCall_PointsAtTheMemberName()
    {
        var source = """
            workflow Bad(input) {
                var a = 1;
                io.ensureDir("x");
            }
            """;
        var statements = new ParserRuntime(new Lexer(source).Tokenize()).Parse();
        var diagnostics = new List<Diagnostic>();
        WorkflowDeterminismDiagnostics.Validate(statements, diagnostics);

        var wf = Assert.Single(diagnostics, d => d.Source == "WF1002");
        Assert.Contains("ensureDir", wf.Message, StringComparison.Ordinal);
        Assert.Equal(2, wf.Line);       // 0-based: line 3
        Assert.Equal(7, wf.Column);     // 0-based: "ensureDir" starts at column 8
        Assert.Equal("ensureDir".Length, wf.Length);
    }

    [Fact]
    public void InterpolationHole_NodesCarryTheirFilePosition()
    {
        const string source = "var x = 1;\nvar s = $\"a {x.y} b\";\n";
        var statements = new ParserRuntime(new Lexer(source, "main.malda").Tokenize(), "main.malda").Parse();

        var decl = Assert.IsType<VarDeclStatement>(statements[1]);
        var interpolated = Assert.IsType<InterpolatedStringExpression>(decl.Initializer);
        var member = Assert.IsType<MemberAccessExpression>(interpolated.Segments.Single(s => s.IsExpression).Expression);
        Assert.Equal(2, member.Line);
        Assert.Equal(16, member.Column); // the "y" in {x.y}
        Assert.Equal("main.malda", member.SourceFile);
        Assert.Equal(14, member.Object.Column); // the "x"
    }

    [Fact]
    public void MultiLineString_TokenIsPositionedWhereItStarts()
    {
        var tokens = new Lexer("var s = \"one\ntwo\";\n").Tokenize();
        var str = tokens.Single(t => t.Type == TokenType.String);
        Assert.Equal(1, str.Line);
        Assert.Equal(9, str.Column);
    }
}
