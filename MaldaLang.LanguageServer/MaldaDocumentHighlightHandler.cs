// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Handles textDocument/documentHighlight: highlights all same-name identifiers in current file.
/// </summary>
public class MaldaDocumentHighlightHandler : IDocumentHighlightHandler
{
    private readonly DocumentStore _store;
    private readonly ISymbolNavigationService _symbolNavigationService;

    public MaldaDocumentHighlightHandler(DocumentStore store)
        : this(store, new SymbolNavigationService())
    {
    }

    public MaldaDocumentHighlightHandler(DocumentStore store, ISymbolNavigationService symbolNavigationService)
    {
        _store = store;
        _symbolNavigationService = symbolNavigationService;
    }

    public DocumentHighlightRegistrationOptions GetRegistrationOptions(DocumentHighlightCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentHighlightRegistrationOptions
        {
            DocumentSelector = MaldaLspDocuments.Selector
        };
    }

    public Task<DocumentHighlightContainer?> Handle(DocumentHighlightParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<DocumentHighlightContainer?>(new DocumentHighlightContainer());
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<DocumentHighlightContainer?>(new DocumentHighlightContainer());
        }

        try
        {
            var highlights = _symbolNavigationService.GetDocumentHighlights(text, request.Position.Line, request.Position.Character, uri.Path, cancellationToken)
                .Select(span => new DocumentHighlight
                {
                    Kind = DocumentHighlightKind.Text,
                    Range = SymbolNavigationLspMapper.ToRange(span)
                });

            return Task.FromResult<DocumentHighlightContainer?>(new DocumentHighlightContainer(highlights));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<DocumentHighlightContainer?>(new DocumentHighlightContainer());
        }
        catch
        {
            return Task.FromResult<DocumentHighlightContainer?>(new DocumentHighlightContainer());
        }
    }
}
