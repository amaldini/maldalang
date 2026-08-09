namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspiledLlmClientRuntimeTests
{
    [Fact]
    public void TranspiledOpenRouterClient_SetMaxTokens_UpdatesMaxTokensProperty()
    {
        const string source = """
            var client = new OpenRouterClient("vendor/model");
            client.setMaxTokens(16384);
            print(string(client.maxTokens));
            """;

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("16384", result.StdOut);
    }

    [Fact]
    public void TranspiledOpenRouterClient_AttributionProperties_AreReadableAfterAssign()
    {
        const string source = """
            var client = new OpenRouterClient("vendor/model");
            client.httpReferer = "https://example.com/app";
            client.appTitle = "Demo";
            print(client.httpReferer + "|" + client.appTitle);
            """;

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("https://example.com/app|Demo", result.StdOut);
    }
}
