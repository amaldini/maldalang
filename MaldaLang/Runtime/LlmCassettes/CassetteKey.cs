// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.LlmCassettes;

/// <summary>
/// Cassette lookup key: promptHash + canonical args + model + mode + schema hash.
/// </summary>
public sealed record CassetteRequest(
    object? Messages,
    object? Tools,
    object? ResponseFormat,
    string? Model,
    string Mode,
    string? PromptHash = null,
    string? SchemaHash = null);

public static class CassetteKey
{
    public static string Compute(CassetteRequest request)
    {
        var promptHash = request.PromptHash
            ?? PromptHasher.HashParts(request.Messages, request.Tools);
        var schemaHash = request.SchemaHash
            ?? PromptHasher.HashParts(request.ResponseFormat);
        return PromptHasher.HashParts(
            promptHash,
            request.Messages,
            request.Tools,
            request.Model ?? "",
            request.Mode,
            schemaHash);
    }
}
