// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Generic;
using MaldaLang.Formatting;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Handles textDocument/formatting and textDocument/rangeFormatting with <see cref="MaldaFormatter"/>
/// (the same formatter as <c>malda fmt</c>). Sources the lexer rejects get no edits.
/// </summary>
public class MaldaDocumentFormattingHandler : IDocumentFormattingHandler, IDocumentRangeFormattingHandler
{
    private readonly DocumentStore _store;

    public MaldaDocumentFormattingHandler(DocumentStore store)
    {
        _store = store;
    }

    public DocumentFormattingRegistrationOptions GetRegistrationOptions(DocumentFormattingCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentFormattingRegistrationOptions
        {
            DocumentSelector = MaldaLspDocuments.Selector
        };
    }

    public DocumentRangeFormattingRegistrationOptions GetRegistrationOptions(DocumentRangeFormattingCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentRangeFormattingRegistrationOptions
        {
            DocumentSelector = MaldaLspDocuments.Selector
        };
    }

    public Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<TextEditContainer?>(new TextEditContainer());
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult<TextEditContainer?>(new TextEditContainer());
        }

        var edits = ToLspEdits(MaldaFormatter.GetDocumentEdits(text, ToOptions(request.Options)));
        return Task.FromResult<TextEditContainer?>(new TextEditContainer(edits));
    }

    public Task<TextEditContainer> Handle(DocumentRangeFormattingParams request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new TextEditContainer());
        }

        var uri = request.TextDocument.Uri;
        var text = _store.Get(uri);
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(new TextEditContainer());
        }

        var startLine = request.Range.Start.Line;
        var endLine = request.Range.End.Line;
        if (request.Range.End.Character == 0 && endLine > startLine)
            endLine--;
        var edits = ToLspEdits(MaldaFormatter.GetRangeEdits(text, startLine, endLine, ToOptions(request.Options)));
        return Task.FromResult(new TextEditContainer(edits));
    }

    private static MaldaFormatOptions ToOptions(FormattingOptions? options)
    {
        var tabSize = options?.TabSize ?? 4;
        return new MaldaFormatOptions
        {
            IndentSize = tabSize > 0 ? tabSize : 4,
            InsertSpaces = options?.InsertSpaces ?? true
        };
    }

    private static List<TextEdit> ToLspEdits(List<MaldaFormatEdit> edits)
    {
        return edits.Select(edit => new TextEdit
        {
            Range = new Range(
                new Position(edit.StartLine, edit.StartColumn),
                new Position(edit.EndLine, edit.EndColumn)),
            NewText = edit.NewText
        }).ToList();
    }
}
