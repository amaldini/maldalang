// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

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
    public static object? InnerTextDocument { get; set; }

    public void Publish(DocumentUri uri, Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic> diagnostics)
    {
        var textDoc = InnerTextDocument;
        if (textDoc == null)
            return;
        var params_ = new OmniSharp.Extensions.LanguageServer.Protocol.Models.PublishDiagnosticsParams { Uri = uri, Diagnostics = diagnostics };
        var method = textDoc.GetType().GetMethod("PublishDiagnostics", new[] { typeof(OmniSharp.Extensions.LanguageServer.Protocol.Models.PublishDiagnosticsParams) });
        method?.Invoke(textDoc, new object[] { params_ });
    }
}
