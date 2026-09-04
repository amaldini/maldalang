// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// DT7 curated interpret vs C# transpile pairs (same stdout, exit 0).
/// Compile-only smoke stays in <see cref="TranspileSmokeTests"/>.
/// Still n/a (smoke only): LLM-awaiting prompts, agent_governance_golden,
/// workflow/job Examples (see WorkflowTranspilerParityTests; runprogram_in_step
/// is smoke + interpreter), grounded_ask
/// (GraphMemory score drift), capability_tokens Example (relative cwd file I/O;
/// abs-path cap fixtures are inline below).
/// Sequential: pair capture and in-process <c>runMALDA</c> both redirect
/// <see cref="Console.Out"/>.
/// </summary>
[Collection("Sequential")]
public class InterpretTranspilePairTests
{
    [Theory]
    [InlineData("Examples/Basics/first_look.malda")]
    [InlineData("Examples/Basics/schema_validate.malda")]
    [InlineData("Examples/Basics/schema_sumtype_validate.malda")]
    [InlineData("Examples/Basics/as_variant.malda")]
    [InlineData("Examples/Prompts/eval_prompt.malda")]
    [InlineData("docs/llm/few-shot/28_api_program_prompt.malda")]
    [InlineData("Examples/Agents/phase6_pure_validate.malda")]
    [InlineData("Examples/Prompts/api_program_calc.malda")]
    [InlineData("Examples/Prompts/prompt_budget.malda")]
    [InlineData("Examples/Prompts/multimodal_attachments.malda")]
    [InlineData("docs/llm/few-shot/31_mcptool_schema.malda")]
    [InlineData("docs/llm/few-shot/32_mcptool_validate.malda")]
    [InlineData("Examples/MCP/mcp_schema_tool.malda")]
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
    public void AsVariant_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            type Intent = Search(query) | Buy(sku, qty);
            var tagged = dict { "tag": "Buy", "sku": "SKU-9", "qty": 2 };
            match asVariant("Intent", tagged) {
                case Buy(sku, qty): io.print($"buy {sku} x {qty}");
                default: io.print("fail");
            }
            """,
            "as-variant");
    }

    [Fact]
    public void EvalPrompt_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            schema Card {
                name: string;
            }
            type Intent = Search(query) | Buy(sku, qty);
            prompt extract(raw) -> Card {
                user: raw;
            }
            prompt parseUtterance(text) -> Intent {
                user: text;
            }
            var card = evalPrompt(extract("x"), dict { "name": "Ada" });
            io.print(card.data.name);
            var fromFence = extract("x").eval("{ \"name\": \"Ada\" }");
            io.print(fromFence.data.name);
            match evalPrompt(parseUtterance("x"), dict { "tag": "Buy", "sku": "SKU-9", "qty": 2 }).data {
                case Buy(sku, qty): io.print($"buy {sku} x {qty}");
                default: io.print("fail");
            }
            """,
            "eval-prompt");
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

    [Fact]
    public void VectorDB_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            """
            function embed(text) {
                return embedBagOfWords(text, 8);
            }

            var dim = 8;
            var db = new VectorDB(dim, "single");
            db.init(embed);
            db.add("hello world");
            db.add("goodbye moon");

            var hits = db.searchSimilar("hello", 1);
            io.print(hits.length);
            io.print(hits[0].data);

            var retriever = db.asRetriever({ topK: 1 });
            var docs = retriever.get("hello");
            io.print(docs.length);
            io.print(docs[0].content);
            """,
            "vectordb");
    }

    [Fact]
    public void CreateGlobTool_Execute_SameStdout()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_pair_glob_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "src"));
        File.WriteAllText(Path.Combine(tempDir, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(tempDir, "src", "b.txt"), "beta");
        File.WriteAllText(Path.Combine(tempDir, "skip.cs"), "// cs");
        var workDir = tempDir.Replace("\\", "/");

        try
        {
            InterpretTranspilePair.AssertSameFromSource(
                $@"
var tool = createGlobTool(""{workDir}"");
var result = tool.execute({{ ""pattern"": ""**/*.txt"" }});
io.print(result.count);
io.print(result.truncated);
var items = result.items;
var i = 0;
while (i < length(items)) {{
    io.print(items[i].path);
    i = i + 1;
}}
",
                "createGlobTool-execute");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CreateFileLifecycleTools_Execute_SameStdout()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_pair_lifecycle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var workDir = tempDir.Replace("\\", "/");

        try
        {
            InterpretTranspilePair.AssertSameFromSource(
                $@"
var workDir = ""{workDir}"";
writeFile(workDir + ""/src.txt"", ""payload"");
var deleteTool = createDeleteFileTool(workDir);
var copyTool = createCopyFileTool(workDir);
var ensureTool = createEnsureDirTool(workDir);
var copied = copyTool.execute({{ ""srcPath"": ""src.txt"", ""destPath"": ""copied.txt"" }});
print(""copy="" + string(copied.success));
print(""content="" + readFile(workDir + ""/copied.txt""));
var ensured = ensureTool.execute({{ ""dirPath"": ""nested/dir"" }});
print(""ensure="" + string(ensured.success));
print(""hasDir="" + string(hasDirectory(workDir + ""/nested/dir"")));
var deleted = deleteTool.execute({{ ""filePath"": ""src.txt"" }});
print(""delete="" + string(deleted.success));
print(""gone="" + string(!hasFile(workDir + ""/src.txt"")));
",
                "createFileLifecycleTools-execute");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CreateCheckMaldaTool_Execute_SameStdout()
    {
        InterpretTranspilePair.AssertSameFromSource(
            @"
var tool = createCheckMaldaTool();
var result = tool.execute({ ""sourceOrFilePath"": ""var x = 1;"" });
print(""ok="" + string(result.ok));
",
            "createCheckMaldaTool-execute");
    }

    [Fact]
    public void CreateFileTools_Execute_SameStdout()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_pair_tools_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "note.txt"), "hello world");
        var workDir = tempDir.Replace("\\", "/");

        try
        {
            InterpretTranspilePair.AssertSameFromSource(
                $@"
var workDir = ""{workDir}"";
writeFile(workDir + ""/note.txt"", ""hello world"");
writeFile(workDir + ""/edit.txt"", ""alpha beta"");
var readTool = createReadFileTool(workDir);
var grepTool = createGrepTool(workDir);
var listTool = createListDirectoryTool(workDir);
print(""read="" + string(readTool.execute({{ ""filePath"": ""note.txt"" }})));
var hits = grepTool.execute({{ ""pattern"": ""hello"", ""filePath"": ""note.txt"" }});
print(""grep="" + string(length(hits)));
var listed = listTool.execute({{ ""dirPath"": ""."" }});
print(""listed="" + string(length(listed) > 0));
var planTool = createSubmitPlanTool();
var plan = planTool.execute({{ ""steps"": [{{ ""id"": ""s1"", ""description"": ""one"" }}] }});
print(""plan="" + string(plan.accepted) + "","" + string(plan.stepCount));
var editTool = createEditFileTool(workDir);
var edited = editTool.execute({{
    ""filePath"": ""edit.txt"",
    ""edits"": [{{ ""oldText"": ""beta"", ""newText"": ""gamma"" }}]
}});
print(""edit="" + string(edited.success) + "","" + string(edited.applied));
var runTool = createRunMALDATool();
var ran = runTool.execute({{ ""sourceOrFilePath"": ""print(1 + 1);"" }});
print(""run="" + string(ran.success) + "","" + string(ran.output));
",
                "createFileTools-execute");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }
}
