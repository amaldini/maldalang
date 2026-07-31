// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

/// <summary>
/// A token-sized piece of streamed LLM output (OpenAI-compatible SSE deltas).
/// </summary>
/// <param name="Kind">content, reasoning, or tool_arguments (internal; not shown by default)</param>
/// <param name="Text">Text fragment to append</param>
public readonly record struct LlmStreamDelta(string Kind, string Text);
