// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class TypeHintImportTests
{
    [Fact]
    public void StrictTypes_ImportedFunctionReturn_IsChecked()
    {
        var dir = Path.Combine(Path.GetTempPath(), "malda-typehint-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var libPath = Path.Combine(dir, "lib.malda");
            File.WriteAllText(libPath, """
                export function make() -> string {
                    return "x";
                }
                """);

            var mainPath = Path.Combine(dir, "main.malda");
            var mainSource = """
                import "./lib.malda";
                var n: int = make();
                """;
            File.WriteAllText(mainPath, mainSource);

            var lexer = new Lexer(mainSource, mainPath);
            var parser = new Parser.Parser(lexer.Tokenize(), mainPath);
            var statements = parser.Parse();
            Assert.Empty(parser.Errors);

            var diagnostics = new List<Diagnostic>();
            StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Enabled, diagnostics, mainPath);
            Assert.Contains(diagnostics, d =>
                d.Source == "malda-types" &&
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("variable 'n'", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void StrictTypes_ImportedSchemaName_IsKnownHint()
    {
        var dir = Path.Combine(Path.GetTempPath(), "malda-typehint-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var libPath = Path.Combine(dir, "types.malda");
            File.WriteAllText(libPath, """
                schema Contact {
                    name: string;
                }
                """);

            var mainPath = Path.Combine(dir, "main.malda");
            var mainSource = """
                import "./types.malda";
                var c: Contact = 1;
                """;
            File.WriteAllText(mainPath, mainSource);

            var lexer = new Lexer(mainSource, mainPath);
            var parser = new Parser.Parser(lexer.Tokenize(), mainPath);
            var statements = parser.Parse();
            Assert.Empty(parser.Errors);

            var diagnostics = new List<Diagnostic>();
            StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Enabled, diagnostics, mainPath);
            Assert.DoesNotContain(diagnostics, d =>
                d.Message.Contains("Unknown type hint", StringComparison.Ordinal));
            Assert.Contains(diagnostics, d =>
                d.Source == "malda-types" &&
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("does not match value", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
