// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.IO;
using MaldaLang;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using SystemEnvironment = System.Environment;

internal static class GraphMemoryBootstrap
{
    internal static string GetAssistantMemoryPath()
    {
        var userProfile = SystemEnvironment.GetFolderPath(SystemEnvironment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
            return "assistant";
        return Path.Combine(userProfile, ".malda", "memory", "assistant");
    }

    internal static GraphMemoryInstance CreateAssistantMemory(Interpreter interpreter, string? pathOverride = null)
    {
        var memory = new GraphMemoryInstance();
        memory.SetInterpreter(interpreter);
        var embedMode = (SystemEnvironment.GetEnvironmentVariable("MALDA_MEMORY_EMBED") ?? "hash").Trim().ToLowerInvariant();
        var initArgs = new List<RuntimeValue> { RuntimeValue.Integer(384), RuntimeValue.String("single") };
        var embedFn = CreateEmbedFunction(interpreter, embedMode);
        if (embedFn != null)
            initArgs.Add(RuntimeValue.Function(embedFn));
        memory.CallMethod("initialize", initArgs, interpreter);

        var path = string.IsNullOrWhiteSpace(pathOverride) ? GetAssistantMemoryPath() : pathOverride!;
        if (MemoryArtifactsExist(path))
            memory.CallMethod("load", new List<RuntimeValue> { RuntimeValue.String(path) }, interpreter);
        return memory;
    }

    private static bool MemoryArtifactsExist(string basePath)
    {
        if (File.Exists($"{basePath}.graph.json"))
            return true;
        var dir = Path.GetDirectoryName(basePath);
        if (string.IsNullOrEmpty(dir))
            dir = ".";
        return File.Exists(Path.Combine(dir, ".graph.json"));
    }

    private static FunctionValue? CreateEmbedFunction(Interpreter interpreter, string embedMode)
    {
        var source = embedMode == "bow"
            ? "function __assistantMemoryEmbed(text) { return embedBagOfWords(text, 384); }"
            : "function __assistantMemoryEmbed(text) { return embedHash(text, 384); }";
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            var statements = parser.Parse();
            if (statements.Count > 0 && statements[0] is FunctionDeclaration fn)
                return new FunctionValue(fn, interpreter._globals, false, null);
        }
        catch
        {
        }
        return null;
    }
}
