// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// L6 unforgeable capability: a sealed host object, not a dict. JSON / object literals
/// cannot rehydrate one. Inspect <c>kind</c>, <c>path</c>, and <c>name</c>; do not <c>Set</c>.
/// <c>path</c> is the confinement string (file path, HTTP prefix, MCP server, or argv prefix).
/// <c>name</c> is the optional MCP tool (empty = any tool on that server).
/// </summary>
public sealed class CapabilityToken : ObjectInstance
{
    public const string KindFileRead = "fileRead";
    public const string KindFileWrite = "fileWrite";
    public const string KindDirList = "dirList";
    public const string KindHttpGet = "httpGet";
    public const string KindMcpCall = "mcpCall";
    public const string KindShell = "shell";

    public string Kind { get; }
    public string Path { get; }
    public string Name { get; }

    private CapabilityToken(string kind, string path, string name) : base(null)
    {
        Kind = kind;
        Path = path;
        Name = name;
    }

    public static CapabilityToken Mint(string kind, string path, string? name = null) =>
        new(kind, path ?? "", name ?? "");

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null) =>
        name switch
        {
            "kind" => RuntimeValue.String(Kind),
            "path" => RuntimeValue.String(Path),
            "name" => RuntimeValue.String(Name),
            _ => throw new RuntimeException($"Undefined property '{name}' on capability token.")
        };

    public override bool TryGet(string name, out RuntimeValue? value, ClassDefinition? accessingClass = null)
    {
        if (name == "kind")
        {
            value = RuntimeValue.String(Kind);
            return true;
        }

        if (name == "path")
        {
            value = RuntimeValue.String(Path);
            return true;
        }

        if (name == "name")
        {
            value = RuntimeValue.String(Name);
            return true;
        }

        value = null;
        return false;
    }

    public override void Set(string name, RuntimeValue value) =>
        throw new RuntimeException("Capability tokens are immutable.");

    public override IEnumerable<string> GetAllKeys()
    {
        yield return "kind";
        yield return "path";
        if (Kind == KindMcpCall || !string.IsNullOrEmpty(Name))
            yield return "name";
    }

    public override string ToString() =>
        string.IsNullOrEmpty(Name) ? $"<cap {Kind} {Path}>" : $"<cap {Kind} {Path} {Name}>";
}
