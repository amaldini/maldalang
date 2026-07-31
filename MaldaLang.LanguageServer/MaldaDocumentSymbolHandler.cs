// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Server;
using MaldaLang.LanguageServer.OmniSharpShim;
public class MaldaDocumentSymbolHandler : IDocumentSymbolHandler
{
    private readonly DocumentStore _store;
    private readonly ISymbolNavigationService _symbolNavigationService;

    public MaldaDocumentSymbolHandler(DocumentStore store)
        : this(store, new SymbolNavigationService())
    {
    }

    public MaldaDocumentSymbolHandler(DocumentStore store, ISymbolNavigationService symbolNavigationService)
    {
        _store = store;
        _symbolNavigationService = symbolNavigationService;
    }

    public Task<Container<DocumentSymbol>?> Handle(DocumentSymbolParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<Container<DocumentSymbol>?>(new Container<DocumentSymbol>());
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<Container<DocumentSymbol>?>(null);
        }

        try
        {
            var symbols = _symbolNavigationService.GetDocumentSymbols(text, uri.Path, cancellationToken);
            var lspSymbols = symbols.Select(SymbolNavigationLspMapper.ToDocumentSymbol);
            return Task.FromResult<Container<DocumentSymbol>?>(new Container<DocumentSymbol>(lspSymbols));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<Container<DocumentSymbol>?>(new Container<DocumentSymbol>());
        }
        catch
        {
            return Task.FromResult<Container<DocumentSymbol>?>(new Container<DocumentSymbol>());
        }

    }
}
