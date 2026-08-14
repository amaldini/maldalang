// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

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
}
