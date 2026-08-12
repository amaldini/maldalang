// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.IDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

/// <summary>
/// Known informational type-hint names beyond Tier 0: declared classes/schemas/sum types
/// in the current unit (plus imported exports) and built-in host class names.
/// </summary>
public sealed class TypeHintNameIndex
{
    /// <summary>
    /// Built-in host classes constructible with <c>new</c> (aligned with interpreter /
    /// IDE surfaces). Shared by known-hint checks and type-hint completions.
    /// </summary>
    public static readonly string[] HostClassNames =
    {
        "LLMClient",
        "OpenRouterClient",
        "LlamaCppClient",
        "LlamaEmbedder",
        "Conversation",
        "Tool",
        "Agent",
        "CodingAgent",
        "GitAgent",
        "DevAgent",
        "MALDACodingAgent",
        "HumanAgent",
        "HTMLCache",
        "RestServer",
        "RestClient",
        "HttpServer",
        "MCPServer",
        "MCPClient",
        "ACPClient",
        "ACPServer",
        "ACPAgentTool",
        "LLMClientBridge",
        "LLMServer",
        "SqlServerClient",
        "PostgresClient",
        "SqliteClient",
        "SerialConnection",
        "ArduinoConnection",
        "VectorDB",
        "GraphMemory",
    };

    private static readonly HashSet<string> HostClassSet =
        new(HostClassNames, StringComparer.Ordinal);

    private readonly HashSet<string> _declared =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _declaredKind =
        new(StringComparer.Ordinal);

    public static TypeHintNameIndex Build(IEnumerable<Statement> statements)
    {
        var index = new TypeHintNameIndex();
        foreach (var stmt in statements)
            index.Register(stmt);
        return index;
    }

    /// <summary>
    /// Merges exported class/schema/sum-type names from imported modules into this index.
    /// </summary>
    public void MergeImported(ModuleSymbolResolver.ImportedSymbolSet imported)
    {
        foreach (var classDecl in imported.Classes)
            AddDeclared(classDecl.Name, "class");
        foreach (var schemaDecl in imported.Schemas)
            AddDeclared(schemaDecl.Name, "schema");
        foreach (var typeDecl in imported.Types)
            AddDeclared(typeDecl.TypeName, "type");
    }

    public static bool IsHostClass(string name) =>
        !string.IsNullOrWhiteSpace(name) && HostClassSet.Contains(name);

    public bool IsDeclared(string name) =>
        !string.IsNullOrWhiteSpace(name) && _declared.Contains(name);

    /// <summary>
    /// True when the name is a Tier 0 hint, a declared class/schema, or a host class.
    /// Tier 0 matching remains case-insensitive; declared/host names use ordinal match.
    /// </summary>
    public bool IsKnown(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        if (Tier0TypeHints.IsKnown(typeName))
            return true;

        return _declared.Contains(typeName) || HostClassSet.Contains(typeName);
    }

    /// <summary>
    /// Canonical display form for a known hint: Tier 0 canonical tag when applicable,
    /// otherwise the declared/host name as written.
    /// </summary>
    public string? NormalizeKnown(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var tier0 = Tier0TypeTags.NormalizeToCanonical(typeName);
        if (tier0 != null)
            return tier0;

        // Tier 0 hint aliases that are not typeOf tags (void/any) or map to float (double).
        if (Tier0TypeHints.IsKnown(typeName))
        {
            var trimmed = typeName.Trim();
            if (string.Equals(trimmed, "double", StringComparison.OrdinalIgnoreCase))
                return "float";
            return trimmed;
        }

        if (_declared.Contains(typeName) || HostClassSet.Contains(typeName))
            return typeName;

        return null;
    }

    public IEnumerable<(string Name, string Kind)> GetDeclaredEntries()
    {
        foreach (var name in _declared.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var kind = _declaredKind.TryGetValue(name, out var k) ? k : "class";
            yield return (name, kind);
        }
    }

    public static List<CompletionItem> GetCompletions(
        TypeHintNameIndex? index,
        string? partialPrefix)
    {
        var partial = partialPrefix?.Trim() ?? string.Empty;
        var items = new List<CompletionItem>();
        items.AddRange(Tier0TypeHints.GetCompletions(partial));

        foreach (var host in HostClassNames)
        {
            if (!string.IsNullOrEmpty(partial) &&
                !host.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add(new CompletionItem
            {
                Label = host,
                Kind = "type",
                Detail = "Built-in class (informational type hint)",
                InsertText = host
            });
        }

        if (index != null)
        {
            foreach (var (name, kind) in index.GetDeclaredEntries())
            {
                if (!string.IsNullOrEmpty(partial) &&
                    !name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var detail = kind switch
                {
                    "schema" => "Schema (informational type hint)",
                    "type" => "Sum type (informational type hint)",
                    _ => "Class (informational type hint)"
                };
                items.Add(new CompletionItem
                {
                    Label = name,
                    Kind = "type",
                    Detail = detail,
                    InsertText = name
                });
            }
        }

        return items
            .GroupBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void Register(Statement stmt)
    {
        switch (stmt)
        {
            case ClassDeclaration classDecl:
                AddDeclared(classDecl.Name, "class");
                foreach (var member in classDecl.Members)
                {
                    if (member.Value is FunctionDeclaration method)
                    {
                        foreach (var inner in method.Body.Statements)
                            Register(inner);
                    }
                }
                break;
            case SchemaDeclaration schemaDecl:
                AddDeclared(schemaDecl.Name, "schema");
                break;
            case TypeDeclaration typeDecl:
                AddDeclared(typeDecl.TypeName, "type");
                break;
            case FunctionDeclaration funcDecl:
                foreach (var inner in funcDecl.Body.Statements)
                    Register(inner);
                break;
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    Register(inner);
                break;
        }
    }

    private void AddDeclared(string name, string kind)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        _declared.Add(name);
        if (!_declaredKind.ContainsKey(name))
            _declaredKind[name] = kind;
    }
}
