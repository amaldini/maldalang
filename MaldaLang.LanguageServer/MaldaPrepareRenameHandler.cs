// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

/// <summary>
/// Handles textDocument/prepareRename: validate symbol under cursor is renameable.
/// </summary>
public class MaldaPrepareRenameHandler : IPrepareRenameHandler
{
    private readonly DocumentStore _store;
    private readonly ISymbolNavigationService _symbolNavigationService;

    public MaldaPrepareRenameHandler(DocumentStore store)
        : this(store, new SymbolNavigationService())
    {
    }

    public MaldaPrepareRenameHandler(DocumentStore store, ISymbolNavigationService symbolNavigationService)
    {
        _store = store;
        _symbolNavigationService = symbolNavigationService;
    }

    public RenameRegistrationOptions GetRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities)
    {
        return new RenameRegistrationOptions
        {
            DocumentSelector = MaldaLspDocuments.Selector,
            PrepareProvider = true
        };
    }

    public Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<RangeOrPlaceholderRange?>(null);
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<RangeOrPlaceholderRange?>(null);
        }

        try
        {
            var target = _symbolNavigationService.PrepareRename(text, request.Position.Line, request.Position.Character, uri.Path, cancellationToken);
            if (target == null)
            {
                return Task.FromResult<RangeOrPlaceholderRange?>(null);
            }

            LspRange range = SymbolNavigationLspMapper.ToRange(target.Span);
            return Task.FromResult<RangeOrPlaceholderRange?>(range);
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<RangeOrPlaceholderRange?>(null);
        }
        catch
        {
            return Task.FromResult<RangeOrPlaceholderRange?>(null);
        }
    }
}
