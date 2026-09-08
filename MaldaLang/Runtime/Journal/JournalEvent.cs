// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.Journal;

/// <summary>
/// One agentic run event (prompt, tool, cap, hop, or workflow step).
/// </summary>
public enum JournalKind
{
    Prompt,
    Tool,
    Cap,
    Hop,
    Step
}

public sealed class JournalTokens
{
    public int In { get; set; }
    public int Out { get; set; }
}

public sealed class JournalEvent
{
    public DateTimeOffset Ts { get; set; } = DateTimeOffset.UtcNow;
    public string? Span { get; set; }
    public JournalKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? PromptHash { get; set; }
    public string? Model { get; set; }
    public JournalTokens? Tokens { get; set; }
    public double? Cost { get; set; }
    public long? Ms { get; set; }
    public bool Ok { get; set; } = true;
    public int? Repairs { get; set; }
    public string? Error { get; set; }

    public RunUsage ToUsage() => new()
    {
        PromptTokens = Tokens?.In ?? 0,
        CompletionTokens = Tokens?.Out ?? 0,
        Cost = Cost ?? 0,
        Ms = Ms ?? 0,
        Repairs = Repairs ?? 0,
        Model = Model
    };
}
