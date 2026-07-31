// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using MaldaLang.LanguageServer.OmniSharpShim;

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

    public Task<Container<DocumentHighlight>?> Handle(DocumentHighlightParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<Container<DocumentHighlight>?>(new Container<DocumentHighlight>());
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<Container<DocumentHighlight>?>(new Container<DocumentHighlight>());
        }

        try
        {
            var highlights = _symbolNavigationService.GetDocumentHighlights(text, request.Position.Line, request.Position.Character, uri.Path, cancellationToken)
                .Select(span => new DocumentHighlight
                {
                    Kind = DocumentHighlightKind.Text,
                    Range = SymbolNavigationLspMapper.ToRange(span)
                });

            return Task.FromResult<Container<DocumentHighlight>?>(new Container<DocumentHighlight>(highlights));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<Container<DocumentHighlight>?>(new Container<DocumentHighlight>());
        }
        catch
        {
            return Task.FromResult<Container<DocumentHighlight>?>(new Container<DocumentHighlight>());
        }
    }
}
