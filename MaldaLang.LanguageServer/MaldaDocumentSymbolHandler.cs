// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
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

    public DocumentSymbolRegistrationOptions GetRegistrationOptions(DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentSymbolRegistrationOptions
        {
            DocumentSelector = MaldaLspDocuments.Selector
        };
    }

    public Task<SymbolInformationOrDocumentSymbolContainer?> Handle(DocumentSymbolParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(new SymbolInformationOrDocumentSymbolContainer());
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);
        }

        try
        {
            var symbols = _symbolNavigationService.GetDocumentSymbols(text, uri.Path, cancellationToken);
            var lspSymbols = symbols.Select(symbol =>
                (SymbolInformationOrDocumentSymbol)SymbolNavigationLspMapper.ToDocumentSymbol(symbol));
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
                new SymbolInformationOrDocumentSymbolContainer(lspSymbols));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(new SymbolInformationOrDocumentSymbolContainer());
        }
        catch
        {
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(new SymbolInformationOrDocumentSymbolContainer());
        }

    }
}
