namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspilerRuntimeHardeningTests
{
    private static string LoadTranspilerSource()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var transpilerPath = Path.Combine(repoRoot, "MaldaLang.Compiler", "CSharpTranspiler.cs");
        return File.ReadAllText(transpilerPath);
    }

    [Fact]
    public void TranspiledRuntimeHelpers_GeneratedCode_HandlesDictionaryLikeAndPlainObjects()
    {
        var generatedSource = LoadTranspilerSource();

        Assert.Contains("private static bool TryConvertDictionaryLikeToRuntimeValue", generatedSource);
        Assert.Contains("private static bool TryConvertNativeObjectToRuntimeValue", generatedSource);
        Assert.Contains("if (TryConvertDictionaryLikeToRuntimeValue(value, out var dictionaryValue)) return dictionaryValue;", generatedSource);
        Assert.Contains("if (value is System.Collections.IEnumerable seq && value is not string) return MaldaLang.Interpreter.RuntimeValue.Array(seq.Cast<object?>().Select(ToRuntimeValue).ToList());", generatedSource);
        Assert.Contains("if (TryConvertNativeObjectToRuntimeValue(value, out var objectValue)) return objectValue;", generatedSource);
    }

    [Fact]
    public void TranspiledRuntimeHelpers_GeneratedCode_SetObjectMember_ReusesToRuntimeValue()
    {
        var generatedSource = LoadTranspilerSource();

        Assert.Contains("var runtimeValue = value is MaldaLang.Interpreter.RuntimeValue rv ? rv : RuntimeHelpers.ToRuntimeValue(value);", generatedSource);
        Assert.Contains("instance.Set(memberName, runtimeValue);", generatedSource);
    }

    [Fact]
    public void TranspiledRuntimeHelpers_GeneratedCode_BuiltInLength_SupportsArrays()
    {
        var generatedSource = LoadTranspilerSource();

        Assert.Contains("public static int BuiltInLength(object? value)", generatedSource);
        Assert.Contains("if (IsArray(unwrapped))", generatedSource);
        Assert.Contains("(object)RuntimeHelpers.BuiltInLength(", generatedSource);
    }

    [Fact]
    public void TranspiledRuntimeHelpers_GeneratedCode_CallObjectMethod_DispatchesLlmClients()
    {
        var generatedSource = LoadTranspilerSource();

        Assert.Contains("else if (instance is MaldaLang.BuiltIns.LLMClientInstance llmClient)", generatedSource);
        Assert.Contains("result = llmClient.CallMethod(methodName, runtimeArgs, null);", generatedSource);
        Assert.Contains("else if (instance is MaldaLang.BuiltIns.LlamaCppClientInstance llamaCppClient)", generatedSource);
        Assert.Contains("else if (instance is MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance llmBridge)", generatedSource);
    }
}
