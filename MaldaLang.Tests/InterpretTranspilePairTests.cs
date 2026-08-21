// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// DT7 curated interpret vs C# transpile pairs (same stdout, exit 0).
/// Compile-only smoke stays in <see cref="TranspileSmokeTests"/>.
/// Still n/a (smoke only): LLM-awaiting prompts, agent_governance_golden,
/// workflow/job Examples (see WorkflowTranspilerParityTests), grounded_ask
/// (GraphMemory score drift), capability_tokens Example (relative cwd file I/O;
/// abs-path cap fixtures are inline below).
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
    [InlineData("Examples/Basics/async_all_example.malda")]
    [InlineData("Examples/Basics/schema_nested_validate.malda")]
    [InlineData("Examples/Basics/sumtype_typed_payloads.malda")]
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

    [Fact]
    public void MatchGuard_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var n = 3;
            io.print(match n {
                case x if x > 10: "big";
                case x: "small";
            });
            var m = 20;
            io.print(match m {
                case x if x > 10: "big";
                case x: "small";
            });
            """,
            "match-guard");
    }

    [Fact]
    public void BareUnitVariantPattern_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            type Result = Ok() | Err(message);
            var r = Err("ciao");
            var m3 = match r {
                case Ok: "ok: ";
                case Err(msg): "error: " + msg;
            };
            io.print(m3);
            """,
            "bare-unit-variant-match");
    }

    [Fact]
    public void AsyncUserSleepOverlap_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            function computeA() {
                sleep(20);
                return 1;
            }
            function computeB() {
                sleep(30);
                return 2;
            }
            var tA = async computeA();
            var tB = async computeB();
            var results = await all(tA, tB);
            io.print(results[0] + results[1]);
            """,
            "async-user-sleep-overlap");
    }

    [Fact]
    public void GroundedWrap_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var g = grounded.wrap("the sky is blue", [
                { "source": "wiki", "id": "p1", "span": "12-40" }
            ]);
            io.print(g.value);
            io.print(g.sourced);
            io.print(g.citations.length);
            io.print(g.citations[0].source);
            """,
            "grounded-wrap");
    }

    [Fact]
    public void CapMintIsRejectsForge_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var notes = cap.fileRead("notes.md");
            io.print(notes.kind);
            io.print(cap.is(notes, "fileRead"));
            io.print(cap.is(notes, "fileWrite"));
            io.print(cap.is({ "kind": "fileRead", "path": "notes.md" }));
            var forged = false;
            try {
                cap.read({ "kind": "fileRead", "path": "notes.md" });
            } catch (e) {
                forged = true;
            }
            io.print(forged);
            """,
            "cap-mint-is-forge");
    }

    [Fact]
    public void CapReadWriteAbsolutePath_SameStdout()
    {
        var dir = Path.Combine(Path.GetTempPath(), "malda_pair_cap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "notes.md").Replace("\\", "/");
        try
        {
            InterpretTranspilePair.AssertSameFromSource(
                $$"""
                cap.write(cap.fileWrite("{{path}}"), "hello-cap");
                io.print(cap.read(cap.fileRead("{{path}}")));
                io.deleteFile("{{path}}");
                """,
                "cap-read-write-abs");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void MathFloorIntegerSink_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var n = 5;
            io.print(str.repeat("-", math.floor(n / 2)));
            io.print(str.repeat("x", math.round(2.4)));
            """,
            "math-floor-integer-sink");
    }

    [Fact]
    public void ResultOption_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var r = result.ok(10);
            io.print(result.unwrapOr(result.map(r, (x) => x * 2), 0));
            io.print(result.isErr(result.err("bad")));
            io.print(result.unwrapOr(result.andThen(r, (x) => result.ok(x * 2)), 0));
            io.print(result.isErr(result.andThen(result.err("bad"), (x) => result.ok(x))));
            var o = option.some(3);
            io.print(option.unwrapOr(option.map(o, (n) => n + 1), 0));
            io.print(option.isNone(option.none()));
            io.print(option.unwrapOr(option.andThen(o, (n) => option.some(n + 1)), 0));
            io.print(option.isNone(option.andThen(option.none(), (n) => option.some(n))));
            """,
            "result-option");
    }

    [Fact]
    public void PrimaryConstructor_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            class Point(x, y) {
                function total() {
                    return this.x + this.y;
                }
            }
            var p = new Point(3, 4);
            io.print(p.x);
            io.print(p.total());
            """,
            "primary-constructor");
    }

    [Fact]
    public void TaggedCatch_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            try {
                throw dict { "kind": "IO", "message": "disk full" };
            } catch (e if e.kind == "IO") {
                io.print("io:" + e.message);
            } catch (e) {
                io.print("other");
            }
            try {
                throw dict { "kind": "Parse", "message": "bad token" };
            } catch (e if e.kind == "IO") {
                io.print("io");
            } catch (e) {
                io.print("generic:" + e.message);
            }
            """,
            "tagged-catch");
    }

    [Fact]
    public void NullConditional_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var missing = null;
            io.print(missing?.name == null);
            io.print(missing?["key"] == null);
            var d = dict { "a": 7 };
            io.print(d?.a);
            """,
            "null-conditional");
    }

    [Fact]
    public void Destructuring_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var [a, b] = [1, 2];
            io.print(a + b);
            var { name } = dict { "name": "Ada" };
            io.print(name);
            """,
            "destructuring");
    }

    [Fact]
    public void ParseJSONField_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            var o = parseJSON("{\"n\": 3, \"ok\": true}");
            io.print(o.n);
            io.print(o.ok);
            """,
            "parse-json-field");
    }

    [Fact]
    public void GetEnvOrMissing_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            io.print(io.getEnvOr("MALDA_PAIR_MISSING_ENV_XYZ", "absent"));
            """,
            "getenvor-missing");
    }
}
