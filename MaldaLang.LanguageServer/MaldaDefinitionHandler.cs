// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using MaldaLang.LanguageServer.OmniSharpShim;

/// <summary>
/// Handles textDocument/definition: resolve symbol at position to its declaration (single-file).
/// </summary>
public class MaldaDefinitionHandler : IDefinitionHandler
{
    private readonly DocumentStore _store;
    private readonly WorkspaceDocumentManager _workspaceDocuments;
    private readonly ISymbolNavigationService _symbolNavigationService;

    public MaldaDefinitionHandler(DocumentStore store)
        : this(store, new WorkspaceDocumentManager(), new SymbolNavigationService())
    {
    }

    public MaldaDefinitionHandler(DocumentStore store, WorkspaceDocumentManager workspaceDocuments, ISymbolNavigationService symbolNavigationService)
    {
        _store = store;
        _workspaceDocuments = workspaceDocuments;
        _symbolNavigationService = symbolNavigationService;
    }

    public Task<LocationOrLocationLinks> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new LocationOrLocationLinks());
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(new LocationOrLocationLinks());
        }

        try
        {
            var workspaceDocuments = _workspaceDocuments.GetWorkspaceDocumentsFor(uri, cancellationToken);
            var definition = workspaceDocuments.Count > 1
                ? _symbolNavigationService.GetWorkspaceDefinition(workspaceDocuments, text, request.Position.Line, request.Position.Character, uri.Path, cancellationToken)
                : _symbolNavigationService.GetDefinition(text, request.Position.Line, request.Position.Character, uri.Path, cancellationToken);
            if (definition == null)
            {
                return Task.FromResult(new LocationOrLocationLinks());
            }

            var locationUri = workspaceDocuments.Count > 1 && !string.IsNullOrWhiteSpace(definition.SourceKey)
                ? _workspaceDocuments.CreateDocumentUri(definition.SourceKey!)
                : uri;
            var location = SymbolNavigationLspMapper.ToLocation(definition, locationUri);
            return Task.FromResult(new LocationOrLocationLinks(location));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(new LocationOrLocationLinks());
        }
        catch
        {
            return Task.FromResult(new LocationOrLocationLinks());
        }
    }
}
