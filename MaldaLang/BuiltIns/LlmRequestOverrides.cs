// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections.Generic;

/// <summary>
/// Per-request LLM overrides from PromptInstance metadata (model, temperature, tools).
/// </summary>
public sealed class LlmRequestOverrides
{
    public string? Model { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public HashSet<string>? ToolNames { get; init; }
}
