// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using MaldaLang.LanguageServer.OmniSharpShim;
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

    public Task<LspRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<LspRange?>(null);
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<LspRange?>(null);
        }

        try
        {
            var target = _symbolNavigationService.PrepareRename(text, request.Position.Line, request.Position.Character, uri.Path, cancellationToken);
            if (target == null)
            {
                return Task.FromResult<LspRange?>(null);
            }

            return Task.FromResult<LspRange?>(SymbolNavigationLspMapper.ToRange(target.Span));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<LspRange?>(null);
        }
        catch
        {
            return Task.FromResult<LspRange?>(null);
        }
    }
}
