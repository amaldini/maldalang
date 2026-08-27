// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class EvalPromptTests : TestBase
{
    public EvalPromptTests()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
    }

    [Fact]
    public void SchemaFixture_OkReturnsData()
    {
        var source = """
            schema Card {
                name: string;
                email: string;
            }
            prompt extract(raw) -> Card {
                user: raw;
            }
            var p = extract("Ada");
            var checked = evalPrompt(p, dict { "name": "Ada", "email": "ada@example.com" });
            print(checked.ok);
            print(checked.data.name);
            print(p.returnType);
            """;
        var result = RunProgram(source);
        var lines = result.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("Ada", lines[1].Trim());
        Assert.Equal("Card", lines[2].Trim());
    }

    [Fact]
    public void SchemaFixture_MismatchReturnsError()
    {
        var source = """
            schema Card {
                name: string;
            }
            prompt extract(raw) -> Card {
                user: raw;
            }
            var checked = evalPrompt(extract("x"), dict { "email": "nope" });
            print(checked.ok);
            print(checked.error);
            """;
        var result = RunProgram(source);
        var lines = result.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("false", lines[0].Trim());
        Assert.False(string.IsNullOrWhiteSpace(lines[1]));
    }

    [Fact]
    public void SumTypeFixture_CoercesVariantForMatch()
    {
        var source = """
            type Intent = Search(query: string) | Buy(sku: string, qty: int) | Help();
            prompt parseUtterance(text) -> Intent {
                user: text;
            }
            var p = parseUtterance("buy");
            var checked = evalPrompt(p, dict { "tag": "Buy", "sku": "SKU-9", "qty": 2 });
            if (checked.ok) {
                match checked.data {
                    case Buy(sku, qty): print($"buy {sku} x {qty}");
                    default: print("unexpected");
                }
            } else {
                print("fail");
            }
            """;
        var result = RunProgram(source);
        Assert.Contains("buy SKU-9 x 2", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FencedJsonString_ExtractsLikeAwait()
    {
        var source = """
            schema Card {
                name: string;
            }
            prompt extract(raw) -> Card {
                user: raw;
            }
            var fixture = "Here is the output:\n```json\n{ \"name\": \"Ada\" }\n```";
            var checked = evalPrompt(extract("x"), fixture);
            print(checked.ok);
            print(checked.data.name);
            """;
        var result = RunProgram(source);
        var lines = result.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("Ada", lines[1].Trim());
    }

    [Fact]
    public void InvalidTag_NotOk()
    {
        var source = """
            type Intent = Search(query) | Buy(sku, qty);
            prompt parseUtterance(text) -> Intent {
                user: text;
            }
            var checked = evalPrompt(parseUtterance("x"), dict { "tag": "Nope" });
            print(checked.ok);
            """;
        var result = RunProgram(source);
        Assert.Contains("false", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstanceEval_MatchesBuiltin()
    {
        var source = """
            schema Card {
                name: string;
            }
            prompt extract(raw) -> Card {
                user: raw;
            }
            var p = extract("Ada");
            var a = evalPrompt(p, dict { "name": "Ada" });
            var b = p.eval(dict { "name": "Ada" });
            print(a.ok);
            print(b.ok);
            print(a.data.name);
            print(b.data.name);
            """;
        var result = RunProgram(source);
        var lines = result.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("true", lines[1].Trim());
        Assert.Equal("Ada", lines[2].Trim());
        Assert.Equal("Ada", lines[3].Trim());
    }

    [Fact]
    public void GatherInstance_EvalsExtractTypeWithoutLlm()
    {
        var source = """
            schema Card {
                name: string;
            }
            prompt research(q) -> Card {
                gather: ["read_file"];
                user: q;
            }
            var p = research("notes");
            print(p.returnType);
            print(p.gather[0]);
            var checked = evalPrompt(p, dict { "name": "Ada" });
            print(checked.ok);
            print(checked.data.name);
            """;
        var result = RunProgram(source);
        var lines = result.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("Card", lines[0].Trim());
        Assert.Equal("read_file", lines[1].Trim());
        Assert.Equal("true", lines[2].Trim());
        Assert.Equal("Ada", lines[3].Trim());
    }

    [Fact]
    public void TypeNameOverride_ValidatesUntypedPrompt()
    {
        var source = """
            schema Card {
                name: string;
            }
            prompt raw(text) {
                user: text;
            }
            var checked = evalPrompt(raw("x"), dict { "name": "Ada" }, "Card");
            print(checked.ok);
            print(checked.data.name);
            """;
        var result = RunProgram(source);
        var lines = result.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("Ada", lines[1].Trim());
    }

    [Fact]
    public void UntypedJsonString_ReturnsParsedObject()
    {
        var source = """
            prompt raw(text) {
                user: text;
            }
            var checked = evalPrompt(raw("x"), "{ \"name\": \"Ada\" }");
            print(checked.ok);
            print(checked.data.name);
            """;
        var result = RunProgram(source);
        var lines = result.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("Ada", lines[1].Trim());
    }

    [Fact]
    public void Transpiled_SchemaAndSumType_Agree()
    {
        var source = """
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
            print(card.ok);
            print(card.data.name);
            match evalPrompt(parseUtterance("x"), dict { "tag": "Buy", "sku": "SKU-9", "qty": 2 }).data {
                case Buy(sku, qty): print($"buy {sku} x {qty}");
                default: print("unexpected");
            }
            """;
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("Ada", lines[1].Trim());
        Assert.Contains("buy SKU-9 x 2", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpiled_InstanceEval_FencedJson()
    {
        var source = """
            schema Card {
                name: string;
                email: string;
            }
            prompt extract(raw) -> Card {
                user: raw;
            }
            var p = extract("Ada");
            var fromFence = p.eval("Here:\n```json\n{ \"name\": \"Ada\", \"email\": \"ada@example.com\" }\n```");
            print(fromFence.ok);
            print(fromFence.data.email);
            print(p.getUser());
            """;
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("ada@example.com", lines[1].Trim());
        Assert.Equal("Ada", lines[2].Trim());
    }
}
