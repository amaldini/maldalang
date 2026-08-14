// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// L6 unforgeable capability: a sealed host object, not a dict. JSON / object literals
/// cannot rehydrate one. Inspect <c>kind</c> and <c>path</c>; do not <c>Set</c>.
/// </summary>
public sealed class CapabilityToken : ObjectInstance
{
    public const string KindFileRead = "fileRead";
    public const string KindFileWrite = "fileWrite";
    public const string KindDirList = "dirList";

    public string Kind { get; }
    public string Path { get; }

    private CapabilityToken(string kind, string path) : base(null)
    {
        Kind = kind;
        Path = path;
    }

    public static CapabilityToken Mint(string kind, string path) =>
        new(kind, path ?? "");

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null) =>
        name switch
        {
            "kind" => RuntimeValue.String(Kind),
            "path" => RuntimeValue.String(Path),
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

        value = null;
        return false;
    }

    public override void Set(string name, RuntimeValue value) =>
        throw new RuntimeException("Capability tokens are immutable.");

    public override IEnumerable<string> GetAllKeys()
    {
        yield return "kind";
        yield return "path";
    }

    public override string ToString() => $"<cap {Kind} {Path}>";
}
