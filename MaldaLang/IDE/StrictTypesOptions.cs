// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

/// <summary>
/// Phase 4.3: optional static checks (CLI <c>--strict-types</c>, IDE/LSP when enabled).
/// </summary>
public sealed class StrictTypesOptions
{
    public static StrictTypesOptions Default { get; } = new() { StrictTypes = false };

    public static StrictTypesOptions Enabled { get; } = new() { StrictTypes = true };

    public bool StrictTypes { get; init; }
}
