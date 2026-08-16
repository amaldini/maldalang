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
        var path = uri.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        path = Uri.UnescapeDataString(path);
        if (path.Length > 2 && path[0] == '/' && path[2] == ':')
        {
            path = path[1..];
        }

        path = path.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(path);
    }
}
