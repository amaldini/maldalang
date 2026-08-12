// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.IDE.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace MaldaLang.LanguageServer;

internal static class SymbolNavigationLspMapper
{
    public static LspRange ToRange(TextSpanInfo span)
    {
        return new LspRange(
            new Position(span.Line, span.Column),
            new Position(span.Line, span.Column + span.Length));
    }

    public static Location ToLocation(SymbolLocation location, DocumentUri uri)
    {
        return new Location
        {
            Uri = uri,
            Range = ToRange(location.Span)
        };
    }

    public static SymbolKind ToSymbolKind(SymbolItemKind kind)
    {
        return kind switch
        {
            SymbolItemKind.Class => SymbolKind.Class,
            SymbolItemKind.Function => SymbolKind.Function,
            SymbolItemKind.Method => SymbolKind.Method,
            SymbolItemKind.Field => SymbolKind.Field,
            SymbolItemKind.Variable => SymbolKind.Variable,
            SymbolItemKind.Actor => SymbolKind.Class,
            SymbolItemKind.Prompt => SymbolKind.Function,
            SymbolItemKind.Workflow => SymbolKind.Object,
            SymbolItemKind.Step => SymbolKind.Method,
            SymbolItemKind.Event => SymbolKind.Event,
            SymbolItemKind.Object => SymbolKind.Object,
            SymbolItemKind.Schema => SymbolKind.Struct,
            _ => SymbolKind.Object
        };
    }

    public static DocumentSymbol ToDocumentSymbol(DocumentSymbolInfo symbol)
    {
        return new DocumentSymbol
        {
            Name = symbol.Name,
            Detail = symbol.Detail,
            Kind = ToSymbolKind(symbol.Kind),
            Range = ToRange(symbol.Span),
            SelectionRange = ToRange(symbol.Span),
            Children = new Container<DocumentSymbol>(symbol.Children.Select(ToDocumentSymbol))
        };
    }
}
