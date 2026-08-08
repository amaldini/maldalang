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
}
