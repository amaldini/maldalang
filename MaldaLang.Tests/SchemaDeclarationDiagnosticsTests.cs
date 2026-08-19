// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser;
using Xunit;

namespace MaldaLang.Tests;

public class SchemaDeclarationDiagnosticsTests
{
    [Fact]
    public void UnknownSchemaFieldType_Default_IsError()
    {
        var source = """
            schema Broken {
                x: NotAType;
            }
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Default, diagnostics);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-schema" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("NotAType", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedSchemaField_Known_NoDiagnostic()
    {
        var source = """
            schema Address {
                city: string;
            }
            schema Person {
                address: Address;
                tags: Address[];
            }
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Default, diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-schema");
    }

    [Fact]
    public void SumTypeField_Known_NoDiagnostic()
    {
        var source = """
            type Intent = Search(query) | Buy(sku, qty);
            schema Order {
                intent: Intent;
                extras: Intent[];
            }
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Default, diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-schema");
    }

    [Fact]
    public void UnknownConstructorPayloadType_IsError()
    {
        var source = """
            type Intent = Buy(sku: NotAType);
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Default, diagnostics);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-schema" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("NotAType", StringComparison.Ordinal) &&
            d.Message.Contains("constructor payload type", StringComparison.Ordinal));
    }

    [Fact]
    public void TypedConstructorPayload_Known_NoDiagnostic()
    {
        var source = """
            schema Address {
                city: string;
            }
            type Intent = Search(query: string) | Visit(addr: Address) | Help();
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Default, diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-schema");
    }

    [Fact]
    public void UnknownApiParameterType_IsError()
    {
        var source = """
            api Calc { function add(a: NotAType, b: number); }
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Default, diagnostics);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-schema" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("NotAType", StringComparison.Ordinal) &&
            d.Message.Contains("api parameter type", StringComparison.Ordinal));
    }

    [Fact]
    public void TypedApiParameter_Known_NoDiagnostic()
    {
        var source = """
            api Calc { function add(a: number, b: int); }
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Default, diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-schema");
    }
}
