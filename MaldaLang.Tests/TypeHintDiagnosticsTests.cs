// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

public class TypeHintDiagnosticsTests
{
    [Fact]
    public void GetDiagnostics_UnknownTypeHint_EmitsMaldaTypesInformation()
    {
        var service = new LanguageService();
        var source = "function f(x: NotARealType) -> int { return x; }";
        var diagnostics = service.GetDiagnostics(source);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Message.Contains("NotARealType", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_KnownTypeHint_NoMaldaTypesDiagnostic()
    {
        var service = new LanguageService();
        var source = "function f(x: int) -> int { return x; }";
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-types");
    }

    [Fact]
    public void GetDiagnostics_DeclaredClassTypeHint_NoUnknownDiagnostic()
    {
        var service = new LanguageService();
        var source = """
            class Person {
                var name: string = "";
            }
            function f(p: Person) -> Person { return p; }
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Message.Contains("Unknown type hint", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_DeclaredSchemaTypeHint_NoUnknownDiagnostic()
    {
        var service = new LanguageService();
        var source = """
            schema Contact {
                name: string;
            }
            var c: Contact = null;
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Message.Contains("Unknown type hint", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_HostClassTypeHint_NoUnknownDiagnostic()
    {
        var service = new LanguageService();
        var source = "var server: RestServer = null;";
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Message.Contains("Unknown type hint", StringComparison.Ordinal));
    }
}
