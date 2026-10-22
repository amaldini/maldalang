// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Runtime.Profiling;

namespace MaldaLang.Compiler;

public partial class Compiler
{
    private static bool UsesUiFramework(string source)
    {
        // In-memory ui.state / ui.setState (HttpServer components) does not need UIHost.
        // Only the browser UI protocol (mount / events) requires the embedded host.
        return source.Contains("ui.mount(", StringComparison.Ordinal) ||
               source.Contains("ui.mountEnvelope(", StringComparison.Ordinal) ||
               source.Contains("uiMount(", StringComparison.Ordinal) ||
               source.Contains("uiMountEnvelope(", StringComparison.Ordinal) ||
               source.Contains("ui.render(", StringComparison.Ordinal) ||
               source.Contains("uiRender(", StringComparison.Ordinal) ||
               source.Contains("ui.dispatchEvent(", StringComparison.Ordinal) ||
               source.Contains("uiDispatchEvent(", StringComparison.Ordinal) ||
               source.Contains("ui.pullEvent(", StringComparison.Ordinal) ||
               source.Contains("uiPullEvent(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Starts the UI host at the top of the transpiled program. The host itself is
    /// <c>MaldaLang.UIHost.dll</c>, referenced and copied next to <c>malda.dll</c>
    /// (<see cref="GenerateTranspiledCsprojContent"/>), so transpiled and interpreted programs run
    /// the same host.
    /// </summary>
    private static string AddUiHostStartToTranspiledCode(string csharpCode)
    {
        const string startCall = "await global::MaldaLang.UIHost.EmbeddedUiHostRuntime.TryStartAsync();";
        if (csharpCode.Contains(EmbeddedUiHostStartMarker, StringComparison.Ordinal))
        {
            return csharpCode.Replace(EmbeddedUiHostStartMarker, $"{EmbeddedUiHostStartMarker}\n                    {startCall}", StringComparison.Ordinal);
        }

        const string mainSignature = "public static async Task Main(string[] args)";
        var mainIndex = csharpCode.IndexOf(mainSignature, StringComparison.Ordinal);
        var openBraceIndex = mainIndex >= 0 ? csharpCode.IndexOf('{', mainIndex) : -1;
        return openBraceIndex >= 0
            ? csharpCode.Insert(openBraceIndex + 1, $"\n                {startCall}")
            : csharpCode;
    }
}
