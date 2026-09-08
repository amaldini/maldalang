// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.LlmCassettes;

using System.Text.RegularExpressions;

/// <summary>
/// Strips secrets from cassette payloads so recorded files are safe to commit.
/// </summary>
public static class Redactor
{
    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "apiKey", "api_key", "x-api-key", "token", "password",
        "secret", "bearer", "openai-api-key", "openrouter-api-key"
    };

    private static readonly Regex SecretPattern = new(
        @"(?i)(sk-[A-Za-z0-9_\-]{8,}|Bearer\s+[A-Za-z0-9\-._~+/]+=*)",
        RegexOptions.Compiled);

    public static bool IsSecretKey(string key) => SecretKeys.Contains(key);

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";
        return SecretPattern.Replace(text, "[REDACTED]");
    }
}
