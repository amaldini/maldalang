// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using MaldaLang.Parser.AST.Expressions;

/// <summary>
/// Small static map of high-traffic Tier-1 builtin return tags for IDE type compatibility.
/// Not generated from the full registry — intentionally curated.
/// </summary>
public static class Tier1BuiltinReturnHints
{
    private static readonly Dictionary<string, string> Namespaced = new(StringComparer.OrdinalIgnoreCase)
    {
        // math.* — runtime tags: floor/ceil/round/abs return float-tagged values
        ["math.floor"] = "float",
        ["math.ceil"] = "float",
        ["math.round"] = "float",
        ["math.abs"] = "float",
        ["math.sqrt"] = "float",
        ["math.sin"] = "float",
        ["math.cos"] = "float",
        ["math.tan"] = "float",
        ["math.log"] = "float",
        ["math.exp"] = "float",
        ["math.pow"] = "float",
        ["math.min"] = "float",
        ["math.max"] = "float",

        // str.*
        ["str.trim"] = "string",
        ["str.trimText"] = "string",
        ["str.lower"] = "string",
        ["str.upper"] = "string",
        ["str.substring"] = "string",
        ["str.replace"] = "string",
        ["str.join"] = "string",
        ["str.repeat"] = "string",
        ["str.len"] = "int",
        ["str.length"] = "int",
        ["str.indexOf"] = "int",

        // io.* — skip getEnv (null vs string); getEnvOr / readFile / input are strings
        ["io.readFile"] = "string",
        ["io.input"] = "string",
        ["io.getEnvOr"] = "string",
        ["io.readText"] = "string",
    };

    private static readonly Dictionary<string, string> FlatAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["floor"] = "float",
        ["ceil"] = "float",
        ["round"] = "float",
        ["abs"] = "float",
        ["sqrt"] = "float",
        ["trim"] = "string",
        ["lower"] = "string",
        ["upper"] = "string",
        ["substring"] = "string",
        ["join"] = "string",
        ["len"] = "int",
        ["readFile"] = "string",
        ["input"] = "string",
        ["getEnvOr"] = "string",
        ["print"] = "void",
    };

    /// <summary>
    /// Returns a Tier-0 tag for a known builtin call, or null when unknown / out of scope.
    /// </summary>
    public static string? TryGetReturnType(Expression callee)
    {
        if (callee is IdentifierExpression id)
        {
            if (FlatAliases.TryGetValue(id.Name, out var flat))
                return flat == "void" ? null : flat;
            return null;
        }

        if (callee is MemberAccessExpression member &&
            member.Object is IdentifierExpression ns)
        {
            var key = ns.Name + "." + member.Member;
            if (Namespaced.TryGetValue(key, out var namespaced))
                return namespaced;
        }

        return null;
    }
}
