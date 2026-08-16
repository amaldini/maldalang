// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Generic;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Handles textDocument/formatting and textDocument/rangeFormatting: indent with spaces.
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

        var lines = text.Split('\n');
        var edits = ToLspEdits(MaldaIndentFormatter.GetIndentEdits(
            lines, 0, lines.Length, request.Options.TabSize, request.Options.InsertSpaces, cancellationToken));
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

        var lines = text.Split('\n');
        var startLine = request.Range.Start.Line;
        var endLine = request.Range.End.Line;
        if (request.Range.End.Character == 0 && endLine > startLine)
            endLine--;
        var edits = ToLspEdits(MaldaIndentFormatter.GetIndentEdits(
            lines, startLine, endLine + 1, request.Options.TabSize, request.Options.InsertSpaces, cancellationToken));
        return Task.FromResult(new TextEditContainer(edits));
    }

    private static List<TextEdit> ToLspEdits(List<TextEditInfo> edits)
    {
        return edits.Select(edit => new TextEdit
        {
            Range = new Range(
                new Position(edit.Span.Line, edit.Span.Column),
                new Position(edit.Span.Line, edit.Span.Column + edit.Span.Length)),
            NewText = edit.NewText
        }).ToList();
    }
}
