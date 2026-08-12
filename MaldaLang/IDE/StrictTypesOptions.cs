// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

/// <summary>
/// Static analysis options for type hints and optional full strict suite.
/// CLI <c>--strict-types</c> uses <see cref="Enabled"/>. IDE/LSP defaults to
/// <see cref="Default"/> (type mismatches as errors) with <see cref="Lenient"/> as opt-out.
/// </summary>
public sealed class StrictTypesOptions
{
    /// <summary>
    /// IDE/LSP default: elevate type-hint mismatches and unknown hints to Error.
    /// Does not enable match/pure/bounds/const (those require <see cref="StrictTypes"/>).
    /// </summary>
    public static StrictTypesOptions Default { get; } = new()
    {
        StrictTypes = false,
        TypeErrors = true
    };

    /// <summary>
    /// Historical severity: type mismatches are Warning, unknown hints are Info.
    /// </summary>
    public static StrictTypesOptions Lenient { get; } = new()
    {
        StrictTypes = false,
        TypeErrors = false
    };

    /// <summary>
    /// Full suite (<c>--strict-types</c>): type Errors plus match/pure/bounds/const.
    /// </summary>
    public static StrictTypesOptions Enabled { get; } = new()
    {
        StrictTypes = true,
        TypeErrors = true
    };

    /// <summary>
    /// When true, enables match exhaustiveness, <c>@pure</c>, bounds, and const checks,
    /// and elevates type diagnostics to Error.
    /// </summary>
    public bool StrictTypes { get; init; }

    /// <summary>
    /// When true, type-hint mismatches and unknown hints are Errors (without the full strict suite).
    /// </summary>
    public bool TypeErrors { get; init; }

    /// <summary>True when type diagnostics should use Error severity.</summary>
    public bool ElevateTypeSeverity => TypeErrors || StrictTypes;
}
