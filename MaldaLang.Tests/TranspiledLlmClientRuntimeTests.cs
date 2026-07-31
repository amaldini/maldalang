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
}
