// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Threading;
using LLama.Native;

namespace MaldaLang.BuiltIns;

/// <summary>
/// Quiets llama.cpp / LLamaSharp native console spam (llama_context:, ggml_*, etc.)
/// so MALDA UI / agent output stays readable. Opt back in with <c>MALDA_LLAMA_LOG=1</c>.
/// </summary>
internal static class LlamaCppLog
{
    private static int _configured;

    /// <summary>
    /// Install a no-op native log callback once, before the first model load.
    /// Safe to call repeatedly.
    /// </summary>
    internal static void EnsureQuietByDefault()
    {
        if (Interlocked.Exchange(ref _configured, 1) != 0)
            return;

        if (IsTruthyEnv("MALDA_LLAMA_LOG"))
            return;

        try
        {
            // Prefer the high-level path: works before or after the native library loads.
            NativeLogConfig.llama_log_set(static (_, _) => { });
        }
        catch
        {
            try
            {
                NativeLibraryConfig.All.WithLogCallback(static (_, _) => { });
            }
            catch
            {
                // Library already configured/loaded; leave default logging.
            }
        }
    }

    private static bool IsTruthyEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }
}
