// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Models;

public class AIChatSettings
{
    public string? ApiKey { get; set; }
    public string? ApiUrl { get; set; }
    public string? Model { get; set; }
    public bool UseOpenRouterClient { get; set; } = true;
}