// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// DT7 curated interpret vs C# transpile pairs (same stdout, exit 0).
/// Compile-only smoke stays in <see cref="TranspileSmokeTests"/>.
/// v1 n/a (smoke only): LLM-awaiting prompts, agent_governance_golden,
/// workflow/job Examples (see WorkflowTranspilerParityTests), grounded_ask
/// (GraphMemory score drift), capability_tokens (cwd file I/O).
/// </summary>
public class InterpretTranspilePairTests
{
    [Theory]
    [InlineData("Examples/Basics/first_look.malda")]
    [InlineData("Examples/Basics/schema_validate.malda")]
    [InlineData("Examples/Basics/schema_sumtype_validate.malda")]
    [InlineData("Examples/Agents/phase6_pure_validate.malda")]
    [InlineData("Examples/Prompts/api_program_calc.malda")]
    [InlineData("Examples/Prompts/prompt_budget.malda")]
    [InlineData("Examples/Modules/selective_import.malda")]
    [InlineData("Examples/Modules/export_type_schema.malda")]
    public void Example_InterpretAndTranspile_SameStdout(string relativePath)
    {
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sourcePath = PlanningPaths.ResolveRepoFile(parts);
        InterpretTranspilePair.AssertSameFromFile(sourcePath, relativePath);
    }

    [Fact]
    public void Interpolation_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var n = 3;
            io.print($"n is {n}");
            io.print("n is " + string(n));
            """,
            "interpolation");
    }

    [Fact]
    public void ValidateSumTypeReturnsDict_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            type Intent = Search(query) | Buy(sku, qty);
            var tagged = dict { "tag": "Buy", "sku": "SKU-9", "qty": 2 };
            var check = validate("Intent", tagged);
            if (check.ok) {
                io.print(check.data.tag);
            } else {
                io.print("fail");
            }
            """,
            "validate-sum-type-dict");
    }

    [Fact]
    public void IntegerSinkRepeat_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var n = 5;
            io.print(str.repeat("-", int(n / 2)));
            """,
            "integer-sink-repeat");
    }
}
