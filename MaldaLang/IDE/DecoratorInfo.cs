// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Services;

public class DecoratorInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Documentation { get; set; } = string.Empty;
    public int MinArgs { get; set; }
    public int MaxArgs { get; set; }
    public List<string> ArgDescriptions { get; set; } = new();
}
