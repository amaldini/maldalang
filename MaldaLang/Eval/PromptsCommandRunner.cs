// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Eval;

using System.Text.Json;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Runtime.LlmCassettes;

public sealed class PromptsCommandRunner
{
    public int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] != "--diff" || args.Length < 2)
        {
            output.WriteLine("malda prompts --diff <baseline.json> [path.malda]");
            return args.Length == 0 ? 2 : 0;
        }

        var baselinePath = args[1];
        if (!File.Exists(baselinePath))
        {
            error.WriteLine($"prompts: baseline not found: {baselinePath}");
            return 1;
        }

        var previous = JsonSerializer.Deserialize<EvalReport>(File.ReadAllText(baselinePath));
        var sourcePath = args.Length > 2 ? args[2] : null;
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            output.WriteLine("No current program; listing baseline hashes:");
            foreach (var row in previous?.Cases ?? [])
                output.WriteLine($"  {row.Suite}/{row.Case}: {row.PromptHash}");
            return 0;
        }

        var parser = new Parser(new Lexer(File.ReadAllText(sourcePath), sourcePath).Tokenize(), sourcePath);
        var prompts = parser.Parse().OfType<PromptDeclaration>().ToList();
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prompt in prompts)
        {
            var version = prompt.Decorators?.FirstOrDefault(d => d.Name == "version");
            var hash = PromptHasher.HashParts(prompt.Name, prompt.ReturnType, version?.Arguments?.FirstOrDefault()?.ToString());
            current[prompt.Name] = hash;
        }

        foreach (var row in previous?.Cases ?? [])
        {
            if (row.PromptHash != null)
                output.WriteLine($"baseline {row.Suite}/{row.Case}: {row.PromptHash}");
        }

        foreach (var (name, hash) in current)
            output.WriteLine($"current {name}: {hash}");
        return 0;
    }
}
