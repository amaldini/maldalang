// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Compiler;

/// <summary>
/// Desktop IDE F5 launch kind. Full-stack sources debug the host partition in
/// the interpreter and the client partition in WebView2 at the same time.
/// </summary>
public enum MaldaDebugLaunchKind
{
    Interpret,
    JavaScript,
    FullStack
}

public static class MaldaDebugLaunch
{
    public static MaldaDebugLaunchKind Classify(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return MaldaDebugLaunchKind.Interpret;
        }

        if (FullStackSourceInspector.IsFullStackSource(source))
        {
            return MaldaDebugLaunchKind.FullStack;
        }

        if (JsBrowserApiDetector.UsesBrowserHost(source))
        {
            return MaldaDebugLaunchKind.JavaScript;
        }

        return MaldaDebugLaunchKind.Interpret;
    }
}
