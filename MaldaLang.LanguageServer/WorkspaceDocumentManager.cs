// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;

/// <summary>
/// LSP adapter over <see cref="WorkspaceFileSet"/>: converts <see cref="DocumentUri"/> to paths.
/// </summary>
public class WorkspaceDocumentManager
{
    private readonly WorkspaceFileSet _files = new();

    public void SetOpenDocument(DocumentUri uri, string text)
    {
        var path = TryGetFileSystemPath(uri);
        if (path == null)
        {
            return;
        }

        _files.SetOpenDocument(path, text);
    }

    public void RemoveOpenDocument(DocumentUri uri)
    {
        var path = TryGetFileSystemPath(uri);
        if (path == null)
        {
            return;
        }

        _files.RemoveOpenDocument(path);
    }

    public IReadOnlyList<WorkspaceDocumentInfo> GetWorkspaceDocuments(CancellationToken cancellationToken = default) =>
        _files.GetDocuments(cancellationToken);

    public IReadOnlyList<WorkspaceDocumentInfo> GetWorkspaceDocumentsFor(DocumentUri uri, CancellationToken cancellationToken = default)
    {
        var path = TryGetFileSystemPath(uri);
        if (path == null)
        {
            return [];
        }

        return _files.GetDocumentsFor(path, cancellationToken);
    }

    public DocumentUri CreateDocumentUri(string sourceKey)
    {
        var normalizedPath = Path.GetFullPath(sourceKey).Replace('\\', '/');
        if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
        {
            normalizedPath = "/" + normalizedPath;
        }

        return new DocumentUri("file", "", normalizedPath, null, null, null);
    }

    private static string? TryGetFileSystemPath(DocumentUri uri)
    {
        return TryGetFileSystemPath(uri, out var path) ? path : null;
    }

    /// <summary>
    /// Source key for the <c>sourceFileName</c> parameter of the IDE navigation services.
    /// A <c>file:</c> URI yields its real filesystem path so relative <c>include</c> /
    /// <c>import</c> resolution bases on the document's own directory; anything else falls
    /// back to <see cref="DocumentUri.Path"/>. Centralized so the handlers cannot drift.
    /// </summary>
    public static string GetSourceKey(DocumentUri uri)
    {
        return TryGetFileSystemPath(uri, out var path) ? path : uri.Path;
    }

    /// <summary>
    /// Filesystem path for a <c>file:</c> document URI, or false when the URI is not a
    /// local file. Unlike <see cref="DocumentUri.Path"/> the leading slash of
    /// <c>/C:/…</c> is stripped, so relative module / include resolution gets a real path.
    /// </summary>
    public static bool TryGetFileSystemPath(DocumentUri uri, out string path)
    {
        path = string.Empty;
        var raw = uri.Path;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = Uri.UnescapeDataString(raw);
        if (raw.Length > 2 && raw[0] == '/' && raw[2] == ':')
        {
            raw = raw[1..];
        }

        raw = raw.Replace('/', Path.DirectorySeparatorChar);
        try
        {
            path = Path.GetFullPath(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
