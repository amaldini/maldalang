// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Server;
using MaldaLang.LanguageServer.OmniSharpShim;
public class MaldaCompletionHandler : ICompletionHandler
{
    private readonly DocumentStore _store;
    private readonly ILanguageService _languageService;

    public MaldaCompletionHandler(DocumentStore store, ILanguageService languageService)
    {
        _store = store;
        _languageService = languageService;
    }

    public Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new CompletionList());
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(new CompletionList());
        }

        var position = request.Position;
        var line = position.Line;
        var character = position.Character;
        List<MaldaLang.IDE.Models.CompletionItem> completions;
        try
        {
            string? sourceFileName = null;
            var uriString = uri.ToString();
            if (uriString.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                sourceFileName = new Uri(uriString).LocalPath;
            completions = _languageService.GetCompletions(text, line, character, sourceFileName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(new CompletionList());
        }

        var items = completions.Select(c => new CompletionItem
        {
            Label = c.Label,
            Kind = MapCompletionKind(c.Kind),
            Detail = c.Detail,
            Documentation = c.Documentation != null ? new StringOrMarkupContent(c.Documentation) : null,
            InsertText = c.InsertText ?? c.Label
        }).ToList();

        return Task.FromResult(new CompletionList(items));
    }

    private static CompletionItemKind MapCompletionKind(string kind)
    {
        return kind?.ToLowerInvariant() switch
        {
            "class" => CompletionItemKind.Class,
            "function" or "method" => CompletionItemKind.Function,
            "variable" => CompletionItemKind.Variable,
            "keyword" => CompletionItemKind.Keyword,
            "property" => CompletionItemKind.Property,
            "decorator" or "event" => CompletionItemKind.Event,
            "constructor" => CompletionItemKind.Constructor,
            "interface" => CompletionItemKind.Interface,
            "enum" => CompletionItemKind.Enum,
            "constant" => CompletionItemKind.Constant,
            "module" or "namespace" => CompletionItemKind.Module,
            _ => CompletionItemKind.Text
        };
    }
}
