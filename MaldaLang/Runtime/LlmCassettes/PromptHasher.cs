// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.LlmCassettes;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Stable content hash for prompts and cassette keys (body + schema + tools + model hints).
/// </summary>
public static class PromptHasher
{
    public static string Hash(string canonicalJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string HashParts(params object?[] parts)
    {
        var payload = JsonSerializer.Serialize(parts, CassetteJson.Options);
        return Hash(payload);
    }

    public static string CanonicalJson(object? value) =>
        JsonSerializer.Serialize(value, CassetteJson.Options);
}
