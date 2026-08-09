// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MaldaLang.IDE.Services;

/// <summary>
/// Supplies the docs/llm language pack (embedded in malda.dll) to IDE AI sessions,
/// plus live decorator docs from <see cref="LanguageService"/>.
/// </summary>
public class MALDALanguageContextService
{
    public const string ResourcePrefix = "MaldaLang.LanguagePack.";

    private static readonly Assembly PackAssembly = typeof(MALDALanguageContextService).Assembly;

    /// <summary>
    /// Always-on boot context for system prompts: syntax + gotchas from the language pack.
    /// </summary>
    public string GetInlineBootContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine("MALDA language pack (boot context — syntax + gotchas). Prefer the llm/ files in the agent working directory for deeper lookup.");
        sb.AppendLine();
        sb.AppendLine("--- malda-syntax.md ---");
        sb.AppendLine(ReadPackResource("malda-syntax.md"));
        sb.AppendLine();
        sb.AppendLine("--- malda-gotchas.md ---");
        sb.AppendLine(ReadPackResource("malda-gotchas.md"));
        return sb.ToString();
    }

    /// <summary>
    /// Compatibility shim: returns the compact boot context (syntax + gotchas).
    /// Prefer <see cref="MaterializeLanguagePack"/> for agent sessions.
    /// </summary>
    public string GetLanguageSpecification() => GetInlineBootContext();

    /// <summary>
    /// Writes the embedded language pack under <paramref name="directory"/>/llm/,
    /// plus live <c>DECORATORS.md</c> and a short <c>INDEX.md</c> load-order guide.
    /// </summary>
    public string MaterializeLanguagePack(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory is required.", nameof(directory));

        var llmDir = Path.Combine(directory, "llm");
        Directory.CreateDirectory(llmDir);

        foreach (var relativePath in EnumeratePackRelativePaths())
        {
            var content = ReadPackResource(relativePath);
            var destPath = Path.Combine(llmDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);
            File.WriteAllText(destPath, content, Encoding.UTF8);
        }

        File.WriteAllText(Path.Combine(llmDir, "DECORATORS.md"), BuildDecoratorsSection(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(llmDir, "INDEX.md"), BuildIndexMarkdown(), Encoding.UTF8);

        return llmDir;
    }

    /// <summary>
    /// Relative paths of embedded pack files (forward-slash, no prefix).
    /// </summary>
    public static IReadOnlyList<string> EnumeratePackRelativePaths()
    {
        return PackAssembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => name.Substring(ResourcePrefix.Length))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ReadPackResource(string relativePath)
    {
        var resourceName = ResourcePrefix + relativePath.Replace('\\', '/');
        using var stream = PackAssembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Embedded language-pack resource not found: {resourceName}");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string BuildIndexMarkdown()
    {
        return @"# IDE language pack index

Working directory layout for the MALDA IDE assistant:

- `current.malda` — the user's open file
- `llm/` — language pack (docs/llm) plus live decorator docs

## Suggested load order (token budget)

1. `llm/malda-syntax.md` (always — idioms, array mutation, `$""...""` interpolation)
2. `llm/malda-gotchas.md` (always — mistakes that run without error)
3. 2–4 files from `llm/few-shot/` matching the task
4. `llm/malda-grammar.md` if generating unfamiliar constructs
5. `llm/malda-builtins-min.md` for stdlib shape; grep `llm/malda-builtins.tsv` for one specific name
6. `llm/DECORATORS.md` for `@GET` / `@Tool` / parameter decorator rules (live from the IDE)

Prefer `grep` + partial `read_file` over reading large files whole.
";
    }

    private string BuildDecoratorsSection()
    {
        var decorators = LanguageService.GetSupportedDecorators();
        var sb = new StringBuilder();

        sb.AppendLine("# MALDA decorators (live from LanguageService)");
        sb.AppendLine();
        sb.AppendLine("Decorators annotate functions and function parameters. Place them before function declarations using `@DecoratorName` syntax.");
        sb.AppendLine();
        sb.AppendLine("## Critical rules");
        sb.AppendLine();
        sb.AppendLine("- Parameter decorators (`@PathParam`, `@QueryParam`, `@Body`) can ONLY be used with HTTP endpoint decorators (`@GET`, `@POST`, `@PUT`, `@DELETE`, `@PATCH`, `@OPTIONS`).");
        sb.AppendLine("- Tool decorators (`@Tool`, `@MCPTool`) are for standalone functions and do NOT use parameter decorators.");
        sb.AppendLine("- Never mix parameter decorators with tool decorators — they are mutually exclusive.");
        sb.AppendLine();

        var httpDecorators = new List<string> { "GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS" };
        var toolDecorators = new List<string> { "Tool", "MCPTool" };
        var paramDecorators = new List<string> { "PathParam", "QueryParam", "Body" };

        AppendDecoratorGroup(sb, decorators, "HTTP endpoint decorators", httpDecorators);
        AppendDecoratorGroup(sb, decorators, "Tool decorators", toolDecorators);
        AppendDecoratorGroup(sb, decorators, "Parameter decorators (REST endpoints only)", paramDecorators);

        sb.AppendLine("## Examples");
        sb.AppendLine();
        sb.AppendLine("```malda");
        sb.AppendLine("// REST endpoint with path parameter");
        sb.AppendLine("@GET(\"/api/users/{id}\")");
        sb.AppendLine("function getUser(@PathParam(\"id\") userId) { ... }");
        sb.AppendLine();
        sb.AppendLine("// Tool decorator (no parameter decorators)");
        sb.AppendLine("@Tool(\"calculate_sum\", \"Adds two numbers\")");
        sb.AppendLine("function add(a, b) { return a + b; }");
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static void AppendDecoratorGroup(
        StringBuilder sb,
        IReadOnlyDictionary<string, DecoratorInfo> decorators,
        string title,
        IEnumerable<string> names)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        foreach (var name in names)
        {
            if (!decorators.TryGetValue(name, out var info))
                continue;

            sb.AppendLine($"- {info.Format}");
            sb.AppendLine($"  {info.Documentation}");
            foreach (var argDesc in info.ArgDescriptions)
                sb.AppendLine($"  - {argDesc}");
            sb.AppendLine();
        }
    }
}
