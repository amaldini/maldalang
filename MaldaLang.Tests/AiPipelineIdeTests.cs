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
        Assert.Contains(completions, c => c.Label == "await");
        Assert.Contains(completions, c => c.Label == "function");
    }

    [Fact]
    public void GetCompletions_IncludesAiPipelineBuiltIns()
    {
        var completions = _service.GetCompletions("var x = ", 0, 8);
        Assert.Contains(completions, c => c.Label == "runPrompt");
        Assert.Contains(completions, c => c.Label == "withExamples");
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
