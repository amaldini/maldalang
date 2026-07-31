// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Argument-count checking for built-ins, in one place so every message reads the same way:
/// <c>name() expects 2 arguments: (min, max)</c>.
///
/// The wording matters beyond ergonomics. It is the only machine-readable record of a
/// built-in's shape, so <c>scripts/sync-llm-builtins-tsv.ps1</c> reads these call sites to
/// build the lookup table coding agents grep instead of inventing signatures.
/// </summary>
public static class BuiltInArity
{
    /// <summary>Pass as <paramref name="maximum"/> for a built-in that takes any number of trailing arguments.</summary>
    public const int Unbounded = -1;

    public static void Require(
        string name,
        List<RuntimeValue> args,
        int minimum,
        int maximum,
        string signature = "")
    {
        if (args.Count < minimum || (maximum != Unbounded && args.Count > maximum))
            throw new RuntimeException($"{name}() expects {DescribeArguments(minimum, maximum, signature)}");
    }

    /// <summary>
    /// The text following "name() expects". Shared with the language-pack generator's guard
    /// test so the table and the runtime error can never describe a built-in differently.
    /// </summary>
    public static string DescribeArguments(int minimum, int maximum, string signature)
    {
        var suffix = signature.Length == 0 ? "" : $": ({signature})";

        if (maximum == Unbounded)
            return $"at least {minimum} {Plural(minimum)}{suffix}";

        if (minimum == maximum)
            return minimum == 0 && signature.Length == 0
                ? "0 arguments"
                : $"{minimum} {Plural(minimum)}{suffix}";

        return $"{minimum}-{maximum} arguments{suffix}";
    }

    private static string Plural(int count) => count == 1 ? "argument" : "arguments";
}
