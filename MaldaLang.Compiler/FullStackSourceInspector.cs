// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.RegularExpressions;
using System.Linq;

namespace MaldaLang.Compiler;

public static class FullStackSourceInspector
{
    public static bool IsFullStackSource(string source)
    {
        return HasCompileTimeTargetDecorator(source, "client", "javascript") &&
               (HasCompileTimeTargetDecorator(source, "server", "csharp") || HasRouteDecorator(source));
    }

    public static bool HasCompileTimeTargetDecorator(string source, params string[] names)
    {
        var alternatives = string.Join("|", names.Select(Regex.Escape));
        return Regex.IsMatch(source, @"^\s*@(?:" + alternatives + @")\s*\(", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    }

    public static bool HasRouteDecorator(string source)
    {
        return Regex.IsMatch(source, @"^\s*@(GET|POST|PUT|PATCH|DELETE|OPTIONS|PAGE|AIPAGE|ACTION|COMPONENT|LIVE)\s*\(", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    }

    public static int ExtractHttpPort(string source, int fallbackPort = 8090)
    {
        var match = Regex.Match(source, @"new\s+HttpServer\s*\(\s*(?<port>\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["port"].Value, out var port)
            ? port
            : fallbackPort;
    }
}
