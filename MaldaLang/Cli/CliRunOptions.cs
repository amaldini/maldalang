// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Cli;

using MaldaLang.Runtime.Profiling;

/// <summary>
/// CLI flags for <c>malda run</c> / script execution (Phase 4.3 <c>--strict-types</c>).
/// </summary>
public sealed class CliRunOptions
{
    public ProfilingOptions? Profiling { get; init; }

    public bool StrictTypes { get; init; }
}
