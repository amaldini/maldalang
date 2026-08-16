// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Generic;
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
        var edits = FormatLines(lines, 0, lines.Length, request.Options, cancellationToken);
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
        var edits = FormatLines(lines, startLine, endLine + 1, request.Options, cancellationToken);
        return Task.FromResult(new TextEditContainer(edits));
    }

    private static List<TextEdit> FormatLines(string[] lines, int startLine, int endLine, FormattingOptions options, CancellationToken cancellationToken)
    {
        var indentSize = options.TabSize > 0 ? options.TabSize : 4;
        var indentStr = options.InsertSpaces ? new string(' ', indentSize) : "\t";
        var edits = new List<TextEdit>();
        var depth = 0;
        for (var i = 0; i < startLine && i < lines.Length; i++)
        {
            UpdateDepth(lines[i], ref depth);
        }

        for (var i = startLine; i < endLine && i < lines.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return edits;
            }

            var line = lines[i];
            var trimmed = line.TrimStart();
            var expectedIndent = options.InsertSpaces
                ? (depth * indentSize > 0 ? new string(' ', depth * indentSize) : "")
                : (depth > 0 ? new string('\t', depth) : "");

            var currentLeading = line.Length - trimmed.Length;
            var currentIndent = currentLeading > 0 ? line.Substring(0, currentLeading) : "";
            if (currentIndent != expectedIndent)
            {
                edits.Add(new TextEdit
                {
                    Range = new Range(new Position(i, 0), new Position(i, currentLeading)),
                    NewText = expectedIndent
                });
            }

            UpdateDepth(line, ref depth);
        }

        return edits;
    }

    private static void UpdateDepth(string line, ref int depth)
    {
        foreach (var c in line)
        {
            if (c == '{') depth++;
            else if (c == '}') depth--;
        }
        if (depth < 0) depth = 0;
    }
}
