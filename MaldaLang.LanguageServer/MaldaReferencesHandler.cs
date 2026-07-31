// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using MaldaLang.LanguageServer.OmniSharpShim;

/// <summary>
/// Handles textDocument/references: return all references to the symbol at position (single-file).
/// </summary>
public class MaldaReferencesHandler : IReferencesHandler
{
    private readonly DocumentStore _store;
    private readonly WorkspaceDocumentManager _workspaceDocuments;
    private readonly ISymbolNavigationService _symbolNavigationService;

    public MaldaReferencesHandler(DocumentStore store)
        : this(store, new WorkspaceDocumentManager(), new SymbolNavigationService())
    {
    }

    public MaldaReferencesHandler(DocumentStore store, WorkspaceDocumentManager workspaceDocuments, ISymbolNavigationService symbolNavigationService)
    {
        _store = store;
        _workspaceDocuments = workspaceDocuments;
        _symbolNavigationService = symbolNavigationService;
    }

    public Task<Container<Location>?> Handle(ReferenceParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<Container<Location>?>(new Container<Location>());
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<Container<Location>?>(new Container<Location>());
        }

        try
        {
            var workspaceDocuments = _workspaceDocuments.GetWorkspaceDocumentsFor(uri, cancellationToken);
            var references = workspaceDocuments.Count > 1
                ? _symbolNavigationService.GetWorkspaceReferences(workspaceDocuments, text, request.Position.Line, request.Position.Character, uri.Path, cancellationToken)
                : _symbolNavigationService.GetReferences(text, request.Position.Line, request.Position.Character, uri.Path, cancellationToken);
            var locations = references.Select(reference =>
            {
                var locationUri = !string.IsNullOrWhiteSpace(reference.SourceKey)
                    ? _workspaceDocuments.CreateDocumentUri(reference.SourceKey!)
                    : uri;
                return SymbolNavigationLspMapper.ToLocation(reference, locationUri);
            });
            return Task.FromResult<Container<Location>?>(new Container<Location>(locations));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<Container<Location>?>(new Container<Location>());
        }
        catch
        {
            return Task.FromResult<Container<Location>?>(new Container<Location>());
        }
    }
}
