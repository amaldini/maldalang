// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class SumTypeNamespaceTests : TestBase
{
    [Fact]
    public void QualifiedConstructor_BuildsSameTagAsBare()
    {
        var output = RunProgram("""
            type Result = Ok(value) | Err(message);
            var a = Result.Ok(42);
            var b = Ok(7);
            match a {
                case Ok(v): print(v);
                case Err(m): print(m);
            }
            match b {
                case Ok(v): print(v);
                case Err(m): print(m);
            }
            """);
        Assert.Contains("42", output);
        Assert.Contains("7", output);
    }

    [Fact]
    public void QualifiedConstructor_DisambiguatesSharedTags()
    {
        var output = RunProgram("""
            type r = Ok(msg) | Err(msg);
            type r2 = Ok(msg) | Warning(msg);
            var x = r.Ok("Ciao");
            var y = r2.Ok("Hey");
            print(match x { case Ok(m): m; case Err(m): m; });
            print(match y { case Ok(m): m; case Warning(m): m; });
            """);
        var lines = output.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Ciao", lines[0]);
        Assert.Equal("Hey", lines[1]);
    }

    [Fact]
    public void GetCompletions_AfterTypeDot_OffersConstructors()
    {
        var service = new LanguageService();
        var source = """
            type r = Ok(msg) | Err(msg);
            var x = r.
            """;
        var completions = service.GetCompletions(source, 1, source.Split('\n')[1].Length);
        Assert.Contains(completions, c => c.Label == "Ok");
        Assert.Contains(completions, c => c.Label == "Err");
    }

    [Fact]
    public void GetDiagnostics_QualifiedConstructorAssignedToSumType_NoError()
    {
        var service = new LanguageService();
        var source = """
            type Result = Ok(n: int) | Err(message);
            var r: Result = Result.Ok(1);
            """;
        var diagnostics = service.GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Source == "malda-types" &&
            d.Message.Contains("does not match value", StringComparison.Ordinal));
    }
}
