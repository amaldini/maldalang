// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace MaldaLang.BuiltIns;

/// <summary>
/// Virtual filesystem over assembly resources staged by <c>malda compile --embed-folder</c>.
/// Logical paths use the scheme <c>embed:&lt;alias&gt;/&lt;relative&gt;</c>.
/// </summary>
public static class EmbeddedFolderStore
{
    public const string SchemePrefix = "embed:";
    public const string ResourceNamePrefix = "malda.embed.";

    private static readonly object Gate = new();
    private static bool _initialized;
    private static readonly Dictionary<string, Dictionary<string, byte[]>> Folders =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex AliasPattern = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public static bool IsEmbedPath(string? path) =>
        !string.IsNullOrEmpty(path) &&
        path.StartsWith(SchemePrefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsValidAlias(string? alias) =>
        !string.IsNullOrEmpty(alias) && AliasPattern.IsMatch(alias);

    /// <summary>
    /// Parse <c>embed:alias</c> or <c>embed:alias/rel/path</c>.
    /// </summary>
    public static bool TryParsePath(string path, out string alias, out string relative)
    {
        alias = "";
        relative = "";
        if (!IsEmbedPath(path))
        {
            return false;
        }

        var rest = path.Substring(SchemePrefix.Length).Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(rest))
        {
            return false;
        }

        var slash = rest.IndexOf('/');
        if (slash < 0)
        {
            alias = rest;
            relative = "";
            return IsValidAlias(alias);
        }

        alias = rest.Substring(0, slash);
        if (!TryNormalizeRelative(rest.Substring(slash + 1), out relative))
        {
            return false;
        }

        return IsValidAlias(alias);
    }

    public static string MakeRoot(string alias) => SchemePrefix + alias;

    public static string Join(string embedRootOrPath, params string[] parts)
    {
        if (!TryJoin(embedRootOrPath, parts, out var joined))
        {
            throw new ArgumentException($"Invalid embed path join under '{embedRootOrPath}'.");
        }

        return joined;
    }

    public static bool TryJoin(string embedRootOrPath, string[] parts, out string joined)
    {
        joined = "";
        if (!TryParsePath(embedRootOrPath, out var alias, out var relative))
        {
            return false;
        }

        var segments = new List<string>();
        if (!string.IsNullOrEmpty(relative))
        {
            segments.Add(relative);
        }

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            var cleaned = part.Replace('\\', '/').Trim('/');
            if (cleaned.Length == 0)
            {
                continue;
            }

            if (IsEmbedPath(cleaned))
            {
                return false;
            }

            segments.Add(cleaned);
        }

        if (!TryNormalizeRelative(string.Join("/", segments), out var normalized))
        {
            return false;
        }

        joined = string.IsNullOrEmpty(normalized) ? MakeRoot(alias) : $"{SchemePrefix}{alias}/{normalized}";
        return true;
    }

    public static bool HasAlias(string alias)
    {
        EnsureInitialized();
        return Folders.ContainsKey(alias);
    }

    public static bool HasFile(string path)
    {
        if (!TryParsePath(path, out var alias, out var relative) || string.IsNullOrEmpty(relative))
        {
            return false;
        }

        EnsureInitialized();
        return Folders.TryGetValue(alias, out var files) && files.ContainsKey(relative);
    }

    public static bool HasDirectory(string path)
    {
        if (!TryParsePath(path, out var alias, out var relative))
        {
            return false;
        }

        EnsureInitialized();
        if (!Folders.TryGetValue(alias, out var files))
        {
            return false;
        }

        if (string.IsNullOrEmpty(relative))
        {
            return true;
        }

        var prefix = relative + "/";
        return files.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ReadText(string path)
    {
        var bytes = ReadBytes(path);
        return bytes == null ? null : Encoding.UTF8.GetString(bytes);
    }

    public static byte[]? ReadBytes(string path)
    {
        if (!TryParsePath(path, out var alias, out var relative) || string.IsNullOrEmpty(relative))
        {
            return null;
        }

        EnsureInitialized();
        if (!Folders.TryGetValue(alias, out var files))
        {
            return null;
        }

        return files.TryGetValue(relative, out var bytes) ? bytes : null;
    }

    /// <summary>
    /// One-level listing under an embed directory. Returns (name, isDirectory, fullEmbedPath).
    /// </summary>
    public static IReadOnlyList<(string Name, bool IsDirectory, string Path)> List(string path)
    {
        if (!TryParsePath(path, out var alias, out var relative))
        {
            return Array.Empty<(string, bool, string)>();
        }

        EnsureInitialized();
        if (!Folders.TryGetValue(alias, out var files))
        {
            return Array.Empty<(string, bool, string)>();
        }

        var prefix = string.IsNullOrEmpty(relative) ? "" : relative + "/";
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string, bool, string)>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in files.Keys)
        {
            if (!string.IsNullOrEmpty(prefix) &&
                !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = string.IsNullOrEmpty(prefix) ? key : key.Substring(prefix.Length);
            if (string.IsNullOrEmpty(remainder))
            {
                continue;
            }

            var slash = remainder.IndexOf('/');
            if (slash < 0)
            {
                if (seenFiles.Add(remainder))
                {
                    var full = string.IsNullOrEmpty(relative)
                        ? $"{SchemePrefix}{alias}/{remainder}"
                        : $"{SchemePrefix}{alias}/{relative}/{remainder}";
                    result.Add((remainder, false, full));
                }
            }
            else
            {
                var dirName = remainder.Substring(0, slash);
                if (dirs.Add(dirName))
                {
                    var full = string.IsNullOrEmpty(relative)
                        ? $"{SchemePrefix}{alias}/{dirName}"
                        : $"{SchemePrefix}{alias}/{relative}/{dirName}";
                    result.Add((dirName, true, full));
                }
            }
        }

        return result.OrderBy(e => e.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Enumerate file paths under an embed directory (or a single file path).
    /// </summary>
    public static IReadOnlyList<string> EnumerateFiles(string path, bool recursive)
    {
        if (!TryParsePath(path, out var alias, out var relative))
        {
            return Array.Empty<string>();
        }

        EnsureInitialized();
        if (!Folders.TryGetValue(alias, out var files))
        {
            return Array.Empty<string>();
        }

        if (!string.IsNullOrEmpty(relative) && files.ContainsKey(relative))
        {
            return new[] { $"{SchemePrefix}{alias}/{relative}" };
        }

        var prefix = string.IsNullOrEmpty(relative) ? "" : relative + "/";
        var list = new List<string>();
        foreach (var key in files.Keys)
        {
            if (!string.IsNullOrEmpty(prefix) &&
                !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!recursive && !string.IsNullOrEmpty(prefix))
            {
                var remainder = key.Substring(prefix.Length);
                if (remainder.Contains('/'))
                {
                    continue;
                }
            }
            else if (!recursive && string.IsNullOrEmpty(prefix) && key.Contains('/'))
            {
                continue;
            }

            list.Add($"{SchemePrefix}{alias}/{key}");
        }

        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public static void RegisterForTests(string alias, IReadOnlyDictionary<string, string> textFiles)
    {
        if (!IsValidAlias(alias))
        {
            throw new ArgumentException($"Invalid embed alias '{alias}'.", nameof(alias));
        }

        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in textFiles)
        {
            if (!TryNormalizeRelative(pair.Key, out var rel) || string.IsNullOrEmpty(rel))
            {
                continue;
            }

            map[rel] = Encoding.UTF8.GetBytes(pair.Value ?? "");
        }

        RegisterForTests(alias, map);
    }

    /// <summary>
    /// Test helper that registers binary files (e.g. GraphMemory <c>.vectordb.bin</c>) under an embed alias.
    /// </summary>
    public static void RegisterForTests(string alias, IReadOnlyDictionary<string, byte[]> binaryFiles)
    {
        if (!IsValidAlias(alias))
        {
            throw new ArgumentException($"Invalid embed alias '{alias}'.", nameof(alias));
        }

        lock (Gate)
        {
            _initialized = true;
            var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in binaryFiles)
            {
                if (!TryNormalizeRelative(pair.Key, out var rel) || string.IsNullOrEmpty(rel))
                {
                    continue;
                }

                map[rel] = pair.Value ?? Array.Empty<byte>();
            }

            Folders[alias] = map;
        }
    }

    public static void ResetForTests()
    {
        lock (Gate)
        {
            Folders.Clear();
            _initialized = false;
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            ScanAssembly(Assembly.GetEntryAssembly());
            ScanAssembly(Assembly.GetExecutingAssembly());
            _initialized = true;
        }
    }

    private static void ScanAssembly(Assembly? assembly)
    {
        if (assembly == null)
        {
            return;
        }

        string[] names;
        try
        {
            names = assembly.GetManifestResourceNames();
        }
        catch
        {
            return;
        }

        foreach (var name in names)
        {
            if (!name.StartsWith(ResourceNamePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = name.Substring(ResourceNamePrefix.Length);
            var slash = rest.IndexOf('/');
            if (slash <= 0)
            {
                continue;
            }

            var alias = rest.Substring(0, slash);
            if (!TryNormalizeRelative(rest.Substring(slash + 1), out var relative) ||
                !IsValidAlias(alias) ||
                string.IsNullOrEmpty(relative))
            {
                continue;
            }

            try
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null)
                {
                    continue;
                }

                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                if (!Folders.TryGetValue(alias, out var map))
                {
                    map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                    Folders[alias] = map;
                }

                map[relative] = ms.ToArray();
            }
            catch
            {
                // Skip unreadable resources.
            }
        }
    }

    private static bool TryNormalizeRelative(string relative, out string normalized)
    {
        normalized = "";
        var parts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();
        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count == 0)
                {
                    return false;
                }

                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            stack.Add(part);
        }

        normalized = string.Join("/", stack);
        return true;
    }
}
