// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Concurrent;
using MaldaLang.IDE.Models;

/// <summary>
/// Tracks open MALDA buffers and lazily discovers sibling <c>.malda</c> files from disk.
/// Path-based so Desktop IDE and <c>malda-lsp</c> can share the same workspace scan.
/// </summary>
public sealed class WorkspaceFileSet
{
    private static readonly string[] RootMarkerDirectories = [".git", ".cursor"];
    private static readonly string[] IgnoredDirectoryNames = ["bin", "obj", ".git", ".cursor", ".vs", "node_modules"];

    private sealed class CachedDiskDocument
    {
        public DateTime LastWriteUtc { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    private sealed class CachedScan
    {
        public DateTime ScannedAtUtc { get; set; }
        public List<string> Files { get; set; } = [];
    }

    private sealed class WorkspaceRootRegistration
    {
        public string Path { get; set; } = string.Empty;
        public bool AllowDiskScan { get; set; }
    }

    private readonly ConcurrentDictionary<string, string> _openDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedDiskDocument> _diskDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedScan> _rootScans = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _workspaceRoots = new(StringComparer.OrdinalIgnoreCase);

    public void SetOpenDocument(string path, string text)
    {
        var normalized = TryNormalizePath(path);
        if (normalized == null)
        {
            return;
        }

        _openDocuments[normalized] = text ?? string.Empty;
        RegisterWorkspaceRoot(normalized);
    }

    public void RemoveOpenDocument(string path)
    {
        var normalized = TryNormalizePath(path);
        if (normalized == null)
        {
            return;
        }

        _openDocuments.TryRemove(normalized, out _);
        RegisterWorkspaceRoot(normalized);
    }

    public IReadOnlyList<WorkspaceDocumentInfo> GetDocuments(CancellationToken cancellationToken = default)
    {
        var documents = new Dictionary<string, WorkspaceDocumentInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in _workspaceRoots.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var document in GetDocumentsForRoot(root.Key, root.Value, cancellationToken))
            {
                documents[document.SourceKey] = document;
            }
        }

        foreach (var openDocument in _openDocuments)
        {
            documents[openDocument.Key] = new WorkspaceDocumentInfo
            {
                SourceKey = openDocument.Key,
                Text = openDocument.Value
            };
        }

        return documents.Values.ToList();
    }

    public IReadOnlyList<WorkspaceDocumentInfo> GetDocumentsFor(string filePath, CancellationToken cancellationToken = default)
    {
        var normalized = TryNormalizePath(filePath);
        if (normalized == null)
        {
            return [];
        }

        var root = RegisterWorkspaceRoot(normalized);
        return GetDocumentsForRoot(root.Path, root.AllowDiskScan, cancellationToken);
    }

    private IReadOnlyList<WorkspaceDocumentInfo> GetDocumentsForRoot(string root, bool allowDiskScan, CancellationToken cancellationToken)
    {
        var documents = new Dictionary<string, WorkspaceDocumentInfo>(StringComparer.OrdinalIgnoreCase);
        if (allowDiskScan)
        {
            foreach (var path in EnumerateWorkspaceFiles(root, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_openDocuments.TryGetValue(path, out var openText))
                {
                    documents[path] = new WorkspaceDocumentInfo
                    {
                        SourceKey = path,
                        Text = openText
                    };
                    continue;
                }

                var text = ReadDiskDocument(path);
                if (text == null)
                {
                    continue;
                }

                documents[path] = new WorkspaceDocumentInfo
                {
                    SourceKey = path,
                    Text = text
                };
            }
        }

        foreach (var openDocument in _openDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsUnderRoot(openDocument.Key, root))
            {
                documents[openDocument.Key] = new WorkspaceDocumentInfo
                {
                    SourceKey = openDocument.Key,
                    Text = openDocument.Value
                };
            }
        }

        return documents.Values.ToList();
    }

    private IEnumerable<string> EnumerateWorkspaceFiles(string root, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_rootScans.TryGetValue(root, out var cachedScan) &&
            now - cachedScan.ScannedAtUtc < TimeSpan.FromSeconds(2))
        {
            return cachedScan.Files;
        }

        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            IEnumerable<string> childDirectories;
            IEnumerable<string> maldaFiles;

            try
            {
                childDirectories = Directory.EnumerateDirectories(current);
                maldaFiles = Directory.EnumerateFiles(current, "*.malda", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in maldaFiles)
            {
                files.Add(Path.GetFullPath(file));
            }

            foreach (var child in childDirectories)
            {
                var directoryName = Path.GetFileName(child);
                if (IgnoredDirectoryNames.Contains(directoryName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                pending.Push(child);
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        _rootScans[root] = new CachedScan
        {
            ScannedAtUtc = now,
            Files = files
        };

        return files;
    }

    private WorkspaceRootRegistration RegisterWorkspaceRoot(string filePath)
    {
        var root = DetermineWorkspaceRoot(filePath);
        _workspaceRoots.AddOrUpdate(root.Path, root.AllowDiskScan, (_, existingAllowDiskScan) => existingAllowDiskScan || root.AllowDiskScan);
        return root;
    }

    private static WorkspaceRootRegistration DetermineWorkspaceRoot(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var current = Directory.Exists(filePath)
            ? Path.GetFullPath(filePath)
            : Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(current))
        {
            return new WorkspaceRootRegistration
            {
                Path = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory,
                AllowDiskScan = File.Exists(fullPath)
            };
        }

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.EnumerateFiles(current, "*.sln", SearchOption.TopDirectoryOnly).Any() ||
                RootMarkerDirectories.Any(marker => Directory.Exists(Path.Combine(current, marker))))
            {
                // The language engine repo is a C# workspace, not one MALDA program.
                // Scanning Examples/ from here floods the editor with thousands of
                // unrelated diagnostics. Keep only the open buffer (empty workspace).
                if (IsMaldaLanguageEngineRepo(current))
                {
                    return new WorkspaceRootRegistration
                    {
                        Path = Path.GetDirectoryName(fullPath) ?? current,
                        AllowDiskScan = false
                    };
                }

                return new WorkspaceRootRegistration
                {
                    Path = current,
                    AllowDiskScan = true
                };
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        var fallbackDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        return new WorkspaceRootRegistration
        {
            Path = fallbackDirectory,
            AllowDiskScan = File.Exists(fullPath) ||
                Directory.EnumerateFiles(fallbackDirectory, "*.malda", SearchOption.TopDirectoryOnly).Any()
        };
    }

    private string? ReadDiskDocument(string path)
    {
        try
        {
            var lastWriteUtc = File.GetLastWriteTimeUtc(path);
            if (_diskDocuments.TryGetValue(path, out var cachedDocument) &&
                cachedDocument.LastWriteUtc == lastWriteUtc)
            {
                return cachedDocument.Text;
            }

            var text = File.ReadAllText(path);
            _diskDocuments[path] = new CachedDiskDocument
            {
                LastWriteUtc = lastWriteUtc,
                Text = text
            };
            return text;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsMaldaLanguageEngineRepo(string root)
    {
        return File.Exists(Path.Combine(root, "MaldaLang.sln")) &&
            Directory.Exists(Path.Combine(root, "Examples")) &&
            Directory.Exists(Path.Combine(root, "MaldaLang"));
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedPath, normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryNormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }
}
