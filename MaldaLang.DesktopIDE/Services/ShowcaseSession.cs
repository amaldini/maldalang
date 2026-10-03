// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
    public ShowcaseLyrics Lyrics { get; init; } = ShowcaseLyrics.Empty;

    /// <summary>Start the reel clock when the splash is up, without waiting for the recorder.</summary>
    public bool AutoStart { get; init; }

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

        var autoStart = Array.Exists(args, arg => string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase));

        try
        {
            session = Load(args[demo + 1], handshake, autoStart);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return true;
    }

    public static ShowcaseSession Load(string playlistPath, string? handshakeDirectory, bool autoStart = false)
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

        foreach (var browse in playlist.Browse)
        {
            PrepareShowcaseFile(repoRoot, browse.File, out var absolutePath);
            browse.AbsolutePath = absolutePath;
        }

        foreach (var page in playlist.Manual)
        {
            PrepareShowcaseFile(repoRoot, Path.Combine("ReferenceManual", page.File), out var absolutePath);
            page.AbsolutePath = absolutePath;
            page.File = Path.GetFileName(page.File);
            page.Anchor = NormalizeManualAnchor(page.File, absolutePath, page.Anchor);
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
            scene.AbsolutePath = PrepareShowcaseFile(repoRoot, scene.File, out _);
        }

        playlist.BindTimeline();

        var handshake = string.IsNullOrWhiteSpace(handshakeDirectory)
            ? Path.Combine(repoRoot, "artifacts", "showcase")
            : Path.GetFullPath(handshakeDirectory);
        Directory.CreateDirectory(handshake);

        return new ShowcaseSession
        {
            Playlist = playlist,
            RepoRoot = repoRoot,
            HandshakeDirectory = handshake,
            AutoStart = autoStart,
            Lyrics = ShowcaseLyrics.Load(Path.GetDirectoryName(fullPlaylist) ?? repoRoot)
        };
    }

    private static string PrepareShowcaseFile(string repoRoot, string file, out string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            throw new InvalidOperationException("Every showcase entry needs a file path.");
        }

        var relative = file.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        absolutePath = Path.IsPathRooted(relative)
            ? Path.GetFullPath(relative)
            : Path.GetFullPath(Path.Combine(repoRoot, relative));
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException($"Showcase file was not found: {file}", absolutePath);
        }

        return absolutePath;
    }

    private static readonly Regex ManualAnchorPattern = new("^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string NormalizeManualAnchor(string file, string absolutePath, string? anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor))
        {
            return "";
        }

        var id = anchor.Trim().TrimStart('#');
        if (!ManualAnchorPattern.IsMatch(id))
        {
            throw new InvalidOperationException($"Showcase manual page '{file}' has an invalid anchor '{anchor}'.");
        }

        var html = File.ReadAllText(absolutePath);
        if (!html.Contains($"id=\"{id}\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Showcase manual page '{file}' has no anchor '{id}'.");
        }

        return id;
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

    /// <summary>Milliseconds from the splash when the last scene ends.</summary>
    public int EndMs { get; set; }

    public List<ShowcaseBrowsePick> Browse { get; set; } = new();
    public List<ShowcaseManualPage> Manual { get; set; } = new();
    public List<ShowcaseScene> Scenes { get; set; } = new();

    /// <summary>Milliseconds from the splash when the first cue replaces the title.</summary>
    public int OpeningMs
    {
        get
        {
            if (Browse.Count > 0)
            {
                return Browse[0].StartMs;
            }

            if (Manual.Count > 0)
            {
                return Manual[0].StartMs;
            }

            return Scenes.Count > 0 ? Scenes[0].StartMs : 0;
        }
    }

    public void BindTimeline()
    {
        var cues = new List<(string Label, int StartMs, Action<int> SetEnd)>();
        foreach (var browse in Browse)
        {
            cues.Add((CueLabel(browse.Caption, browse.File), browse.StartMs, end => browse.EndMs = end));
        }

        foreach (var page in Manual)
        {
            cues.Add((CueLabel(page.Caption, page.File), page.StartMs, end => page.EndMs = end));
        }

        foreach (var scene in Scenes)
        {
            cues.Add((CueLabel(scene.Caption, scene.File), scene.StartMs, end => scene.EndMs = end));
        }

        if (cues.Count == 0)
        {
            throw new InvalidOperationException("Showcase playlist has no cues.");
        }

        var previous = -1;
        string? previousLabel = null;
        foreach (var cue in cues)
        {
            if (cue.StartMs < 0)
            {
                throw new InvalidOperationException($"Showcase cue '{cue.Label}' has a negative startMs.");
            }

            if (cue.StartMs <= previous)
            {
                throw new InvalidOperationException(
                    $"Showcase cue '{cue.Label}' startMs is {cue.StartMs}, which is not after '{previousLabel}' at {previous} ms.");
            }

            previous = cue.StartMs;
            previousLabel = cue.Label;
        }

        if (EndMs <= previous)
        {
            throw new InvalidOperationException($"Showcase endMs must be later than '{previousLabel}' at {previous} ms.");
        }

        for (var i = 0; i < cues.Count; i++)
        {
            var end = i + 1 < cues.Count ? cues[i + 1].StartMs : EndMs;
            cues[i].SetEnd(end);
        }
    }

    private static string CueLabel(string caption, string file)
    {
        if (!string.IsNullOrWhiteSpace(caption))
        {
            return caption.Trim();
        }

        if (!string.IsNullOrWhiteSpace(file))
        {
            return file.Trim();
        }

        return "cue";
    }
}

public sealed class ShowcaseManualPage
{
    public string File { get; set; } = "";
    public string Caption { get; set; } = "";
    public string Anchor { get; set; } = "";
    public int StartMs { get; set; }

    [JsonIgnore]
    public int EndMs { get; set; }

    [JsonIgnore]
    public string AbsolutePath { get; set; } = "";
}

public sealed class ShowcaseBrowsePick
{
    public string File { get; set; } = "";
    public string Caption { get; set; } = "";
    public int StartMs { get; set; }
    public int? FocusLine { get; set; }

    [JsonIgnore]
    public int EndMs { get; set; }

    [JsonIgnore]
    public string AbsolutePath { get; set; } = "";
}

public sealed class ShowcaseScene
{
    public string File { get; set; } = "";
    public string Caption { get; set; } = "";
    public string Panel { get; set; } = "output";
    public bool Maximize { get; set; }
    public int StartMs { get; set; }
    public int? FocusLine { get; set; }

    [JsonIgnore]
    public int EndMs { get; set; }

    [JsonIgnore]
    public string AbsolutePath { get; set; } = "";
}
