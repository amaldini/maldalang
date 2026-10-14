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
        // math.* — runtime tags: floor/ceil/round return float-tagged values.
        // abs/min/max keep the argument kind, so they live in ArgumentKindPreserving.
        ["math.floor"] = "float",
        ["math.ceil"] = "float",
        ["math.round"] = "float",
        ["math.sqrt"] = "float",
        ["math.sin"] = "float",
        ["math.cos"] = "float",
        ["math.tan"] = "float",
        ["math.log"] = "float",
        ["math.exp"] = "float",
        ["math.pow"] = "float",

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

    // math.abs(-3) is int and math.abs(-1.5) is float at runtime; min/max likewise follow
    // their arguments. A fixed "float" tag here rejected `var n: int = math.abs(x)`.
    private static readonly HashSet<string> ArgumentKindPreserving = new(StringComparer.OrdinalIgnoreCase)
    {
        "math.abs", "math.min", "math.max", "abs",
    };

    /// <summary>
    /// Returns a Tier-0 tag for a known builtin call, or null when unknown / out of scope.
    /// <paramref name="inferArgument"/> resolves argument tags for built-ins whose return
    /// kind follows their arguments; without it those built-ins are unknown.
    /// </summary>
    public static string? TryGetReturnType(
        Expression callee,
        IReadOnlyList<Expression>? arguments = null,
        Func<Expression, string?>? inferArgument = null)
    {
        var calleeKey = CalleeKey(callee);
        if (calleeKey != null && ArgumentKindPreserving.Contains(calleeKey))
            return InferNumericKind(arguments, inferArgument);

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

    private static string? CalleeKey(Expression callee) => callee switch
    {
        IdentifierExpression id => id.Name,
        MemberAccessExpression { Object: IdentifierExpression ns } member => ns.Name + "." + member.Member,
        _ => null,
    };

    /// <summary>
    /// int when every argument is int, float when they are all numeric and one is float,
    /// otherwise unknown (an array argument, or an argument the checker cannot type).
    /// </summary>
    private static string? InferNumericKind(
        IReadOnlyList<Expression>? arguments,
        Func<Expression, string?>? inferArgument)
    {
        if (arguments == null || arguments.Count == 0 || inferArgument == null)
            return null;

        var sawFloat = false;
        foreach (var argument in arguments)
        {
            switch (inferArgument(argument))
            {
                case "int":
                    break;
                case "float":
                    sawFloat = true;
                    break;
                default:
                    return null;
            }
        }

        return sawFloat ? "float" : "int";
    }
}
