// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

/// <summary>
/// Shared prompt-body field names (object-literal keys and statement-based keywords).
/// </summary>
public static class PromptBodyFields
{
    public static readonly string[] Names =
    {
        "system", "user", "model", "temperature", "tools", "gather", "maxTokens", "examples"
    };

    public static bool IsName(string name) =>
        name is "system" or "user" or "model" or "temperature"
            or "tools" or "gather" or "maxTokens" or "examples";

    public static string DisplayList =>
        "system, user, model, temperature, tools, gather, maxTokens, or examples";
}
