// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MaldaLang.DesktopIDE.Services;

public sealed class ShowcaseSession
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public required ShowcasePlaylist Playlist { get; init; }
    public required string RepoRoot { get; init; }
    public required string HandshakeDirectory { get; init; }

    public string StatusPath => Path.Combine(HandshakeDirectory, "status.json");
    public string StartPath => Path.Combine(HandshakeDirectory, "start");

    internal static ShowcaseSession? Pending { get; set; }

    public static bool TryParseLaunchArgs(string[] args, out ShowcaseSession? session, out string? error)
    {
        session = null;
        error = null;
        var demo = Array.FindIndex(args, arg => string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase));
        if (demo < 0)
        {
            return false;
        }

        if (demo + 1 >= args.Length || args[demo + 1].StartsWith('-'))
        {
            error = "--demo requires a path to a playlist JSON file.";
            return true;
        }

        string? handshake = null;
        var handshakeFlag = Array.FindIndex(args, arg => string.Equals(arg, "--handshake", StringComparison.OrdinalIgnoreCase));
        if (handshakeFlag >= 0)
        {
            if (handshakeFlag + 1 >= args.Length || args[handshakeFlag + 1].StartsWith('-'))
            {
                error = "--handshake requires a directory.";
                return true;
            }

            handshake = args[handshakeFlag + 1];
        }

        try
        {
            session = Load(args[demo + 1], handshake);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return true;
    }

    public static ShowcaseSession Load(string playlistPath, string? handshakeDirectory)
    {
        var fullPlaylist = Path.GetFullPath(playlistPath);
        if (!File.Exists(fullPlaylist))
        {
            throw new FileNotFoundException("Showcase playlist was not found.", fullPlaylist);
        }

        var repoRoot = FindRepoRoot(Path.GetDirectoryName(fullPlaylist) ?? fullPlaylist)
            ?? throw new InvalidOperationException("Could not find the repository root (MaldaLang.sln) above the playlist.");

        ShowcasePlaylist playlist;
        try
        {
            var json = File.ReadAllText(fullPlaylist);
            playlist = JsonSerializer.Deserialize<ShowcasePlaylist>(json, JsonOptions)
                ?? throw new InvalidOperationException("Showcase playlist is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Showcase playlist is not valid JSON: {ex.Message}");
        }

        if (playlist.Scenes.Count == 0)
        {
            throw new InvalidOperationException("Showcase playlist has no scenes.");
        }

        if (playlist.Width < 640 || playlist.Height < 480)
        {
            throw new InvalidOperationException("Showcase window width must be at least 640 and height at least 480.");
        }

        if (playlist.SplitBeatMs < 0 || playlist.ReadyTimeoutMs < 1)
        {
            throw new InvalidOperationException("Showcase splitBeatMs and readyTimeoutMs must be non-negative, and readyTimeoutMs at least 1.");
        }

        foreach (var scene in playlist.Scenes)
        {
            if (string.IsNullOrWhiteSpace(scene.File))
            {
                throw new InvalidOperationException("Every showcase scene needs a file path.");
            }

            var panel = scene.Panel.Trim().ToLowerInvariant();
            if (panel is not ("output" or "preview" or "server"))
            {
                throw new InvalidOperationException($"Scene '{scene.File}' has panel '{scene.Panel}'. Use output, preview, or server.");
            }

            scene.Panel = panel;
            if (scene.HoldMs < 0)
            {
                throw new InvalidOperationException($"Scene '{scene.File}' has a negative holdMs.");
            }

            var relative = scene.File.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            scene.AbsolutePath = Path.IsPathRooted(relative)
                ? Path.GetFullPath(relative)
                : Path.GetFullPath(Path.Combine(repoRoot, relative));
            if (!File.Exists(scene.AbsolutePath))
            {
                throw new FileNotFoundException($"Showcase scene was not found: {scene.File}", scene.AbsolutePath);
            }
        }

        var handshake = string.IsNullOrWhiteSpace(handshakeDirectory)
            ? Path.Combine(repoRoot, "artifacts", "showcase")
            : Path.GetFullPath(handshakeDirectory);
        Directory.CreateDirectory(handshake);

        return new ShowcaseSession
        {
            Playlist = playlist,
            RepoRoot = repoRoot,
            HandshakeDirectory = handshake
        };
    }

    private static string? FindRepoRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MaldaLang.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}

public sealed class ShowcasePlaylist
{
    public int Width { get; set; } = 1600;
    public int Height { get; set; } = 900;
    public int Left { get; set; }
    public int Top { get; set; }
    public int SplitBeatMs { get; set; } = 800;
    public int ReadyTimeoutMs { get; set; } = 90000;
    public List<ShowcaseScene> Scenes { get; set; } = new();
}

public sealed class ShowcaseScene
{
    public string File { get; set; } = "";
    public string Caption { get; set; } = "";
    public string Panel { get; set; } = "output";
    public bool Maximize { get; set; }
    public int HoldMs { get; set; } = 2000;
    public int? FocusLine { get; set; }

    [JsonIgnore]
    public string AbsolutePath { get; set; } = "";
}
