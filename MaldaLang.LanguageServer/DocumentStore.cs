// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MaldaLang;
using MaldaLang.Parser;
using OmniSharp.Extensions.LanguageServer.Protocol;

/// <summary>
/// In-memory store of open document URIs to their latest text content.
/// Updated on textDocument/didOpen and textDocument/didChange, cleared on didClose.
/// </summary>
public class DocumentStore
{
    private sealed class DocumentEntry
    {
        public string Text { get; set; } = string.Empty;
        public List<Token>? Tokens { get; set; }
    }

    private readonly ConcurrentDictionary<DocumentUri, DocumentEntry> _documents = new();

    public void Set(DocumentUri uri, string text)
    {
        _documents[uri] = new DocumentEntry { Text = text ?? string.Empty };
    }

    public string? Get(DocumentUri uri)
    {
        return _documents.TryGetValue(uri, out var entry) ? entry.Text : null;
    }

    public void Remove(DocumentUri uri)
    {
        _documents.TryRemove(uri, out _);
    }

    public bool TryGet(DocumentUri uri, out string? text)
    {
        var ok = _documents.TryGetValue(uri, out var entry);
        text = entry?.Text;
        return ok;
    }

    public bool TryGetTokens(DocumentUri uri, CancellationToken cancellationToken, out List<Token>? tokens)
    {
        tokens = null;
        if (!_documents.TryGetValue(uri, out var entry))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (entry.Tokens != null)
        {
            tokens = entry.Tokens;
            return true;
        }

        try
        {
            var lexer = new Lexer(entry.Text);
            var parsedTokens = lexer.Tokenize();
            cancellationToken.ThrowIfCancellationRequested();
            entry.Tokens = parsedTokens;
            tokens = parsedTokens;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
