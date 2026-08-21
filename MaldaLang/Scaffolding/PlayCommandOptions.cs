// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Scaffolding;

using System;
using System.Collections.Generic;
using System.IO;

public sealed class PlayCommandOptions
{
    public const int DefaultPort = 8765;
    public const string DefaultHost = "127.0.0.1";
    public const string PreviewDirectoryName = ".malda-play";

    public string SourcePath { get; init; } = string.Empty;
    public int? Port { get; init; }
    public string Host { get; init; } = DefaultHost;
    public bool OpenBrowser { get; init; }
    public string? PreviewDirectory { get; init; }
}

public readonly record struct JavaScriptCompileResult(bool Success, string? OutputPath, string? ErrorMessage);

public static class PlayCommandOptionsParser
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        TextWriter error,
        out PlayCommandOptions? options)
    {
        options = null;
        if (args.Count < 1)
        {
            WriteUsage(error);
            return false;
        }

        string? sourcePath = null;
        int? port = null;
        string host = PlayCommandOptions.DefaultHost;
        bool openBrowser = false;
        var seenFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (token.StartsWith("-", StringComparison.Ordinal))
            {
                if (!seenFlags.Add(token))
                {
                    error.WriteLine($"Duplicate option '{token}'.");
                    return false;
                }

                if (string.Equals(token, "--open", StringComparison.OrdinalIgnoreCase))
                {
                    openBrowser = true;
                    continue;
                }

                if (string.Equals(token, "--port", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Count || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        error.WriteLine("Option '--port' requires a value.");
                        return false;
                    }

                    i++;
                    if (!int.TryParse(args[i], out var parsedPort) || parsedPort < 1 || parsedPort > 65535)
                    {
                        error.WriteLine("Option '--port' must be an integer between 1 and 65535.");
                        return false;
                    }

                    port = parsedPort;
                    continue;
                }

                if (string.Equals(token, "--host", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Count || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        error.WriteLine("Option '--host' requires a value.");
                        return false;
                    }

                    i++;
                    var parsedHost = args[i].Trim();
                    if (string.IsNullOrWhiteSpace(parsedHost))
                    {
                        error.WriteLine("Option '--host' cannot be empty.");
                        return false;
                    }

                    host = parsedHost;
                    continue;
                }

                error.WriteLine($"Unknown option '{token}'.");
                WriteUsage(error);
                return false;
            }

            if (sourcePath != null)
            {
                error.WriteLine($"Unexpected extra argument '{token}'.");
                WriteUsage(error);
                return false;
            }

            sourcePath = token;
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            error.WriteLine("Missing MALDA source file.");
            WriteUsage(error);
            return false;
        }

        options = new PlayCommandOptions
        {
            SourcePath = sourcePath,
            Port = port,
            Host = host,
            OpenBrowser = openBrowser
        };
        return true;
    }

    public static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage: malda play <file.malda> [options]");
        output.WriteLine("  Compile a MALDA file to JavaScript, write a host page, and serve a local preview.");
        output.WriteLine("  Options:");
        output.WriteLine("    --port <n>   Bind port (default 8765; tries the next ports if that one is busy)");
        output.WriteLine("    --host <h>   Bind host (default 127.0.0.1)");
        output.WriteLine("    --open       Open the default browser when the OS allows it");
        output.WriteLine("  Examples:");
        output.WriteLine("    malda play app.malda");
        output.WriteLine("    malda play app.malda --open");
        output.WriteLine("    malda play Examples/Games/game_bounce.malda --port 8766");
        output.WriteLine("  PWA packaging is still: malda compile app.malda --mode pwa -o dist");
        output.WriteLine("  Press Ctrl+C to stop the preview server.");
    }
}

public sealed class PlayPrepareResult
{
    public required string SourcePath { get; init; }
    public required string PreviewDirectory { get; init; }
    public required string JavaScriptPath { get; init; }
    public required string HostHtmlPath { get; init; }
}
