// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Server;
using MaldaLang.LanguageServer.OmniSharpShim;
public class MaldaHoverHandler : IHoverHandler
{
    private readonly DocumentStore _store;
    private readonly ILanguageService _languageService;

    public MaldaHoverHandler(DocumentStore store, ILanguageService languageService)
    {
        _store = store;
        _languageService = languageService;
    }

    public Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<Hover?>(null);
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<Hover?>(null);
        }

        var position = request.Position;
        var line = position.Line;
        var character = position.Character;
        string? info;
        try
        {
            string? sourceFileName = null;
            var uriString = uri.ToString();
            if (uriString.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                sourceFileName = new Uri(uriString).LocalPath;
            }

            info = _languageService.GetHoverInformation(text, line, character, sourceFileName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<Hover?>(null);
        }

        if (string.IsNullOrEmpty(info))
        {
            return Task.FromResult<Hover?>(null);
        }

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = info
            })
        });
    }
}
