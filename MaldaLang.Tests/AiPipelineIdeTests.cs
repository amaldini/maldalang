// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Services;
using Xunit;

namespace MaldaLang.Tests;

public class AiPipelineIdeTests
{
    private readonly LanguageService _service = new();

    [Fact]
    public void GetHover_StepInsideWorkflow_ShowsWorkflowStepHelp()
    {
        var source = """
            workflow ingest(input) {
                step load = loadDocuments(input);
            }
            """;
        var hover = _service.GetHoverInformation(source, 1, 4);
        Assert.NotNull(hover);
        Assert.Contains("Durable step boundary", hover);
    }

    [Fact]
    public void GetCompletions_IncludesPromptSchemaKeywords()
    {
        var completions = _service.GetCompletions("var x = ", 0, 8);
        Assert.DoesNotContain(completions, c => c.Label == "chain");
        Assert.Contains(completions, c => c.Label == "prompt");
        Assert.Contains(completions, c => c.Label == "schema");
        Assert.Contains(completions, c => c.Label == "api");
        Assert.Contains(completions, c => c.Label == "await");
        Assert.Contains(completions, c => c.Label == "function");
    }

    [Fact]
    public void GetCompletions_IncludesAiPipelineBuiltIns()
    {
        var completions = _service.GetCompletions("var x = ", 0, 8);
        Assert.Contains(completions, c => c.Label == "runPrompt");
        Assert.Contains(completions, c => c.Label == "evalPrompt");
        Assert.Contains(completions, c => c.Label == "withExamples");
        Assert.Contains(completions, c => c.Label == "runProgram");
    }

    [Fact]
    public void GetHover_ApiKeyword_ShowsClosedApiHelp()
    {
        var source = """
            api Calc {
                function add(a, b);
            }
            """;
        var hover = _service.GetHoverInformation(source, 0, 1);
        Assert.NotNull(hover);
        Assert.Contains("runProgram", hover);
    }

    [Fact]
    public void GetHover_ApiName_ShowsMethodSignatures()
    {
        var source = """
            api Calc {
                function add(a, b);
            }
            """;
        var hover = _service.GetHoverInformation(source, 0, 5);
        Assert.NotNull(hover);
        Assert.Contains("api Calc", hover);
        Assert.Contains("function add(a, b)", hover);
    }

    [Fact]
    public void GetHover_ApiName_ShowsOptionalParameterTypes()
    {
        var source = """
            api Calc {
                function add(a: number, b: number);
            }
            """;
        var hover = _service.GetHoverInformation(source, 0, 5);
        Assert.NotNull(hover);
        Assert.Contains("function add(a: number, b: number)", hover);
    }

    [Fact]
    public void GetHover_RunPrompt_ShowsBuiltInHelp()
    {
        var source = "var x = runPrompt(p, client);";
        var hover = _service.GetHoverInformation(source, 0, 10);
        Assert.NotNull(hover);
        Assert.Contains("onToken", hover);
    }

    [Fact]
    public void GetHover_EvalPrompt_ShowsBuiltInHelp()
    {
        var source = "var x = evalPrompt(p, fixture);";
        var hover = _service.GetHoverInformation(source, 0, 10);
        Assert.NotNull(hover);
        Assert.Contains("fixture", hover);
        Assert.Contains("No LLM", hover);
    }

    [Fact]
    public void GetSignatureHelp_EvalPrompt_ShowsParameters()
    {
        var source = "var x = evalPrompt(";
        var help = _service.GetSignatureHelp(source, 0, source.Length);
        Assert.NotNull(help);
        Assert.Contains("prompt", help.Parameters);
        Assert.Contains("fixture", help.Parameters);
        Assert.Contains("typeName?", help.Parameters);
    }

    [Fact]
    public void GetSignatureHelp_RunPrompt_ShowsParameters()
    {
        var source = "var x = runPrompt(";
        var help = _service.GetSignatureHelp(source, 0, source.Length);
        Assert.NotNull(help);
        Assert.Contains("prompt", help.Parameters);
        Assert.Contains("client?", help.Parameters);
    }

    [Fact]
    public void GetSignatureHelp_WithExamples_ShowsParameters()
    {
        var source = "var x = withExamples(";
        var help = _service.GetSignatureHelp(source, 0, source.Length);
        Assert.NotNull(help);
        Assert.Contains("prompt", help.Parameters);
        Assert.Contains("examples", help.Parameters);
    }
}
