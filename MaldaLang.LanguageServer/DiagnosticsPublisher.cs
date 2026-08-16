// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

/// <summary>
/// Abstraction for publishing LSP diagnostics. Set the inner implementation after the server is built.
/// </summary>
public interface IDiagnosticsPublisher
{
    void Publish(DocumentUri uri, Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic> diagnostics);
}

/// <summary>
/// Implementation that forwards to the language server facade once set. Registered as singleton;
/// Program.cs sets the inner after LanguageServer.From() returns.
/// </summary>
public sealed class DiagnosticsPublisher : IDiagnosticsPublisher
{
    public static ITextDocumentLanguageServer? InnerTextDocument { get; set; }

    public void Publish(DocumentUri uri, Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic> diagnostics)
    {
        var textDocument = InnerTextDocument;
        if (textDocument == null)
            return;

        textDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = diagnostics
        });
    }
}
