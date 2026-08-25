// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class SchemaValidateExampleTests : TestBase
{
    [Fact]
    public void Basics_SchemaValidate_RunsUnderInterpreter()
    {
        var path = PlanningPaths.ResolveRepoFile("Examples", "Basics", "schema_validate.malda");
        var source = File.ReadAllText(path);
        var output = RunProgram(source);
        Assert.Contains("ok: Ada", output, StringComparison.Ordinal);
        Assert.Contains("expected failure", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Basics_SchemaNestedValidate_RunsUnderInterpreter()
    {
        var path = PlanningPaths.ResolveRepoFile("Examples", "Basics", "schema_nested_validate.malda");
        var source = File.ReadAllText(path);
        var output = RunProgram(source);
        Assert.Contains("ok: Ada", output, StringComparison.Ordinal);
        Assert.Contains("London", output, StringComparison.Ordinal);
        Assert.Contains("expected failure", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Basics_SchemaSumTypeValidate_RunsUnderInterpreter()
    {
        var path = PlanningPaths.ResolveRepoFile("Examples", "Basics", "schema_sumtype_validate.malda");
        var source = File.ReadAllText(path);
        var output = RunProgram(source);
        Assert.Contains("intent: Buy", output, StringComparison.Ordinal);
        Assert.Contains("order: shoes", output, StringComparison.Ordinal);
        Assert.Contains("expected failure", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Basics_AsVariant_RunsUnderInterpreter()
    {
        var path = PlanningPaths.ResolveRepoFile("Examples", "Basics", "as_variant.malda");
        var source = File.ReadAllText(path);
        var output = RunProgram(source);
        Assert.Contains("buy SKU-9 x 2", output, StringComparison.Ordinal);
        Assert.Contains("again SKU-1 x 1", output, StringComparison.Ordinal);
        Assert.Contains("help", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FewShot_AsVariant_RunsUnderInterpreter()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "llm", "few-shot", "25_as_variant.malda");
        var source = File.ReadAllText(path);
        var output = RunProgram(source);
        Assert.Contains("buy SKU-9 x 2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Basics_SumTypeTypedPayloads_RunsUnderInterpreter()
    {
        var path = PlanningPaths.ResolveRepoFile("Examples", "Basics", "sumtype_typed_payloads.malda");
        var source = File.ReadAllText(path);
        var output = RunProgram(source);
        var lines = output.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("false", lines[1].Trim());
        Assert.Equal("Milan", lines[2].Trim());
    }
}
